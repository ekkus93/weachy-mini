#include "reachy_llama.h"

#include "llama.h"
#include "reachy_llama_internal.hpp"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

namespace
{
constexpr const char * kVersion = "rma130-abi1";
constexpr const char * kUpstreamRevision = "dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb";
constexpr std::size_t kMaxChatMessages = 4096U;
constexpr uint32_t kMaxStreamQueueCapacity = 4096U;
constexpr int32_t kMaxThreads = 128;

uint64_t MonotonicUs()
{
    const auto now = std::chrono::steady_clock::now().time_since_epoch();
    return static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(now).count());
}

void WriteError(reachy_llama_error_info * error, int32_t status, const char * message)
{
    if (error == nullptr)
    {
        return;
    }
    if (error->struct_size != 0U && error->struct_size != sizeof(reachy_llama_error_info))
    {
        return;
    }
    error->struct_size = sizeof(reachy_llama_error_info);
    error->status = status;
    std::memset(error->message, 0, sizeof(error->message));
    if (message != nullptr)
    {
        const std::size_t maximum = sizeof(error->message) - 1U;
        const std::size_t length = std::min(std::strlen(message), maximum);
        std::memcpy(error->message, message, length);
    }
}

int32_t Fail(reachy_llama_error_info * error, reachy_llama_status status, const char * message)
{
    const int32_t code = static_cast<int32_t>(status);
    WriteError(error, code, message);
    return code;
}

void ClearError(reachy_llama_error_info * error)
{
    WriteError(error, static_cast<int32_t>(REACHY_LLAMA_STATUS_OK), "");
}

bool IsValidModelConfig(const reachy_llama_model_config * config)
{
    return config != nullptr && config->struct_size == sizeof(reachy_llama_model_config) &&
           config->abi_version == REACHY_LLAMA_ABI_VERSION && config->reserved == 0U &&
           config->check_tensors <= 1U;
}

bool IsValidGenerationConfig(const reachy_llama_generation_config * config)
{
    if (config == nullptr || config->struct_size != sizeof(reachy_llama_generation_config) ||
        config->abi_version != REACHY_LLAMA_ABI_VERSION)
    {
        return false;
    }
    if (config->context_tokens < 64U || config->batch_tokens == 0U ||
        config->batch_tokens > config->context_tokens || config->micro_batch_tokens == 0U ||
        config->micro_batch_tokens > config->batch_tokens || config->max_generated_tokens == 0U ||
        config->max_generated_tokens >= config->context_tokens)
    {
        return false;
    }
    if (config->threads <= 0 || config->threads > kMaxThreads || config->batch_threads <= 0 ||
        config->batch_threads > kMaxThreads)
    {
        return false;
    }
    if (!std::isfinite(config->temperature) || config->temperature < 0.0F ||
        !std::isfinite(config->min_p) || config->min_p < 0.0F || config->min_p > 1.0F)
    {
        return false;
    }
    return config->stream_queue_capacity > 0U &&
           config->stream_queue_capacity <= kMaxStreamQueueCapacity;
}

void SilentLogger(enum ggml_log_level /* level */, const char * /* text */, void * /* user_data */)
{
}

void InitializeRuntime()
{
    static std::once_flag initialized;
    std::call_once(initialized, [] {
        llama_log_set(SilentLogger, nullptr);
        llama_backend_init();
    });
}

struct Model
{
    explicit Model(llama_model * model_value)
        : model(model_value)
    {
    }

    ~Model()
    {
        if (model != nullptr)
        {
            llama_model_free(model);
        }
    }

    Model(const Model &) = delete;
    Model & operator=(const Model &) = delete;

    llama_model * model;
    std::mutex mutex;
    bool available{true};
    uint32_t active_generations{0U};
};

struct Job
{
    Job(
        reachy_llama_generation_handle handle_value,
        std::shared_ptr<Model> model_value,
        reachy_llama_generation_config config_value,
        std::string prompt_value)
        : handle(handle_value),
          model(std::move(model_value)),
          config(config_value),
          prompt(std::move(prompt_value)),
          queue(static_cast<std::size_t>(config.stream_queue_capacity))
    {
    }

    ~Job()
    {
        if (worker.joinable())
        {
            worker.join();
        }
    }

    Job(const Job &) = delete;
    Job & operator=(const Job &) = delete;

    reachy_llama_generation_handle handle;
    std::shared_ptr<Model> model;
    reachy_llama_generation_config config;
    std::string prompt;
    reachy_llama_internal::BoundedStreamQueue queue;
    std::thread worker;
    std::atomic<bool> cancel_requested{false};
    std::atomic<bool> finished{false};
    std::atomic<uint32_t> state{REACHY_LLAMA_GENERATION_STATE_RUNNING};
    std::atomic<uint64_t> prompt_tokens{0U};
    std::atomic<uint64_t> generated_tokens{0U};
    std::atomic<uint64_t> started_us{0U};
    std::atomic<uint64_t> first_text_us{0U};
    std::atomic<uint64_t> finished_us{0U};
    std::mutex poll_mutex;
    std::mutex terminal_mutex;
    uint32_t terminal_type{REACHY_LLAMA_GENERATION_EVENT_ERROR};
    int32_t terminal_status{REACHY_LLAMA_STATUS_INTERNAL_ERROR};
    std::string terminal_message{"generation terminated unexpectedly"};
    bool terminal_delivered{false};
};

std::mutex g_registry_mutex;
std::unordered_map<reachy_llama_model_handle, std::shared_ptr<Model>> g_models;
std::unordered_map<reachy_llama_generation_handle, std::shared_ptr<Job>> g_jobs;
std::atomic<uint64_t> g_next_handle{1U};

uint64_t NextHandle()
{
    for (;;)
    {
        const uint64_t candidate = g_next_handle.fetch_add(1U, std::memory_order_relaxed);
        if (candidate != 0U)
        {
            return candidate;
        }
    }
}

std::shared_ptr<Model> FindModel(reachy_llama_model_handle handle)
{
    std::lock_guard<std::mutex> lock(g_registry_mutex);
    const auto iterator = g_models.find(handle);
    return iterator == g_models.end() ? nullptr : iterator->second;
}

std::shared_ptr<Job> FindJob(reachy_llama_generation_handle handle)
{
    std::lock_guard<std::mutex> lock(g_registry_mutex);
    const auto iterator = g_jobs.find(handle);
    return iterator == g_jobs.end() ? nullptr : iterator->second;
}

bool AbortDecode(void * user_data)
{
    const auto * requested = static_cast<const std::atomic<bool> *>(user_data);
    return requested != nullptr && requested->load(std::memory_order_acquire);
}

void SetTerminal(
    const std::shared_ptr<Job> & job,
    uint32_t type,
    reachy_llama_status status,
    uint32_t state,
    std::string message)
{
    {
        std::lock_guard<std::mutex> lock(job->terminal_mutex);
        job->terminal_type = type;
        job->terminal_status = static_cast<int32_t>(status);
        job->terminal_message = std::move(message);
    }
    job->state.store(state, std::memory_order_release);
}

std::vector<llama_token> TokenizePrompt(
    const llama_vocab * vocab,
    const std::string & prompt,
    reachy_llama_status & status,
    std::string & message)
{
    if (prompt.size() > static_cast<std::size_t>(std::numeric_limits<int32_t>::max()))
    {
        status = REACHY_LLAMA_STATUS_TOKENIZE_FAILED;
        message = "prompt text exceeds the supported tokenizer input size";
        return {};
    }
    const int32_t prompt_length = static_cast<int32_t>(prompt.size());
    const int32_t required = llama_tokenize(
        vocab,
        prompt.c_str(),
        prompt_length,
        nullptr,
        0,
        true,
        true);
    const int64_t token_count = required < 0 ? -static_cast<int64_t>(required)
                                             : static_cast<int64_t>(required);
    if (token_count <= 0 || token_count > std::numeric_limits<int32_t>::max())
    {
        status = REACHY_LLAMA_STATUS_TOKENIZE_FAILED;
        message = "llama.cpp could not determine a bounded prompt token count";
        return {};
    }

    std::vector<llama_token> tokens(static_cast<std::size_t>(token_count));
    const int32_t actual = llama_tokenize(
        vocab,
        prompt.c_str(),
        prompt_length,
        tokens.data(),
        static_cast<int32_t>(tokens.size()),
        true,
        true);
    if (actual < 0 || static_cast<std::size_t>(actual) > tokens.size())
    {
        status = REACHY_LLAMA_STATUS_TOKENIZE_FAILED;
        message = "llama.cpp failed to tokenize the prompt";
        return {};
    }
    tokens.resize(static_cast<std::size_t>(actual));
    return tokens;
}

bool DecodeBatch(
    const std::shared_ptr<Job> & job,
    llama_context * context,
    llama_token * tokens,
    std::size_t count,
    reachy_llama_status & status,
    std::string & message)
{
    if (count == 0U || count > static_cast<std::size_t>(std::numeric_limits<int32_t>::max()))
    {
        status = REACHY_LLAMA_STATUS_INTERNAL_ERROR;
        message = "invalid internal decode batch size";
        return false;
    }
    const llama_batch batch = llama_batch_get_one(tokens, static_cast<int32_t>(count));
    const int32_t result = llama_decode(context, batch);
    if (job->cancel_requested.load(std::memory_order_acquire))
    {
        status = REACHY_LLAMA_STATUS_CANCELLED;
        message = "generation cancelled";
        return false;
    }
    if (result != 0)
    {
        status = REACHY_LLAMA_STATUS_DECODE_FAILED;
        message = "llama.cpp decode failed";
        return false;
    }
    return true;
}

std::string TokenPiece(
    const llama_vocab * vocab,
    llama_token token,
    reachy_llama_status & status,
    std::string & message)
{
    std::vector<char> buffer(256U);
    int32_t result = llama_token_to_piece(
        vocab,
        token,
        buffer.data(),
        static_cast<int32_t>(buffer.size()),
        0,
        true);
    if (result < 0)
    {
        const int64_t required = -static_cast<int64_t>(result);
        if (required <= 0 || required > std::numeric_limits<int32_t>::max())
        {
            status = REACHY_LLAMA_STATUS_INTERNAL_ERROR;
            message = "generated token piece exceeded the supported size";
            return {};
        }
        buffer.resize(static_cast<std::size_t>(required));
        result = llama_token_to_piece(
            vocab,
            token,
            buffer.data(),
            static_cast<int32_t>(buffer.size()),
            0,
            true);
    }
    if (result < 0 || static_cast<std::size_t>(result) > buffer.size())
    {
        status = REACHY_LLAMA_STATUS_INTERNAL_ERROR;
        message = "llama.cpp failed to render a generated token";
        return {};
    }
    return std::string(buffer.data(), static_cast<std::size_t>(result));
}

void RunGeneration(const std::shared_ptr<Job> & job)
{
    reachy_llama_status status = REACHY_LLAMA_STATUS_OK;
    std::string message;
    const llama_model * const_model = job->model->model;
    llama_model * model = job->model->model;

    if (llama_model_has_encoder(const_model))
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_UNSUPPORTED_MODEL,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "RMA-130 supports decoder-only text generation");
        return;
    }

    const llama_vocab * vocab = llama_model_get_vocab(const_model);
    if (vocab == nullptr)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_UNSUPPORTED_MODEL,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "loaded model has no usable vocabulary");
        return;
    }

    std::vector<llama_token> prompt_tokens = TokenizePrompt(vocab, job->prompt, status, message);
    if (status != REACHY_LLAMA_STATUS_OK)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            status,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            std::move(message));
        return;
    }
    job->prompt_tokens.store(prompt_tokens.size(), std::memory_order_release);

    const uint64_t total_budget = static_cast<uint64_t>(prompt_tokens.size()) +
                                  static_cast<uint64_t>(job->config.max_generated_tokens);
    if (total_budget > static_cast<uint64_t>(job->config.context_tokens))
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_CONTEXT_LIMIT,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "prompt plus requested output exceeds the configured context");
        return;
    }

    llama_context_params context_params = llama_context_default_params();
    context_params.n_ctx = job->config.context_tokens;
    context_params.n_batch = job->config.batch_tokens;
    context_params.n_ubatch = job->config.micro_batch_tokens;
    context_params.n_threads = job->config.threads;
    context_params.n_threads_batch = job->config.batch_threads;
    context_params.abort_callback = AbortDecode;
    context_params.abort_callback_data = &job->cancel_requested;
    context_params.no_perf = true;

    llama_context * context = llama_init_from_model(model, context_params);
    if (context == nullptr)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_CONTEXT_CREATE_FAILED,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "llama.cpp could not create the generation context");
        return;
    }

    llama_sampler * sampler = nullptr;
    try
    {
        sampler = llama_sampler_chain_init(llama_sampler_chain_default_params());
        if (sampler == nullptr)
        {
            throw std::bad_alloc();
        }
        if (job->config.temperature <= 0.0F)
        {
            llama_sampler * greedy = llama_sampler_init_greedy();
            if (greedy == nullptr)
            {
                throw std::bad_alloc();
            }
            llama_sampler_chain_add(sampler, greedy);
        }
        else
        {
            llama_sampler * min_p = llama_sampler_init_min_p(job->config.min_p, 1U);
            llama_sampler * temperature = llama_sampler_init_temp(job->config.temperature);
            llama_sampler * distribution = llama_sampler_init_dist(job->config.seed);
            if (min_p == nullptr || temperature == nullptr || distribution == nullptr)
            {
                if (min_p != nullptr)
                {
                    llama_sampler_free(min_p);
                }
                if (temperature != nullptr)
                {
                    llama_sampler_free(temperature);
                }
                if (distribution != nullptr)
                {
                    llama_sampler_free(distribution);
                }
                throw std::bad_alloc();
            }
            llama_sampler_chain_add(sampler, min_p);
            llama_sampler_chain_add(sampler, temperature);
            llama_sampler_chain_add(sampler, distribution);
        }

        std::size_t offset = 0U;
        while (offset < prompt_tokens.size())
        {
            if (job->cancel_requested.load(std::memory_order_acquire))
            {
                status = REACHY_LLAMA_STATUS_CANCELLED;
                message = "generation cancelled";
                break;
            }
            const std::size_t remaining = prompt_tokens.size() - offset;
            const std::size_t count = std::min(
                remaining,
                static_cast<std::size_t>(job->config.batch_tokens));
            if (!DecodeBatch(job, context, prompt_tokens.data() + offset, count, status, message))
            {
                break;
            }
            offset += count;
        }

        uint64_t sequence = 1U;
        for (uint32_t generated = 0U;
             status == REACHY_LLAMA_STATUS_OK && generated < job->config.max_generated_tokens;
             ++generated)
        {
            if (job->cancel_requested.load(std::memory_order_acquire))
            {
                status = REACHY_LLAMA_STATUS_CANCELLED;
                message = "generation cancelled";
                break;
            }

            const llama_token token = llama_sampler_sample(sampler, context, -1);
            if (llama_vocab_is_eog(vocab, token))
            {
                break;
            }

            std::string piece = TokenPiece(vocab, token, status, message);
            if (status != REACHY_LLAMA_STATUS_OK)
            {
                break;
            }
            if (!job->queue.Push(sequence, std::move(piece)))
            {
                status = REACHY_LLAMA_STATUS_CANCELLED;
                message = "generation cancelled";
                break;
            }
            if (job->first_text_us.load(std::memory_order_acquire) == 0U)
            {
                job->first_text_us.store(MonotonicUs(), std::memory_order_release);
            }
            job->generated_tokens.fetch_add(1U, std::memory_order_release);
            ++sequence;

            if (generated + 1U >= job->config.max_generated_tokens)
            {
                break;
            }
            llama_token next = token;
            if (!DecodeBatch(job, context, &next, 1U, status, message))
            {
                break;
            }
        }
    }
    catch (const std::bad_alloc &)
    {
        status = REACHY_LLAMA_STATUS_INTERNAL_ERROR;
        message = "native allocation failed during generation";
    }
    catch (...)
    {
        status = REACHY_LLAMA_STATUS_INTERNAL_ERROR;
        message = "unexpected native generation failure";
    }

    if (sampler != nullptr)
    {
        llama_sampler_free(sampler);
    }
    llama_free(context);

    if (status == REACHY_LLAMA_STATUS_CANCELLED ||
        job->cancel_requested.load(std::memory_order_acquire))
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_CANCELLED,
            REACHY_LLAMA_STATUS_CANCELLED,
            REACHY_LLAMA_GENERATION_STATE_CANCELLED,
            "generation cancelled");
    }
    else if (status != REACHY_LLAMA_STATUS_OK)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            status,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            std::move(message));
    }
    else
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_COMPLETED,
            REACHY_LLAMA_STATUS_OK,
            REACHY_LLAMA_GENERATION_STATE_COMPLETED,
            "");
    }
}

void WorkerMain(const std::shared_ptr<Job> & job)
{
    job->started_us.store(MonotonicUs(), std::memory_order_release);
    try
    {
        RunGeneration(job);
    }
    catch (const std::bad_alloc &)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "native allocation failed before generation completed");
    }
    catch (...)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "unexpected native worker failure");
    }

    {
        std::lock_guard<std::mutex> lock(job->model->mutex);
        if (job->model->active_generations > 0U)
        {
            --job->model->active_generations;
        }
    }
    job->finished_us.store(MonotonicUs(), std::memory_order_release);
    job->finished.store(true, std::memory_order_release);
}

int32_t ValidateErrorStruct(reachy_llama_error_info * error)
{
    if (error == nullptr || error->struct_size == 0U ||
        error->struct_size == sizeof(reachy_llama_error_info))
    {
        return REACHY_LLAMA_STATUS_OK;
    }
    return REACHY_LLAMA_STATUS_ABI_MISMATCH;
}

} // namespace

extern "C"
{
uint32_t reachy_llama_abi_version(void)
{
    return REACHY_LLAMA_ABI_VERSION;
}

const char * reachy_llama_version_string(void)
{
    return kVersion;
}

const char * reachy_llama_upstream_revision(void)
{
    return kUpstreamRevision;
}

const char * reachy_llama_status_string(int32_t status)
{
    switch (static_cast<reachy_llama_status>(status))
    {
        case REACHY_LLAMA_STATUS_OK:
            return "ok";
        case REACHY_LLAMA_STATUS_INVALID_ARGUMENT:
            return "invalid_argument";
        case REACHY_LLAMA_STATUS_ABI_MISMATCH:
            return "abi_mismatch";
        case REACHY_LLAMA_STATUS_NOT_FOUND:
            return "not_found";
        case REACHY_LLAMA_STATUS_BUSY:
            return "busy";
        case REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL:
            return "buffer_too_small";
        case REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED:
            return "model_load_failed";
        case REACHY_LLAMA_STATUS_TOKENIZE_FAILED:
            return "tokenize_failed";
        case REACHY_LLAMA_STATUS_TEMPLATE_FAILED:
            return "template_failed";
        case REACHY_LLAMA_STATUS_CONTEXT_CREATE_FAILED:
            return "context_create_failed";
        case REACHY_LLAMA_STATUS_CONTEXT_LIMIT:
            return "context_limit";
        case REACHY_LLAMA_STATUS_DECODE_FAILED:
            return "decode_failed";
        case REACHY_LLAMA_STATUS_UNSUPPORTED_MODEL:
            return "unsupported_model";
        case REACHY_LLAMA_STATUS_CANCELLED:
            return "cancelled";
        case REACHY_LLAMA_STATUS_INTERNAL_ERROR:
            return "internal_error";
    }
    return "unknown";
}

int32_t reachy_llama_default_model_config(reachy_llama_model_config * config)
{
    if (config == nullptr)
    {
        return REACHY_LLAMA_STATUS_INVALID_ARGUMENT;
    }
    *config = reachy_llama_model_config{
        sizeof(reachy_llama_model_config), REACHY_LLAMA_ABI_VERSION, 1U, 0U};
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_default_generation_config(reachy_llama_generation_config * config)
{
    if (config == nullptr)
    {
        return REACHY_LLAMA_STATUS_INVALID_ARGUMENT;
    }
    *config = reachy_llama_generation_config{
        sizeof(reachy_llama_generation_config),
        REACHY_LLAMA_ABI_VERSION,
        4096U,
        512U,
        128U,
        256U,
        2,
        2,
        0.8F,
        0.05F,
        0xFFFFFFFFU,
        64U};
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_model_load(
    const char * model_path_utf8,
    const reachy_llama_model_config * config,
    reachy_llama_model_handle * out_model,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (model_path_utf8 == nullptr || model_path_utf8[0] == '\0' || out_model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "model path and output handle are required");
    }
    *out_model = 0U;
    if (!IsValidModelConfig(config))
    {
        return Fail(error, REACHY_LLAMA_STATUS_ABI_MISMATCH, "model configuration ABI is invalid");
    }

    try
    {
        InitializeRuntime();
        llama_model_params params = llama_model_default_params();
        params.n_gpu_layers = 0;
        params.check_tensors = config->check_tensors != 0U;
        llama_model * native_model = llama_model_load_from_file(model_path_utf8, params);
        if (native_model == nullptr)
        {
            return Fail(error, REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED, "llama.cpp could not load the requested local model");
        }
        std::unique_ptr<llama_model, decltype(&llama_model_free)> native_owner(
            native_model, llama_model_free);
        std::shared_ptr<Model> model = std::make_shared<Model>(native_owner.get());
        native_owner.release();
        const reachy_llama_model_handle handle = NextHandle();
        {
            std::lock_guard<std::mutex> lock(g_registry_mutex);
            g_models.emplace(handle, model);
        }
        *out_model = handle;
        return REACHY_LLAMA_STATUS_OK;
    }
    catch (const std::bad_alloc &)
    {
        return Fail(error, REACHY_LLAMA_STATUS_INTERNAL_ERROR, "native allocation failed while loading the model");
    }
    catch (...)
    {
        return Fail(error, REACHY_LLAMA_STATUS_INTERNAL_ERROR, "unexpected native model-load failure");
    }
}

int32_t reachy_llama_model_unload(
    reachy_llama_model_handle model_handle,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    std::shared_ptr<Model> model = FindModel(model_handle);
    if (model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
    }

    {
        std::lock_guard<std::mutex> model_lock(model->mutex);
        if (!model->available)
        {
            return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
        }
        if (model->active_generations != 0U)
        {
            return Fail(error, REACHY_LLAMA_STATUS_BUSY, "model has an active generation; cancel and release it before unload");
        }
        model->available = false;
    }

    {
        std::lock_guard<std::mutex> registry_lock(g_registry_mutex);
        const auto iterator = g_models.find(model_handle);
        if (iterator != g_models.end() && iterator->second == model)
        {
            g_models.erase(iterator);
        }
    }
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_model_get_metrics(
    reachy_llama_model_handle model_handle,
    reachy_llama_model_metrics * metrics,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (metrics == nullptr || metrics->struct_size != sizeof(reachy_llama_model_metrics))
    {
        return Fail(error, REACHY_LLAMA_STATUS_ABI_MISMATCH, "model metrics struct size is invalid");
    }
    std::shared_ptr<Model> model = FindModel(model_handle);
    if (model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
    }
    metrics->abi_version = REACHY_LLAMA_ABI_VERSION;
    metrics->tensor_bytes = llama_model_size(model->model);
    metrics->parameter_count = llama_model_n_params(model->model);
    metrics->training_context_tokens = llama_model_n_ctx_train(model->model);
    metrics->reserved = 0U;
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_tokenize(
    reachy_llama_model_handle model_handle,
    const char * text_utf8,
    uint32_t add_special,
    uint32_t parse_special,
    int32_t * tokens,
    size_t token_capacity,
    size_t * required_tokens,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (text_utf8 == nullptr || required_tokens == nullptr || add_special > 1U || parse_special > 1U)
    {
        return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "tokenize arguments are invalid");
    }
    std::shared_ptr<Model> model = FindModel(model_handle);
    if (model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
    }
    const llama_vocab * vocab = llama_model_get_vocab(model->model);
    if (vocab == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_UNSUPPORTED_MODEL, "loaded model has no usable vocabulary");
    }

    const std::size_t text_length = std::strlen(text_utf8);
    if (text_length > static_cast<std::size_t>(std::numeric_limits<int32_t>::max()))
    {
        return Fail(error, REACHY_LLAMA_STATUS_TOKENIZE_FAILED, "tokenizer input text exceeds the supported size");
    }
    const int32_t text_length_i32 = static_cast<int32_t>(text_length);
    const int32_t first = llama_tokenize(
        vocab,
        text_utf8,
        text_length_i32,
        nullptr,
        0,
        add_special != 0U,
        parse_special != 0U);
    const int64_t count = first < 0 ? -static_cast<int64_t>(first) : static_cast<int64_t>(first);
    if (count < 0 || count > std::numeric_limits<int32_t>::max())
    {
        return Fail(error, REACHY_LLAMA_STATUS_TOKENIZE_FAILED, "token count is outside the supported range");
    }
    *required_tokens = static_cast<std::size_t>(count);
    if (static_cast<std::size_t>(count) > token_capacity)
    {
        return Fail(error, REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL, "token output buffer is too small");
    }
    if (count == 0)
    {
        return REACHY_LLAMA_STATUS_OK;
    }
    if (tokens == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "token output buffer is required");
    }

    std::vector<llama_token> native_tokens(static_cast<std::size_t>(count));
    const int32_t actual = llama_tokenize(
        vocab,
        text_utf8,
        text_length_i32,
        native_tokens.data(),
        static_cast<int32_t>(native_tokens.size()),
        add_special != 0U,
        parse_special != 0U);
    if (actual < 0 || static_cast<std::size_t>(actual) > native_tokens.size())
    {
        return Fail(error, REACHY_LLAMA_STATUS_TOKENIZE_FAILED, "llama.cpp failed to tokenize the input");
    }
    *required_tokens = static_cast<std::size_t>(actual);
    for (int32_t index = 0; index < actual; ++index)
    {
        tokens[static_cast<std::size_t>(index)] = native_tokens[static_cast<std::size_t>(index)];
    }
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_apply_chat_template(
    reachy_llama_model_handle model_handle,
    const char * template_utf8,
    const reachy_llama_chat_message * messages,
    size_t message_count,
    uint32_t add_assistant,
    char * output_utf8,
    size_t output_capacity,
    size_t * required_bytes,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (required_bytes == nullptr || add_assistant > 1U || message_count > kMaxChatMessages ||
        (message_count > 0U && messages == nullptr))
    {
        return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "chat-template arguments are invalid");
    }
    std::shared_ptr<Model> model = FindModel(model_handle);
    if (model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
    }

    const char * selected_template = template_utf8;
    if (selected_template == nullptr)
    {
        selected_template = llama_model_chat_template(model->model, nullptr);
    }
    if (selected_template == nullptr || selected_template[0] == '\0')
    {
        return Fail(error, REACHY_LLAMA_STATUS_TEMPLATE_FAILED, "no chat template is available");
    }

    std::vector<llama_chat_message> native_messages;
    native_messages.reserve(message_count);
    for (std::size_t index = 0U; index < message_count; ++index)
    {
        if (messages[index].role_utf8 == nullptr || messages[index].content_utf8 == nullptr)
        {
            return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "chat message role and content are required");
        }
        native_messages.push_back(
            llama_chat_message{messages[index].role_utf8, messages[index].content_utf8});
    }

    const int32_t needed = llama_chat_apply_template(
        selected_template,
        native_messages.data(),
        native_messages.size(),
        add_assistant != 0U,
        nullptr,
        0);
    if (needed < 0)
    {
        return Fail(error, REACHY_LLAMA_STATUS_TEMPLATE_FAILED, "llama.cpp rejected the requested chat template");
    }
    const std::size_t required = static_cast<std::size_t>(needed) + 1U;
    *required_bytes = required;
    if (required > output_capacity)
    {
        return Fail(error, REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL, "chat-template output buffer is too small");
    }
    if (output_utf8 == nullptr || output_capacity > static_cast<std::size_t>(std::numeric_limits<int32_t>::max()))
    {
        return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "chat-template output buffer is invalid");
    }
    const int32_t actual = llama_chat_apply_template(
        selected_template,
        native_messages.data(),
        native_messages.size(),
        add_assistant != 0U,
        output_utf8,
        static_cast<int32_t>(output_capacity));
    if (actual < 0 || actual != needed)
    {
        return Fail(error, REACHY_LLAMA_STATUS_TEMPLATE_FAILED, "llama.cpp chat-template output changed between sizing and rendering");
    }
    output_utf8[static_cast<std::size_t>(actual)] = '\0';
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_generation_start(
    reachy_llama_model_handle model_handle,
    const char * prompt_utf8,
    const reachy_llama_generation_config * config,
    reachy_llama_generation_handle * out_generation,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (prompt_utf8 == nullptr || prompt_utf8[0] == '\0' || out_generation == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_INVALID_ARGUMENT, "prompt and output generation handle are required");
    }
    *out_generation = 0U;
    if (!IsValidGenerationConfig(config))
    {
        return Fail(error, REACHY_LLAMA_STATUS_ABI_MISMATCH, "generation configuration is invalid or incompatible");
    }
    std::shared_ptr<Model> model = FindModel(model_handle);
    if (model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
    }

    {
        std::lock_guard<std::mutex> lock(model->mutex);
        if (!model->available)
        {
            return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
        }
        if (model->active_generations != 0U)
        {
            return Fail(error, REACHY_LLAMA_STATUS_BUSY, "model already has an active generation; RMA-130 does not queue requests");
        }
        model->active_generations = 1U;
    }

    try
    {
        const reachy_llama_generation_handle handle = NextHandle();
        std::shared_ptr<Job> job = std::make_shared<Job>(handle, model, *config, std::string(prompt_utf8));
        {
            std::lock_guard<std::mutex> lock(g_registry_mutex);
            g_jobs.emplace(handle, job);
        }
        try
        {
            job->worker = std::thread([job] { WorkerMain(job); });
        }
        catch (...)
        {
            std::lock_guard<std::mutex> lock(g_registry_mutex);
            g_jobs.erase(handle);
            throw;
        }
        *out_generation = handle;
        return REACHY_LLAMA_STATUS_OK;
    }
    catch (const std::bad_alloc &)
    {
        std::lock_guard<std::mutex> lock(model->mutex);
        model->active_generations = 0U;
        return Fail(error, REACHY_LLAMA_STATUS_INTERNAL_ERROR, "native allocation failed while starting generation");
    }
    catch (...)
    {
        std::lock_guard<std::mutex> lock(model->mutex);
        model->active_generations = 0U;
        return Fail(error, REACHY_LLAMA_STATUS_INTERNAL_ERROR, "native worker could not be started");
    }
}

int32_t reachy_llama_generation_poll(
    reachy_llama_generation_handle generation_handle,
    reachy_llama_generation_event * event,
    char * text_utf8,
    size_t text_capacity,
    size_t * required_bytes,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (event == nullptr || event->struct_size != sizeof(reachy_llama_generation_event) ||
        required_bytes == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_ABI_MISMATCH, "generation event struct is invalid");
    }
    std::shared_ptr<Job> job = FindJob(generation_handle);
    if (job == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "generation handle is not active");
    }
    std::lock_guard<std::mutex> poll_lock(job->poll_mutex);

    *required_bytes = 0U;
    event->type = REACHY_LLAMA_GENERATION_EVENT_NONE;
    event->status = REACHY_LLAMA_STATUS_OK;
    event->reserved = 0U;
    event->sequence = 0U;

    reachy_llama_internal::StreamChunk chunk{};
    if (job->queue.Peek(chunk))
    {
        const std::size_t required = chunk.text.size() + 1U;
        *required_bytes = required;
        if (text_utf8 == nullptr || text_capacity < required)
        {
            return Fail(error, REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL, "generation text buffer is too small");
        }
        reachy_llama_internal::StreamChunk popped{};
        if (!job->queue.Pop(popped))
        {
            return Fail(error, REACHY_LLAMA_STATUS_INTERNAL_ERROR, "generation queue changed unexpectedly");
        }
        std::memcpy(text_utf8, popped.text.data(), popped.text.size());
        text_utf8[popped.text.size()] = '\0';
        event->type = REACHY_LLAMA_GENERATION_EVENT_TEXT;
        event->sequence = popped.sequence;
        return REACHY_LLAMA_STATUS_OK;
    }

    if (!job->finished.load(std::memory_order_acquire))
    {
        return REACHY_LLAMA_STATUS_OK;
    }

    std::lock_guard<std::mutex> lock(job->terminal_mutex);
    if (job->terminal_delivered)
    {
        return REACHY_LLAMA_STATUS_OK;
    }
    event->type = job->terminal_type;
    event->status = job->terminal_status;
    job->terminal_delivered = true;
    if (job->terminal_type == REACHY_LLAMA_GENERATION_EVENT_ERROR)
    {
        WriteError(error, job->terminal_status, job->terminal_message.c_str());
    }
    else if (job->terminal_type == REACHY_LLAMA_GENERATION_EVENT_CANCELLED)
    {
        WriteError(error, REACHY_LLAMA_STATUS_CANCELLED, "generation cancelled");
    }
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_generation_cancel(
    reachy_llama_generation_handle generation_handle,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    std::shared_ptr<Job> job = FindJob(generation_handle);
    if (job == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "generation handle is not active");
    }
    if (job->finished.load(std::memory_order_acquire))
    {
        return REACHY_LLAMA_STATUS_OK;
    }
    job->cancel_requested.store(true, std::memory_order_release);
    job->queue.Cancel();
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_generation_get_metrics(
    reachy_llama_generation_handle generation_handle,
    reachy_llama_generation_metrics * metrics,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    if (metrics == nullptr || metrics->struct_size != sizeof(reachy_llama_generation_metrics))
    {
        return Fail(error, REACHY_LLAMA_STATUS_ABI_MISMATCH, "generation metrics struct size is invalid");
    }
    std::shared_ptr<Job> job = FindJob(generation_handle);
    if (job == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "generation handle is not active");
    }

    metrics->abi_version = REACHY_LLAMA_ABI_VERSION;
    metrics->state = job->state.load(std::memory_order_acquire);
    metrics->queue_depth = static_cast<uint32_t>(job->queue.Size());
    metrics->prompt_tokens = job->prompt_tokens.load(std::memory_order_acquire);
    metrics->generated_tokens = job->generated_tokens.load(std::memory_order_acquire);
    metrics->started_monotonic_us = job->started_us.load(std::memory_order_acquire);
    metrics->first_text_monotonic_us = job->first_text_us.load(std::memory_order_acquire);
    metrics->finished_monotonic_us = job->finished_us.load(std::memory_order_acquire);
    metrics->context_tokens = job->config.context_tokens;
    metrics->batch_tokens = job->config.batch_tokens;
    metrics->threads = job->config.threads;
    metrics->batch_threads = job->config.batch_threads;
    return REACHY_LLAMA_STATUS_OK;
}

int32_t reachy_llama_generation_release(
    reachy_llama_generation_handle generation_handle,
    reachy_llama_error_info * error)
{
    if (ValidateErrorStruct(error) != REACHY_LLAMA_STATUS_OK)
    {
        return REACHY_LLAMA_STATUS_ABI_MISMATCH;
    }
    ClearError(error);
    std::shared_ptr<Job> job = FindJob(generation_handle);
    if (job == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "generation handle is not active");
    }
    if (!job->finished.load(std::memory_order_acquire))
    {
        return Fail(error, REACHY_LLAMA_STATUS_BUSY, "generation is active; release never blocks waiting for inference");
    }
    if (job->worker.joinable())
    {
        job->worker.join();
    }
    {
        std::lock_guard<std::mutex> lock(g_registry_mutex);
        const auto iterator = g_jobs.find(generation_handle);
        if (iterator != g_jobs.end() && iterator->second == job)
        {
            g_jobs.erase(iterator);
        }
    }
    return REACHY_LLAMA_STATUS_OK;
}
}

#include "reachy_llama.h"

#include "llama.h"
#include "reachy_llama_internal.hpp"

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <utility>

namespace reachy_constraint_shim
{
struct ActiveConstraint
{
    const std::string * grammar;
    const std::string * root;
    bool initialization_failed{false};
};

thread_local ActiveConstraint * g_active_constraint = nullptr;
thread_local const llama_vocab * g_active_vocab = nullptr;

const llama_vocab * GetVocab(const llama_model * model)
{
    const llama_vocab * vocab = llama_model_get_vocab(model);
    g_active_vocab = vocab;
    return vocab;
}

llama_sampler * InitSamplerChain(llama_sampler_chain_params params)
{
    llama_sampler * chain = llama_sampler_chain_init(params);
    if (chain == nullptr || g_active_constraint == nullptr)
    {
        return chain;
    }
    if (g_active_vocab == nullptr)
    {
        g_active_constraint->initialization_failed = true;
        llama_sampler_free(chain);
        return nullptr;
    }

    llama_sampler * grammar = llama_sampler_init_grammar(
        g_active_vocab,
        g_active_constraint->grammar->c_str(),
        g_active_constraint->root->c_str());
    if (grammar == nullptr)
    {
        g_active_constraint->initialization_failed = true;
        llama_sampler_free(chain);
        return nullptr;
    }
    llama_sampler_chain_add(chain, grammar);
    return chain;
}
} // namespace reachy_constraint_shim

// Keep the accepted ABI-1 implementation byte-identical in a separate include and
// apply only two narrowly-scoped hooks: vocabulary capture and sampler-chain
// initialization. Unconstrained calls see no active constraint and retain the
// historical behavior. Constrained calls install a thread-local, deep-copied GBNF
// contract before entering the same generation worker.
#define llama_model_get_vocab(model) reachy_constraint_shim::GetVocab(model)
#define llama_sampler_chain_init(params) reachy_constraint_shim::InitSamplerChain(params)
#define reachy_llama_version_string ReachyLegacyVersionStringInternal
#define reachy_llama_status_string ReachyLegacyStatusStringInternal
#define reachy_llama_generation_start ReachyLegacyGenerationStartInternal
#include "reachy_llama_abi1_base.inc"
#undef reachy_llama_generation_start
#undef reachy_llama_status_string
#undef reachy_llama_version_string
#undef llama_sampler_chain_init
#undef llama_model_get_vocab

namespace
{
constexpr const char * kAbi2Version = "rma133-abi2-constrained";

struct ConstraintPayload
{
    std::string grammar;
    std::string root;
};

bool IsValidUtf8(const char * text, std::size_t length)
{
    if (text == nullptr)
    {
        return false;
    }
    std::size_t index = 0U;
    while (index < length)
    {
        const auto first = static_cast<unsigned char>(text[index]);
        if (first <= 0x7fU)
        {
            ++index;
            continue;
        }

        std::size_t continuation_count = 0U;
        uint32_t code_point = 0U;
        if (first >= 0xc2U && first <= 0xdfU)
        {
            continuation_count = 1U;
            code_point = static_cast<uint32_t>(first & 0x1fU);
        }
        else if (first >= 0xe0U && first <= 0xefU)
        {
            continuation_count = 2U;
            code_point = static_cast<uint32_t>(first & 0x0fU);
        }
        else if (first >= 0xf0U && first <= 0xf4U)
        {
            continuation_count = 3U;
            code_point = static_cast<uint32_t>(first & 0x07U);
        }
        else
        {
            return false;
        }
        if (continuation_count > length - index - 1U)
        {
            return false;
        }

        for (std::size_t offset = 1U; offset <= continuation_count; ++offset)
        {
            const auto value = static_cast<unsigned char>(text[index + offset]);
            if ((value & 0xc0U) != 0x80U)
            {
                return false;
            }
            code_point = (code_point << 6U) | static_cast<uint32_t>(value & 0x3fU);
        }
        if ((continuation_count == 1U && code_point < 0x80U) ||
            (continuation_count == 2U && code_point < 0x800U) ||
            (continuation_count == 3U && code_point < 0x10000U) ||
            code_point > 0x10ffffU ||
            (code_point >= 0xd800U && code_point <= 0xdfffU))
        {
            return false;
        }
        index += continuation_count + 1U;
    }
    return true;
}

bool IsValidRoot(const char * root, std::size_t length)
{
    if (root == nullptr || length == 0U || length > REACHY_LLAMA_MAX_GRAMMAR_ROOT_BYTES)
    {
        return false;
    }
    const auto first = static_cast<unsigned char>(root[0]);
    const bool first_ok = (first >= 'A' && first <= 'Z') ||
                          (first >= 'a' && first <= 'z') || first == '_';
    if (!first_ok)
    {
        return false;
    }
    for (std::size_t index = 1U; index < length; ++index)
    {
        const auto value = static_cast<unsigned char>(root[index]);
        const bool valid = (value >= 'A' && value <= 'Z') ||
                           (value >= 'a' && value <= 'z') ||
                           (value >= '0' && value <= '9') || value == '_' || value == '-';
        if (!valid)
        {
            return false;
        }
    }
    return true;
}

int32_t ValidateAndCopyConstraint(
    const reachy_llama_generation_constraint * constraint,
    ConstraintPayload & payload,
    reachy_llama_error_info * error)
{
    if (constraint == nullptr ||
        constraint->struct_size != sizeof(reachy_llama_generation_constraint) ||
        constraint->abi_version != REACHY_LLAMA_ABI_VERSION)
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_ABI_MISMATCH,
            "generation constraint ABI is invalid");
    }
    if (constraint->type != REACHY_LLAMA_CONSTRAINT_GBNF || constraint->reserved != 0U)
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
            "constrained generation requires an explicit GBNF constraint");
    }
    if (constraint->grammar_utf8 == nullptr || constraint->grammar_bytes == 0U ||
        constraint->grammar_bytes > REACHY_LLAMA_MAX_GRAMMAR_BYTES ||
        constraint->root_utf8 == nullptr || constraint->root_bytes == 0U ||
        constraint->root_bytes > REACHY_LLAMA_MAX_GRAMMAR_ROOT_BYTES)
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
            "grammar or root length is missing or outside the bounded limit");
    }
    if (std::memchr(constraint->grammar_utf8, '\0', constraint->grammar_bytes) != nullptr ||
        std::memchr(constraint->root_utf8, '\0', constraint->root_bytes) != nullptr)
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
            "grammar and root must not contain embedded NUL bytes");
    }
    if (!IsValidUtf8(constraint->grammar_utf8, constraint->grammar_bytes) ||
        !IsValidUtf8(constraint->root_utf8, constraint->root_bytes))
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
            "grammar and root must be valid UTF-8");
    }
    if (!IsValidRoot(constraint->root_utf8, constraint->root_bytes))
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INVALID_CONSTRAINT,
            "grammar root name contains unsupported characters");
    }

    try
    {
        payload.grammar.assign(constraint->grammar_utf8, constraint->grammar_bytes);
        payload.root.assign(constraint->root_utf8, constraint->root_bytes);
    }
    catch (const std::bad_alloc &)
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            "native allocation failed while copying the generation constraint");
    }
    return REACHY_LLAMA_STATUS_OK;
}

void WorkerMainConstrained(
    const std::shared_ptr<Job> & job,
    const std::shared_ptr<const ConstraintPayload> & payload)
{
    job->started_us.store(MonotonicUs(), std::memory_order_release);
    reachy_constraint_shim::ActiveConstraint active{&payload->grammar, &payload->root, false};
    reachy_constraint_shim::g_active_constraint = &active;
    reachy_constraint_shim::g_active_vocab = nullptr;

    try
    {
        RunGeneration(job);
        if (active.initialization_failed)
        {
            SetTerminal(
                job,
                REACHY_LLAMA_GENERATION_EVENT_ERROR,
                REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED,
                REACHY_LLAMA_GENERATION_STATE_ERROR,
                "llama.cpp rejected the requested GBNF grammar; unconstrained generation was not attempted");
        }
    }
    catch (const std::bad_alloc &)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "native allocation failed before constrained generation completed");
    }
    catch (...)
    {
        SetTerminal(
            job,
            REACHY_LLAMA_GENERATION_EVENT_ERROR,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            REACHY_LLAMA_GENERATION_STATE_ERROR,
            "unexpected constrained-generation worker failure");
    }

    reachy_constraint_shim::g_active_constraint = nullptr;
    reachy_constraint_shim::g_active_vocab = nullptr;
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
} // namespace

extern "C"
{
const char * reachy_llama_version_string(void)
{
    return kAbi2Version;
}

const char * reachy_llama_status_string(int32_t status)
{
    switch (static_cast<reachy_llama_status>(status))
    {
        case REACHY_LLAMA_STATUS_INVALID_CONSTRAINT:
            return "invalid_constraint";
        case REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED:
            return "constraint_init_failed";
        default:
            return ReachyLegacyStatusStringInternal(status);
    }
}

int32_t reachy_llama_generation_start(
    reachy_llama_model_handle model,
    const char * prompt_utf8,
    const reachy_llama_generation_config * config,
    reachy_llama_generation_handle * out_generation,
    reachy_llama_error_info * error)
{
    return ReachyLegacyGenerationStartInternal(model, prompt_utf8, config, out_generation, error);
}

int32_t reachy_llama_generation_start_constrained(
    reachy_llama_model_handle model_handle,
    const char * prompt_utf8,
    const reachy_llama_generation_config * config,
    const reachy_llama_generation_constraint * constraint,
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
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INVALID_ARGUMENT,
            "prompt and output generation handle are required");
    }
    *out_generation = 0U;
    if (!IsValidGenerationConfig(config))
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_ABI_MISMATCH,
            "generation configuration is invalid or incompatible");
    }

    ConstraintPayload copied;
    const int32_t constraint_status = ValidateAndCopyConstraint(constraint, copied, error);
    if (constraint_status != REACHY_LLAMA_STATUS_OK)
    {
        return constraint_status;
    }

    std::shared_ptr<Model> model = FindModel(model_handle);
    if (model == nullptr)
    {
        return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
    }

    std::shared_ptr<const ConstraintPayload> payload;
    try
    {
        payload = std::make_shared<const ConstraintPayload>(std::move(copied));
    }
    catch (const std::bad_alloc &)
    {
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            "native allocation failed while owning the generation constraint");
    }

    {
        std::lock_guard<std::mutex> lock(model->mutex);
        if (!model->available)
        {
            return Fail(error, REACHY_LLAMA_STATUS_NOT_FOUND, "model handle is not active");
        }
        if (model->active_generations != 0U)
        {
            return Fail(
                error,
                REACHY_LLAMA_STATUS_BUSY,
                "model already has an active generation; reachy_llama does not queue requests");
        }
        model->active_generations = 1U;
    }

    try
    {
        const reachy_llama_generation_handle handle = NextHandle();
        std::shared_ptr<Job> job =
            std::make_shared<Job>(handle, model, *config, std::string(prompt_utf8));
        {
            std::lock_guard<std::mutex> lock(g_registry_mutex);
            g_jobs.emplace(handle, job);
        }
        try
        {
            job->worker = std::thread([job, payload] { WorkerMainConstrained(job, payload); });
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
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            "native allocation failed while starting constrained generation");
    }
    catch (...)
    {
        std::lock_guard<std::mutex> lock(model->mutex);
        model->active_generations = 0U;
        return Fail(
            error,
            REACHY_LLAMA_STATUS_INTERNAL_ERROR,
            "constrained-generation worker could not be started");
    }
}
}

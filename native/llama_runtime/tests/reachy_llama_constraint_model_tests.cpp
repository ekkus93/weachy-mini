#include "reachy_llama.h"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace
{
[[noreturn]] void FailTest(const std::string & message)
{
    std::cerr << "RMA-133 constrained-model contract failure: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

void Require(bool condition, const std::string & message)
{
    if (!condition)
    {
        FailTest(message);
    }
}

reachy_llama_error_info FreshError()
{
    reachy_llama_error_info error{};
    error.struct_size = sizeof(error);
    return error;
}

reachy_llama_generation_config TestGenerationConfig()
{
    reachy_llama_generation_config config{};
    Require(
        reachy_llama_default_generation_config(&config) == REACHY_LLAMA_STATUS_OK,
        "default generation config failed");
    config.context_tokens = 256U;
    config.batch_tokens = 64U;
    config.micro_batch_tokens = 32U;
    config.max_generated_tokens = 64U;
    config.threads = 1;
    config.batch_threads = 1;
    config.temperature = 0.0F;
    config.min_p = 0.0F;
    config.seed = 133U;
    config.stream_queue_capacity = 8U;
    return config;
}

reachy_llama_generation_constraint Constraint(
    const std::string & grammar,
    const std::string & root)
{
    reachy_llama_generation_constraint constraint{};
    constraint.struct_size = sizeof(constraint);
    constraint.abi_version = REACHY_LLAMA_ABI_VERSION;
    constraint.type = REACHY_LLAMA_CONSTRAINT_GBNF;
    constraint.grammar_utf8 = grammar.data();
    constraint.grammar_bytes = grammar.size();
    constraint.root_utf8 = root.data();
    constraint.root_bytes = root.size();
    return constraint;
}

struct GenerationResult
{
    uint32_t terminal_type{REACHY_LLAMA_GENERATION_EVENT_NONE};
    int32_t terminal_status{REACHY_LLAMA_STATUS_OK};
    std::string text;
};

GenerationResult PollToTerminal(
    reachy_llama_generation_handle generation,
    std::chrono::seconds timeout)
{
    GenerationResult result{};
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < deadline)
    {
        reachy_llama_generation_event event{};
        event.struct_size = sizeof(event);
        std::vector<char> buffer(4096U, '\0');
        std::size_t required = 0U;
        reachy_llama_error_info error = FreshError();
        int32_t status = reachy_llama_generation_poll(
            generation,
            &event,
            buffer.data(),
            buffer.size(),
            &required,
            &error);
        if (status == REACHY_LLAMA_STATUS_BUFFER_TOO_SMALL)
        {
            Require(required > buffer.size(), "buffer-too-small did not report a larger size");
            buffer.assign(required, '\0');
            event = {};
            event.struct_size = sizeof(event);
            error = FreshError();
            status = reachy_llama_generation_poll(
                generation,
                &event,
                buffer.data(),
                buffer.size(),
                &required,
                &error);
        }
        Require(status == REACHY_LLAMA_STATUS_OK, "poll returned a non-event API failure");
        if (event.type == REACHY_LLAMA_GENERATION_EVENT_TEXT)
        {
            Require(required > 0U, "text event did not report bytes");
            result.text.append(buffer.data());
        }
        else if (
            event.type == REACHY_LLAMA_GENERATION_EVENT_COMPLETED ||
            event.type == REACHY_LLAMA_GENERATION_EVENT_CANCELLED ||
            event.type == REACHY_LLAMA_GENERATION_EVENT_ERROR)
        {
            result.terminal_type = event.type;
            result.terminal_status = event.status;
            return result;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    FailTest("timed out waiting for generation terminal event");
}

reachy_llama_generation_handle Start(
    reachy_llama_model_handle model,
    const std::string & grammar,
    const std::string & root,
    const reachy_llama_generation_config & config)
{
    reachy_llama_generation_constraint constraint = Constraint(grammar, root);
    reachy_llama_generation_handle generation = 0U;
    reachy_llama_error_info error = FreshError();
    const int32_t status = reachy_llama_generation_start_constrained(
        model,
        "Once upon a time",
        &config,
        &constraint,
        &generation,
        &error);
    Require(status == REACHY_LLAMA_STATUS_OK, std::string("constrained start failed: ") + error.message);
    Require(generation != 0U, "constrained start did not return a generation handle");
    return generation;
}

void Release(reachy_llama_generation_handle generation)
{
    reachy_llama_error_info error = FreshError();
    Require(
        reachy_llama_generation_release(generation, &error) == REACHY_LLAMA_STATUS_OK,
        std::string("generation release failed: ") + error.message);
}

void TestOwnedConstraintBytes(reachy_llama_model_handle model)
{
    reachy_llama_generation_config config = TestGenerationConfig();
    config.max_generated_tokens = 8U;
    std::string grammar = "root ::= \"a\"";
    std::string root = "root";
    reachy_llama_generation_handle generation = Start(model, grammar, root, config);

    std::fill(grammar.begin(), grammar.end(), 'x');
    std::fill(root.begin(), root.end(), 'y');

    const GenerationResult result = PollToTerminal(generation, std::chrono::seconds(10));
    Require(
        result.terminal_type == REACHY_LLAMA_GENERATION_EVENT_COMPLETED,
        "owned-constraint generation did not complete");
    Require(result.terminal_status == REACHY_LLAMA_STATUS_OK, "owned-constraint terminal status was not OK");
    Require(result.text == "a", "runtime did not preserve the deep-copied grammar/root bytes");
    Require(result.text.rfind("```", 0U) != 0U, "constrained bytes unexpectedly began with a Markdown fence");
    Require(result.text.rfind("<think>", 0U) != 0U, "constrained bytes unexpectedly began with <think>");
    Release(generation);
}

void TestMalformedGrammarFailsWithoutOutput(reachy_llama_model_handle model)
{
    reachy_llama_generation_config config = TestGenerationConfig();
    config.max_generated_tokens = 8U;
    const std::string grammar = "root ::= (";
    const std::string root = "root";
    const reachy_llama_generation_handle generation = Start(model, grammar, root, config);
    const GenerationResult result = PollToTerminal(generation, std::chrono::seconds(10));
    Require(
        result.terminal_type == REACHY_LLAMA_GENERATION_EVENT_ERROR,
        "malformed grammar did not end in an explicit error event");
    Require(
        result.terminal_status == REACHY_LLAMA_STATUS_CONSTRAINT_INIT_FAILED,
        "malformed grammar did not report constraint-init failure");
    Require(result.text.empty(), "malformed grammar produced partial/unconstrained output");
    Release(generation);
}

void WaitForBackpressure(reachy_llama_generation_handle generation)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (std::chrono::steady_clock::now() < deadline)
    {
        reachy_llama_generation_metrics metrics{};
        metrics.struct_size = sizeof(metrics);
        reachy_llama_error_info error = FreshError();
        Require(
            reachy_llama_generation_get_metrics(generation, &metrics, &error) ==
                REACHY_LLAMA_STATUS_OK,
            std::string("generation metrics failed: ") + error.message);
        if (metrics.queue_depth > 0U)
        {
            return;
        }
        Require(
            metrics.state == REACHY_LLAMA_GENERATION_STATE_RUNNING,
            "generation finished before cancellation/backpressure could be exercised");
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    FailTest("timed out waiting for constrained generation backpressure");
}

void TestCancellationCleanupAndReuse(reachy_llama_model_handle model)
{
    reachy_llama_generation_config config = TestGenerationConfig();
    config.context_tokens = 512U;
    config.max_generated_tokens = 256U;
    config.stream_queue_capacity = 1U;

    std::string literal;
    literal.reserve(1024U);
    for (std::size_t index = 0U; index < 1024U; ++index)
    {
        literal.push_back(static_cast<char>('a' + static_cast<int>(index % 26U)));
    }
    const std::string grammar = "root ::= \"" + literal + "\"";
    const std::string root = "root";
    const reachy_llama_generation_handle generation = Start(model, grammar, root, config);
    WaitForBackpressure(generation);

    reachy_llama_error_info error = FreshError();
    Require(
        reachy_llama_generation_cancel(generation, &error) == REACHY_LLAMA_STATUS_OK,
        std::string("generation cancel failed: ") + error.message);
    const GenerationResult cancelled = PollToTerminal(generation, std::chrono::seconds(10));
    Require(
        cancelled.terminal_type == REACHY_LLAMA_GENERATION_EVENT_CANCELLED,
        "cancelled constrained generation did not report CANCELLED");
    Require(
        cancelled.terminal_status == REACHY_LLAMA_STATUS_CANCELLED,
        "cancelled constrained generation terminal status mismatch");
    Release(generation);

    config = TestGenerationConfig();
    config.max_generated_tokens = 8U;
    const reachy_llama_generation_handle reuse = Start(model, "root ::= \"b\"", "root", config);
    const GenerationResult reused = PollToTerminal(reuse, std::chrono::seconds(10));
    Require(
        reused.terminal_type == REACHY_LLAMA_GENERATION_EVENT_COMPLETED && reused.text == "b",
        "model was not reusable after constrained cancellation/release");
    Release(reuse);
}
} // namespace

int main(int argc, char ** argv)
{
    Require(argc == 2, "usage: reachy_llama_constraint_model_tests TEST_MODEL.gguf");
    Require(REACHY_LLAMA_ABI_VERSION == 2U, "test requires ABI 2");

    reachy_llama_model_config config{};
    Require(
        reachy_llama_default_model_config(&config) == REACHY_LLAMA_STATUS_OK,
        "default model config failed");
    reachy_llama_model_handle model = 0U;
    reachy_llama_error_info error = FreshError();
    Require(
        reachy_llama_model_load(argv[1], &config, &model, &error) == REACHY_LLAMA_STATUS_OK,
        std::string("test model load failed: ") + error.message);
    Require(model != 0U, "test model load returned a zero handle");

    TestOwnedConstraintBytes(model);
    TestMalformedGrammarFailsWithoutOutput(model);
    TestCancellationCleanupAndReuse(model);

    error = FreshError();
    Require(
        reachy_llama_model_unload(model, &error) == REACHY_LLAMA_STATUS_OK,
        std::string("model unload failed after constrained tests: ") + error.message);
    return EXIT_SUCCESS;
}

#include "reachy_llama.h"
#include "reachy_llama_internal.hpp"

#include <atomic>
#include <cstdlib>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>

namespace
{
[[noreturn]] void FailTest(const char * message)
{
    std::cerr << "RMA-130 contract failure: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

void Require(bool condition, const char * message)
{
    if (!condition)
    {
        FailTest(message);
    }
}

void TestAbiAndDefaults()
{
    Require(reachy_llama_abi_version() == REACHY_LLAMA_ABI_VERSION, "ABI version mismatch");
    Require(
        std::string(reachy_llama_upstream_revision()) ==
            "dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb",
        "upstream revision mismatch");
    Require(
        std::string(reachy_llama_status_string(REACHY_LLAMA_STATUS_BUSY)) == "busy",
        "status string mismatch");

    reachy_llama_model_config model_config{};
    Require(
        reachy_llama_default_model_config(&model_config) == REACHY_LLAMA_STATUS_OK,
        "default model config failed");
    Require(model_config.struct_size == sizeof(model_config), "model config size mismatch");
    Require(model_config.abi_version == REACHY_LLAMA_ABI_VERSION, "model config ABI mismatch");
    Require(model_config.check_tensors == 1U, "tensor verification must default on");

    reachy_llama_generation_config generation_config{};
    Require(
        reachy_llama_default_generation_config(&generation_config) == REACHY_LLAMA_STATUS_OK,
        "default generation config failed");
    Require(
        generation_config.struct_size == sizeof(generation_config),
        "generation config size mismatch");
    Require(
        generation_config.abi_version == REACHY_LLAMA_ABI_VERSION,
        "generation config ABI mismatch");
    Require(generation_config.context_tokens == 4096U, "unexpected default context");
    Require(
        generation_config.batch_tokens <= generation_config.context_tokens,
        "batch exceeds context");
    Require(
        generation_config.micro_batch_tokens <= generation_config.batch_tokens,
        "micro-batch exceeds batch");
    Require(
        generation_config.max_generated_tokens < generation_config.context_tokens,
        "output budget exceeds context");
    Require(generation_config.stream_queue_capacity > 0U, "stream queue must be bounded nonzero");
}

void TestFailClosedInvalidModelLoads()
{
    reachy_llama_model_config config{};
    Require(
        reachy_llama_default_model_config(&config) == REACHY_LLAMA_STATUS_OK,
        "default model config failed");

    for (int iteration = 0; iteration < 64; ++iteration)
    {
        reachy_llama_error_info error{};
        error.struct_size = sizeof(error);
        reachy_llama_model_handle handle = 777U;
        const int32_t status = reachy_llama_model_load(
            "/definitely/not/a/weachy/model.gguf",
            &config,
            &handle,
            &error);
        Require(status == REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED, "invalid model must fail load");
        Require(handle == 0U, "failed model load returned a live handle");
        Require(
            error.status == REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED,
            "invalid model error category mismatch");
        Require(error.message[0] != '\0', "invalid model failure must be visible");
    }
}

void TestInvalidHandlesNeverFallback()
{
    reachy_llama_error_info error{};
    error.struct_size = sizeof(error);
    Require(
        reachy_llama_model_unload(999999U, &error) == REACHY_LLAMA_STATUS_NOT_FOUND,
        "unknown model handle must not fall back");
    Require(error.status == REACHY_LLAMA_STATUS_NOT_FOUND, "unknown model error mismatch");

    error = {};
    error.struct_size = sizeof(error);
    Require(
        reachy_llama_generation_cancel(999999U, &error) == REACHY_LLAMA_STATUS_NOT_FOUND,
        "unknown generation handle must not fall back");
    Require(error.status == REACHY_LLAMA_STATUS_NOT_FOUND, "unknown generation error mismatch");
}

void TestBoundedQueueOrderingAndAllocationStress()
{
    for (uint64_t iteration = 0U; iteration < 256U; ++iteration)
    {
        reachy_llama_internal::BoundedStreamQueue queue(8U);
        for (uint64_t index = 0U; index < 8U; ++index)
        {
            Require(queue.Push(index + 1U, "chunk-" + std::to_string(index)), "queue push failed");
        }
        Require(queue.Size() == 8U, "queue capacity accounting mismatch");
        for (uint64_t index = 0U; index < 8U; ++index)
        {
            reachy_llama_internal::StreamChunk chunk{};
            Require(queue.Pop(chunk), "queue pop failed");
            Require(chunk.sequence == index + 1U, "queue sequence reordered");
            Require(chunk.text == "chunk-" + std::to_string(index), "queue text reordered");
        }
        Require(queue.Size() == 0U, "queue did not drain exactly");
    }
}

void TestCancellationUnblocksBackpressureWithoutSilentDrain()
{
    for (int iteration = 0; iteration < 128; ++iteration)
    {
        reachy_llama_internal::BoundedStreamQueue queue(1U);
        std::atomic<bool> first_push_result{false};
        std::atomic<bool> second_push_started{false};
        std::atomic<bool> second_push_result{true};

        std::thread producer(
            [&queue, &first_push_result, &second_push_started, &second_push_result] {
                first_push_result.store(queue.Push(1U, "first"), std::memory_order_release);
                second_push_started.store(true, std::memory_order_release);
                second_push_result.store(queue.Push(2U, "second"), std::memory_order_release);
            });

        while (!second_push_started.load(std::memory_order_acquire))
        {
            std::this_thread::yield();
        }
        queue.Cancel();
        producer.join();

        Require(first_push_result.load(std::memory_order_acquire), "first queue push failed");
        Require(
            !second_push_result.load(std::memory_order_acquire),
            "cancelled blocked producer incorrectly published output");
        Require(queue.IsCancelled(), "queue did not retain cancellation state");
        reachy_llama_internal::StreamChunk retained{};
        Require(queue.Pop(retained), "retained pre-cancel chunk disappeared");
        Require(retained.sequence == 1U, "retained chunk sequence changed");
        Require(retained.text == "first", "retained chunk text changed");
        Require(queue.Size() == 0U, "queue did not drain retained chunk exactly");
    }
}
} // namespace

int main()
{
    TestAbiAndDefaults();
    TestFailClosedInvalidModelLoads();
    TestInvalidHandlesNeverFallback();
    TestBoundedQueueOrderingAndAllocationStress();
    TestCancellationUnblocksBackpressureWithoutSilentDrain();
    return EXIT_SUCCESS;
}

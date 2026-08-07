#include "reachy_llama.h"
#include "reachy_llama_internal.hpp"

#include <atomic>
#include <cassert>
#include <cstdint>
#include <string>
#include <thread>

namespace
{
void TestAbiAndDefaults()
{
    assert(reachy_llama_abi_version() == REACHY_LLAMA_ABI_VERSION);
    assert(std::string(reachy_llama_upstream_revision()) ==
           "dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb");
    assert(std::string(reachy_llama_status_string(REACHY_LLAMA_STATUS_BUSY)) == "busy");

    reachy_llama_model_config model_config{};
    assert(reachy_llama_default_model_config(&model_config) == REACHY_LLAMA_STATUS_OK);
    assert(model_config.struct_size == sizeof(model_config));
    assert(model_config.abi_version == REACHY_LLAMA_ABI_VERSION);
    assert(model_config.check_tensors == 1U);

    reachy_llama_generation_config generation_config{};
    assert(reachy_llama_default_generation_config(&generation_config) == REACHY_LLAMA_STATUS_OK);
    assert(generation_config.struct_size == sizeof(generation_config));
    assert(generation_config.abi_version == REACHY_LLAMA_ABI_VERSION);
    assert(generation_config.context_tokens == 4096U);
    assert(generation_config.batch_tokens <= generation_config.context_tokens);
    assert(generation_config.micro_batch_tokens <= generation_config.batch_tokens);
    assert(generation_config.max_generated_tokens < generation_config.context_tokens);
    assert(generation_config.stream_queue_capacity > 0U);
}

void TestFailClosedInvalidModelLoads()
{
    reachy_llama_model_config config{};
    assert(reachy_llama_default_model_config(&config) == REACHY_LLAMA_STATUS_OK);

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
        assert(status == REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED);
        assert(handle == 0U);
        assert(error.status == REACHY_LLAMA_STATUS_MODEL_LOAD_FAILED);
        assert(error.message[0] != '\0');
    }
}

void TestInvalidHandlesNeverFallback()
{
    reachy_llama_error_info error{};
    error.struct_size = sizeof(error);
    assert(reachy_llama_model_unload(999999U, &error) == REACHY_LLAMA_STATUS_NOT_FOUND);
    assert(error.status == REACHY_LLAMA_STATUS_NOT_FOUND);

    error = {};
    error.struct_size = sizeof(error);
    assert(reachy_llama_generation_cancel(999999U, &error) == REACHY_LLAMA_STATUS_NOT_FOUND);
    assert(error.status == REACHY_LLAMA_STATUS_NOT_FOUND);
}

void TestBoundedQueueOrderingAndAllocationStress()
{
    for (uint64_t iteration = 0U; iteration < 256U; ++iteration)
    {
        reachy_llama_internal::BoundedStreamQueue queue(8U);
        for (uint64_t index = 0U; index < 8U; ++index)
        {
            assert(queue.Push(index + 1U, "chunk-" + std::to_string(index)));
        }
        assert(queue.Size() == 8U);
        for (uint64_t index = 0U; index < 8U; ++index)
        {
            reachy_llama_internal::StreamChunk chunk{};
            assert(queue.Pop(chunk));
            assert(chunk.sequence == index + 1U);
            assert(chunk.text == "chunk-" + std::to_string(index));
        }
        assert(queue.Size() == 0U);
    }
}

void TestCancellationUnblocksBackpressureWithoutSilentDrain()
{
    for (int iteration = 0; iteration < 128; ++iteration)
    {
        reachy_llama_internal::BoundedStreamQueue queue(1U);
        std::atomic<bool> second_push_started{false};
        std::atomic<bool> second_push_result{true};

        std::thread producer([&queue, &second_push_started, &second_push_result] {
            assert(queue.Push(1U, "first"));
            second_push_started.store(true, std::memory_order_release);
            second_push_result.store(queue.Push(2U, "second"), std::memory_order_release);
        });

        while (!second_push_started.load(std::memory_order_acquire))
        {
            std::this_thread::yield();
        }
        queue.Cancel();
        producer.join();

        assert(!second_push_result.load(std::memory_order_acquire));
        assert(queue.IsCancelled());
        reachy_llama_internal::StreamChunk retained{};
        assert(queue.Pop(retained));
        assert(retained.sequence == 1U);
        assert(retained.text == "first");
        assert(queue.Size() == 0U);
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
    return 0;
}

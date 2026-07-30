#include "reachy_sim.h"
#include "reachy_sim_blocking_backend.h"

#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <threads.h>
#include <time.h>

#define CONTENTION_THREAD_COUNT 8U
#define CONTENTION_ATTEMPTS 2000U

static int g_failures = 0;

static void check_true(bool condition, const char* message)
{
    if(!condition)
    {
        (void)fprintf(stderr, "FAIL: %s\n", message);
        ++g_failures;
    }
}

static void check_status(
    int32_t actual,
    ReachySimStatus expected,
    const char* message)
{
    if(actual != (int32_t)expected)
    {
        (void)fprintf(
            stderr,
            "FAIL: %s: expected %s, received %s\n",
            message,
            reachy_sim_status_string((int32_t)expected),
            reachy_sim_status_string(actual));
        ++g_failures;
    }
}

static bool bytes_equal_value(
    const uint8_t* bytes,
    size_t byte_count,
    uint8_t value)
{
    for(size_t index = 0U; index < byte_count; ++index)
    {
        if(bytes[index] != value)
        {
            return false;
        }
    }
    return true;
}

static ReachySimErrorInfo initialized_error(void)
{
    ReachySimErrorInfo error = {0};
    error.abi_version = REACHY_SIM_ABI_VERSION;
    error.struct_size = (uint32_t)sizeof(error);
    return error;
}

static ReachySimCapabilities initialized_capabilities(void)
{
    ReachySimCapabilities capabilities = {0};
    capabilities.abi_version = REACHY_SIM_ABI_VERSION;
    capabilities.struct_size = (uint32_t)sizeof(capabilities);
    return capabilities;
}

static ReachySimHandle create_handle(const char* model)
{
    ReachySimConfig config = reachy_sim_default_config();
    ReachySimHandle handle = REACHY_SIM_INVALID_HANDLE;
    ReachySimErrorInfo error = initialized_error();
    check_status(
        reachy_sim_create(
            (const uint8_t*)model,
            strlen(model) + 1U,
            &config,
            &handle,
            &error),
        REACHY_SIM_STATUS_OK,
        "create test handle");
    check_true(handle != REACHY_SIM_INVALID_HANDLE, "create returned a valid handle");
    return handle;
}

static bool wait_for_blocking_step(void)
{
    const struct timespec delay = {0, 1000000L};
    for(unsigned int attempt = 0U; attempt < 5000U; ++attempt)
    {
        if(reachy_sim_blocking_backend_step_entered())
        {
            return true;
        }
        (void)thrd_sleep(&delay, NULL);
    }
    return false;
}

typedef struct StepThreadArguments {
    ReachySimHandle handle;
    int32_t status;
} StepThreadArguments;

static int blocking_step_thread(void* opaque_arguments)
{
    StepThreadArguments* const arguments = opaque_arguments;
    arguments->status = reachy_sim_step(arguments->handle, 1U);
    return 0;
}

static void test_output_contracts(void)
{
    static const uint8_t SENTINEL = UINT8_C(0xa5);
    ReachySimHandle handle = create_handle("output-contract-model");

    size_t required_size = SIZE_MAX;
    check_status(
        reachy_sim_copy_state(handle, NULL, 0U, &required_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "state size query");
    check_true(
        required_size == sizeof(ReachySimStateHeader),
        "state size query returns exact size");

    required_size = SIZE_MAX;
    check_status(
        reachy_sim_copy_state(handle, NULL, 1U, &required_size),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "state null buffer with nonzero capacity");
    check_true(
        required_size == SIZE_MAX,
        "invalid state copy leaves required size unchanged");

    uint8_t state_bytes[sizeof(ReachySimStateHeader)] = {0};
    memset(state_bytes, SENTINEL, sizeof(state_bytes));
    required_size = SIZE_MAX;
    check_status(
        reachy_sim_copy_state(
            handle,
            state_bytes,
            sizeof(state_bytes) - 1U,
            &required_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "undersized state buffer");
    check_true(
        required_size == sizeof(state_bytes),
        "undersized state reports exact size");
    check_true(
        bytes_equal_value(state_bytes, sizeof(state_bytes), SENTINEL),
        "undersized state buffer is not partially mutated");

    check_status(
        reachy_sim_copy_state(
            handle,
            state_bytes,
            sizeof(state_bytes),
            NULL),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "state copy requires size output");
    check_true(
        bytes_equal_value(state_bytes, sizeof(state_bytes), SENTINEL),
        "missing size output leaves state buffer unchanged");

    required_size = 0U;
    check_status(
        reachy_sim_copy_state(
            handle,
            state_bytes,
            sizeof(state_bytes),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "full state copy");
    check_true(
        required_size == sizeof(state_bytes),
        "full state copy size");

    size_t snapshot_size = SIZE_MAX;
    check_status(
        reachy_sim_copy_snapshot(handle, NULL, 0U, &snapshot_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "snapshot size query");
    check_true(
        snapshot_size ==
            sizeof(ReachySimSnapshotHeader) + sizeof(ReachySimStateHeader),
        "snapshot size query returns exact size");

    uint8_t snapshot_bytes[
        sizeof(ReachySimSnapshotHeader) + sizeof(ReachySimStateHeader)] = {0};
    memset(snapshot_bytes, SENTINEL, sizeof(snapshot_bytes));
    size_t actual_snapshot_size = SIZE_MAX;
    check_status(
        reachy_sim_copy_snapshot(
            handle,
            snapshot_bytes,
            sizeof(snapshot_bytes) - 1U,
            &actual_snapshot_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "undersized snapshot buffer");
    check_true(
        actual_snapshot_size == sizeof(snapshot_bytes),
        "undersized snapshot reports exact size");
    check_true(
        bytes_equal_value(snapshot_bytes, sizeof(snapshot_bytes), SENTINEL),
        "undersized snapshot buffer is not partially mutated");

    actual_snapshot_size = 0U;
    check_status(
        reachy_sim_copy_snapshot(
            handle,
            snapshot_bytes,
            sizeof(snapshot_bytes),
            &actual_snapshot_size),
        REACHY_SIM_STATUS_OK,
        "full snapshot copy");
    check_true(
        actual_snapshot_size == sizeof(snapshot_bytes),
        "full snapshot copy size");

    check_status(
        reachy_sim_destroy(handle),
        REACHY_SIM_STATUS_OK,
        "destroy output-contract handle");
}

static void test_initialized_create_outputs(void)
{
    static const uint8_t MODEL[] = "create-output-model";
    ReachySimConfig config = reachy_sim_default_config();

    ReachySimHandle handle = UINT64_C(123);
    ReachySimErrorInfo zero_error = {0};
    const ReachySimErrorInfo original_zero_error = zero_error;
    check_status(
        reachy_sim_create(
            MODEL,
            sizeof(MODEL),
            &config,
            &handle,
            &zero_error),
        REACHY_SIM_STATUS_ABI_MISMATCH,
        "create rejects uninitialized error output");
    check_true(
        handle == REACHY_SIM_INVALID_HANDLE,
        "failed create clears handle before output validation");
    check_true(
        memcmp(&zero_error, &original_zero_error, sizeof(zero_error)) == 0,
        "invalid error output is not mutated");

    ReachySimErrorInfo short_error = initialized_error();
    short_error.struct_size -= 1U;
    const ReachySimErrorInfo original_short_error = short_error;
    handle = UINT64_C(456);
    check_status(
        reachy_sim_create(
            MODEL,
            sizeof(MODEL),
            &config,
            &handle,
            &short_error),
        REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH,
        "create rejects short error output");
    check_true(
        memcmp(&short_error, &original_short_error, sizeof(short_error)) == 0,
        "short error output is not mutated");

    handle = REACHY_SIM_INVALID_HANDLE;
    check_status(
        reachy_sim_create(
            MODEL,
            sizeof(MODEL),
            &config,
            &handle,
            NULL),
        REACHY_SIM_STATUS_OK,
        "create permits omitted optional error output");
    check_status(
        reachy_sim_destroy(handle),
        REACHY_SIM_STATUS_OK,
        "destroy no-error-output handle");
}

static void test_exclusive_operation_lease(void)
{
    static const uint8_t SENTINEL = UINT8_C(0x6d);
    ReachySimHandle primary = create_handle("blocking-primary");
    ReachySimHandle independent = create_handle("blocking-independent");

    reachy_sim_blocking_backend_reset_controls();
    reachy_sim_blocking_backend_set_step_blocked(true);
    StepThreadArguments arguments = {primary, REACHY_SIM_STATUS_BACKEND_ERROR};
    thrd_t thread;
    check_true(
        thrd_create(&thread, blocking_step_thread, &arguments) == thrd_success,
        "create blocking step thread");
    const bool entered = wait_for_blocking_step();
    check_true(entered, "blocking step entered backend");

    if(entered)
    {
        check_status(
            reachy_sim_step(primary, 0U),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent step returns handle busy before argument validation");
        check_status(
            reachy_sim_reset(primary, REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent reset returns handle busy");
        check_status(
            reachy_sim_submit_commands(primary, NULL, 0U),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent command returns handle busy");
        check_status(
            reachy_sim_apply_wrench(primary, NULL),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent wrench returns handle busy");
        check_status(
            reachy_sim_restore_snapshot(primary, NULL, 0U),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent restore returns handle busy");
        check_status(
            reachy_sim_destroy(primary),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "destroy returns handle busy during operation");

        uint8_t state_bytes[sizeof(ReachySimStateHeader)] = {0};
        memset(state_bytes, SENTINEL, sizeof(state_bytes));
        size_t required_size = SIZE_MAX;
        check_status(
            reachy_sim_copy_state(
                primary,
                state_bytes,
                sizeof(state_bytes),
                &required_size),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent state copy returns handle busy");
        check_true(
            required_size == SIZE_MAX,
            "busy state copy leaves required size unchanged");
        check_true(
            bytes_equal_value(state_bytes, sizeof(state_bytes), SENTINEL),
            "busy state copy leaves output unchanged");

        uint8_t snapshot_bytes[
            sizeof(ReachySimSnapshotHeader) + sizeof(ReachySimStateHeader)] = {0};
        memset(snapshot_bytes, SENTINEL, sizeof(snapshot_bytes));
        required_size = SIZE_MAX;
        check_status(
            reachy_sim_copy_snapshot(
                primary,
                snapshot_bytes,
                sizeof(snapshot_bytes),
                &required_size),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent snapshot copy returns handle busy");
        check_true(
            required_size == SIZE_MAX,
            "busy snapshot copy leaves required size unchanged");
        check_true(
            bytes_equal_value(snapshot_bytes, sizeof(snapshot_bytes), SENTINEL),
            "busy snapshot copy leaves output unchanged");

        ReachySimCapabilities capabilities = initialized_capabilities();
        capabilities.capability_flags = UINT64_MAX;
        const ReachySimCapabilities original_capabilities = capabilities;
        check_status(
            reachy_sim_get_handle_capabilities(primary, &capabilities),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent capability copy returns handle busy");
        check_true(
            memcmp(
                &capabilities,
                &original_capabilities,
                sizeof(capabilities)) == 0,
            "busy capability output is unchanged");

        ReachySimErrorInfo last_error = initialized_error();
        memset(last_error.message, 'q', sizeof(last_error.message));
        const ReachySimErrorInfo original_error = last_error;
        check_status(
            reachy_sim_get_last_error(primary, &last_error),
            REACHY_SIM_STATUS_HANDLE_BUSY,
            "concurrent last-error copy returns handle busy");
        check_true(
            memcmp(&last_error, &original_error, sizeof(last_error)) == 0,
            "busy error output is unchanged");

        ReachySimStateHeader independent_state = {0};
        size_t independent_size = 0U;
        check_status(
            reachy_sim_copy_state(
                independent,
                &independent_state,
                sizeof(independent_state),
                &independent_size),
            REACHY_SIM_STATUS_OK,
            "operation on independent handle is not blocked");
    }

    reachy_sim_blocking_backend_set_step_blocked(false);
    int thread_result = -1;
    check_true(
        thrd_join(thread, &thread_result) == thrd_success,
        "join blocking step thread");
    check_true(thread_result == 0, "blocking step thread result");
    check_status(
        arguments.status,
        REACHY_SIM_STATUS_OK,
        "blocking step completes after release");

    ReachySimStateHeader final_state = {0};
    size_t final_size = 0U;
    check_status(
        reachy_sim_copy_state(
            primary,
            &final_state,
            sizeof(final_state),
            &final_size),
        REACHY_SIM_STATUS_OK,
        "state copy after lease release");
    check_true(final_state.sequence == 1U, "only the leased step mutated state");

    check_status(
        reachy_sim_destroy(primary),
        REACHY_SIM_STATUS_OK,
        "destroy primary after lease release");
    check_status(
        reachy_sim_destroy(independent),
        REACHY_SIM_STATUS_OK,
        "destroy independent handle");
    reachy_sim_blocking_backend_reset_controls();
}

typedef struct ContentionShared {
    ReachySimHandle handle;
    atomic_uint ready_count;
    atomic_bool start;
    atomic_uint success_count;
    atomic_uint busy_count;
    atomic_uint unexpected_count;
} ContentionShared;

static int contention_thread(void* opaque_shared)
{
    ContentionShared* const shared = opaque_shared;
    (void)atomic_fetch_add_explicit(
        &shared->ready_count,
        1U,
        memory_order_acq_rel);
    while(!atomic_load_explicit(&shared->start, memory_order_acquire))
    {
        thrd_yield();
    }

    for(unsigned int attempt = 0U; attempt < CONTENTION_ATTEMPTS; ++attempt)
    {
        const int32_t status = reachy_sim_step(shared->handle, 1U);
        if(status == REACHY_SIM_STATUS_OK)
        {
            (void)atomic_fetch_add_explicit(
                &shared->success_count,
                1U,
                memory_order_relaxed);
        }
        else if(status == REACHY_SIM_STATUS_HANDLE_BUSY)
        {
            (void)atomic_fetch_add_explicit(
                &shared->busy_count,
                1U,
                memory_order_relaxed);
        }
        else
        {
            (void)atomic_fetch_add_explicit(
                &shared->unexpected_count,
                1U,
                memory_order_relaxed);
        }
    }
    return 0;
}

static void test_threaded_contention(void)
{
    ReachySimHandle handle = create_handle("contention-model");
    reachy_sim_blocking_backend_reset_controls();

    ContentionShared shared = {0};
    shared.handle = handle;
    atomic_init(&shared.ready_count, 0U);
    atomic_init(&shared.start, false);
    atomic_init(&shared.success_count, 0U);
    atomic_init(&shared.busy_count, 0U);
    atomic_init(&shared.unexpected_count, 0U);

    thrd_t threads[CONTENTION_THREAD_COUNT];
    bool created[CONTENTION_THREAD_COUNT] = {false};
    for(unsigned int index = 0U; index < CONTENTION_THREAD_COUNT; ++index)
    {
        created[index] =
            thrd_create(&threads[index], contention_thread, &shared) == thrd_success;
        check_true(created[index], "create contention thread");
    }

    const struct timespec delay = {0, 1000000L};
    for(unsigned int attempt = 0U; attempt < 5000U; ++attempt)
    {
        if(atomic_load_explicit(&shared.ready_count, memory_order_acquire) ==
           CONTENTION_THREAD_COUNT)
        {
            break;
        }
        (void)thrd_sleep(&delay, NULL);
    }
    check_true(
        atomic_load_explicit(&shared.ready_count, memory_order_acquire) ==
            CONTENTION_THREAD_COUNT,
        "all contention threads reached start barrier");
    atomic_store_explicit(&shared.start, true, memory_order_release);

    for(unsigned int index = 0U; index < CONTENTION_THREAD_COUNT; ++index)
    {
        if(created[index])
        {
            int thread_result = -1;
            check_true(
                thrd_join(threads[index], &thread_result) == thrd_success,
                "join contention thread");
            check_true(thread_result == 0, "contention thread result");
        }
    }

    const unsigned int successes =
        atomic_load_explicit(&shared.success_count, memory_order_acquire);
    const unsigned int busy =
        atomic_load_explicit(&shared.busy_count, memory_order_acquire);
    const unsigned int unexpected =
        atomic_load_explicit(&shared.unexpected_count, memory_order_acquire);
    check_true(unexpected == 0U, "contention returns only success or handle busy");
    check_true(successes > 0U, "contention permits forward progress");
    check_true(
        successes + busy == CONTENTION_THREAD_COUNT * CONTENTION_ATTEMPTS,
        "every contention attempt has one typed result");

    ReachySimStateHeader state = {0};
    size_t required_size = 0U;
    check_status(
        reachy_sim_copy_state(
            handle,
            &state,
            sizeof(state),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "copy state after contention");
    check_true(
        state.sequence == (uint64_t)successes,
        "state sequence equals successful serialized operations");

    check_status(
        reachy_sim_destroy(handle),
        REACHY_SIM_STATUS_OK,
        "destroy contended handle");
}

int main(void)
{
    test_output_contracts();
    test_initialized_create_outputs();
    test_exclusive_operation_lease();
    test_threaded_contention();

    if(g_failures != 0)
    {
        (void)fprintf(stderr, "%d RMA-030 concurrency test(s) failed\n", g_failures);
        return 1;
    }
    (void)printf("RMA-030 handle concurrency and output-buffer tests passed\n");
    return 0;
}

int reachy_sim_contract_base_main(void);
#define main reachy_sim_contract_base_main
#include "reachy_sim_contract_test_base.inc"
#undef main

#define g_failures g_rma030_failures
#define check_true rma030_check_true
#define check_status rma030_check_status
#define initialized_error rma030_initialized_error
#define main reachy_sim_rma030_original_main
#include "reachy_sim_concurrency_test.inc"
#undef main
#undef initialized_error
#undef check_status
#undef check_true
#undef g_failures

static void rma030_test_output_contracts(void)
{
    static const uint8_t SENTINEL = UINT8_C(0xa5);
    ReachySimHandle handle = create_handle("output-contract-model");

    size_t required_size = SIZE_MAX;
    rma030_check_status(
        reachy_sim_copy_state(handle, NULL, 0U, &required_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "state size query");
    rma030_check_true(
        required_size == sizeof(ReachySimStateHeader),
        "state size query returns exact size");

    required_size = SIZE_MAX;
    rma030_check_status(
        reachy_sim_copy_state(handle, NULL, 1U, &required_size),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "state null buffer with nonzero capacity");
    rma030_check_true(
        required_size == SIZE_MAX,
        "invalid state copy leaves required size unchanged");

    uint8_t state_bytes[sizeof(ReachySimStateHeader)] = {0};
    memset(state_bytes, SENTINEL, sizeof(state_bytes));
    required_size = SIZE_MAX;
    rma030_check_status(
        reachy_sim_copy_state(
            handle,
            state_bytes,
            sizeof(state_bytes) - 1U,
            &required_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "undersized state buffer");
    rma030_check_true(
        required_size == sizeof(state_bytes),
        "undersized state reports exact size");
    rma030_check_true(
        bytes_equal_value(state_bytes, sizeof(state_bytes), SENTINEL),
        "undersized state buffer is not partially mutated");

    rma030_check_status(
        reachy_sim_copy_state(
            handle,
            state_bytes,
            sizeof(state_bytes),
            NULL),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "state copy requires size output");
    rma030_check_true(
        bytes_equal_value(state_bytes, sizeof(state_bytes), SENTINEL),
        "missing size output leaves state buffer unchanged");

    required_size = 0U;
    rma030_check_status(
        reachy_sim_copy_state(
            handle,
            state_bytes,
            sizeof(state_bytes),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "full state copy");
    rma030_check_true(
        required_size == sizeof(state_bytes),
        "full state copy size");

    size_t snapshot_size = SIZE_MAX;
    rma030_check_status(
        reachy_sim_copy_snapshot(handle, NULL, 0U, &snapshot_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "snapshot size query");
    rma030_check_true(
        snapshot_size > sizeof(ReachySimSnapshotHeader),
        "snapshot size query includes a backend payload");

    uint8_t snapshot_bytes[256] = {0};
    rma030_check_true(
        snapshot_size <= sizeof(snapshot_bytes),
        "snapshot fits the contract-test buffer");
    if(snapshot_size <= sizeof(snapshot_bytes))
    {
        memset(snapshot_bytes, SENTINEL, sizeof(snapshot_bytes));
        size_t actual_snapshot_size = SIZE_MAX;
        rma030_check_status(
            reachy_sim_copy_snapshot(
                handle,
                snapshot_bytes,
                snapshot_size - 1U,
                &actual_snapshot_size),
            REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
            "undersized snapshot buffer");
        rma030_check_true(
            actual_snapshot_size == snapshot_size,
            "undersized snapshot reports exact size");
        rma030_check_true(
            bytes_equal_value(snapshot_bytes, sizeof(snapshot_bytes), SENTINEL),
            "undersized snapshot buffer is not partially mutated");

        actual_snapshot_size = 0U;
        rma030_check_status(
            reachy_sim_copy_snapshot(
                handle,
                snapshot_bytes,
                snapshot_size,
                &actual_snapshot_size),
            REACHY_SIM_STATUS_OK,
            "full snapshot copy");
        rma030_check_true(
            actual_snapshot_size == snapshot_size,
            "full snapshot copy size");
    }

    rma030_check_status(
        reachy_sim_destroy(handle),
        REACHY_SIM_STATUS_OK,
        "destroy output-contract handle");
}

int reachy_sim_rma030_main(void)
{
    rma030_test_output_contracts();
    test_initialized_create_outputs();
    test_exclusive_operation_lease();
    test_threaded_contention();

    if(g_rma030_failures != 0)
    {
        (void)fprintf(
            stderr,
            "%d RMA-030 concurrency test(s) failed\n",
            g_rma030_failures);
        return 1;
    }
    (void)printf(
        "RMA-030 handle concurrency and output-buffer tests passed\n");
    return 0;
}

int main(void)
{
    const int base_result = reachy_sim_contract_base_main();
    const int hardening_result = reachy_sim_rma030_main();
    return base_result != 0 || hardening_result != 0 ? 1 : 0;
}

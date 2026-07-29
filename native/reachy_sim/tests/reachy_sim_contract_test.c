#include "reachy_sim.h"

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

_Static_assert(sizeof(ReachySimHandle) == 8U, "ReachySimHandle layout changed");
_Static_assert(sizeof(ReachySimConfig) == 24U, "ReachySimConfig layout changed");
_Static_assert(sizeof(ReachySimCapabilities) == 40U, "ReachySimCapabilities layout changed");
_Static_assert(sizeof(ReachySimStateHeader) == 48U, "ReachySimStateHeader layout changed");
_Static_assert(sizeof(ReachySimCommandBatchHeader) == 24U, "ReachySimCommandBatchHeader layout changed");
_Static_assert(sizeof(ReachySimWrenchCommand) == 96U, "ReachySimWrenchCommand layout changed");
_Static_assert(sizeof(ReachySimSnapshotHeader) == 40U, "ReachySimSnapshotHeader layout changed");
_Static_assert(sizeof(ReachySimErrorInfo) == 272U, "ReachySimErrorInfo layout changed");

static int g_failures = 0;

static void check_true(int condition, const char* message)
{
    if(condition == 0)
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

static ReachySimErrorInfo initialized_error(void)
{
    ReachySimErrorInfo error = {0};
    error.abi_version = REACHY_SIM_ABI_VERSION;
    error.struct_size = (uint32_t)sizeof(error);
    return error;
}

static ReachySimHandle create_valid_handle(void)
{
    static const uint8_t MODEL[] = "contract-test-model";
    ReachySimConfig config = reachy_sim_default_config();
    ReachySimHandle handle = REACHY_SIM_INVALID_HANDLE;
    ReachySimErrorInfo error = initialized_error();
    const int32_t status = reachy_sim_create(
        MODEL,
        sizeof(MODEL),
        &config,
        &handle,
        &error);
    check_status(
        status,
        REACHY_SIM_STATUS_OK,
        "create valid handle");
    check_true(
        handle != REACHY_SIM_INVALID_HANDLE,
        "valid create returned invalid handle");
    check_status(
        error.status,
        REACHY_SIM_STATUS_OK,
        "valid create error state");
    return handle;
}

static void test_metadata(void)
{
    check_true(
        reachy_sim_abi_version() == REACHY_SIM_ABI_VERSION,
        "ABI version query");
    check_true(
        strcmp(
            reachy_sim_version_string(),
            "0.2.0-abi-contract") == 0,
        "version string query");
    check_true(
        reachy_sim_status_recoverability(
            REACHY_SIM_STATUS_BUFFER_TOO_SMALL) ==
            REACHY_SIM_RECOVERABILITY_RETRY,
        "buffer-too-small recoverability");

    ReachySimConfig config = reachy_sim_default_config();
    check_true(
        config.abi_version == REACHY_SIM_ABI_VERSION,
        "default config ABI");
    check_true(
        config.struct_size == sizeof(config),
        "default config size");
    check_true(
        fabs(config.timestep_seconds - 0.002) < 1e-12,
        "default timestep");

    ReachySimCapabilities capabilities = {0};
    capabilities.abi_version = REACHY_SIM_ABI_VERSION;
    capabilities.struct_size = (uint32_t)sizeof(capabilities);
    check_status(
        reachy_sim_get_capabilities(&capabilities),
        REACHY_SIM_STATUS_OK,
        "global capabilities");
    check_true(
        capabilities.max_model_bytes > 0U,
        "model limit is present");
    check_true(
        capabilities.max_command_bytes > 0U,
        "command limit is present");
    check_true(
        capabilities.max_snapshot_bytes > 0U,
        "snapshot limit is present");
}

static void test_create_failures(void)
{
    static const uint8_t MODEL[] = "contract-test-model";
    ReachySimConfig config = reachy_sim_default_config();
    ReachySimHandle handle = UINT64_C(123);
    ReachySimErrorInfo error = initialized_error();

    check_status(
        reachy_sim_create(
            MODEL,
            sizeof(MODEL),
            &config,
            NULL,
            &error),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "null out handle");
    check_status(
        error.status,
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "null out handle error");

    config.abi_version = REACHY_SIM_ABI_VERSION + 1U;
    check_status(
        reachy_sim_create(
            MODEL,
            sizeof(MODEL),
            &config,
            &handle,
            &error),
        REACHY_SIM_STATUS_ABI_MISMATCH,
        "config ABI mismatch");
    check_true(
        handle == REACHY_SIM_INVALID_HANDLE,
        "failed create cleared handle");
    check_status(
        error.status,
        REACHY_SIM_STATUS_ABI_MISMATCH,
        "config ABI error info");

    config = reachy_sim_default_config();
    config.struct_size -= 1U;
    check_status(
        reachy_sim_create(
            MODEL,
            sizeof(MODEL),
            &config,
            &handle,
            &error),
        REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH,
        "config size mismatch");

    config = reachy_sim_default_config();
    check_status(
        reachy_sim_create(
            NULL,
            0U,
            &config,
            &handle,
            &error),
        REACHY_SIM_STATUS_MODEL_EMPTY,
        "empty model");
}

static void test_state_commands_wrench_and_snapshots(void)
{
    ReachySimHandle handle = create_valid_handle();

    ReachySimCapabilities handle_capabilities = {0};
    handle_capabilities.abi_version = REACHY_SIM_ABI_VERSION;
    handle_capabilities.struct_size =
        (uint32_t)sizeof(handle_capabilities);
    check_status(
        reachy_sim_get_handle_capabilities(
            handle,
            &handle_capabilities),
        REACHY_SIM_STATUS_OK,
        "handle capabilities");
    check_true(
        (handle_capabilities.capability_flags &
         REACHY_SIM_CAPABILITY_STEP) != 0U,
        "handle step capability");

    size_t required_size = 0U;
    check_status(
        reachy_sim_copy_state(
            handle,
            NULL,
            0U,
            &required_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "state size query");
    check_true(
        required_size == sizeof(ReachySimStateHeader),
        "state size query result");

    ReachySimStateHeader state = {0};
    check_status(
        reachy_sim_copy_state(
            handle,
            &state,
            sizeof(state),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "initial state copy");
    check_true(state.sequence == 0U, "initial state sequence");

    check_status(
        reachy_sim_step(handle, 10U),
        REACHY_SIM_STATUS_OK,
        "step ten times");
    check_status(
        reachy_sim_copy_state(
            handle,
            &state,
            sizeof(state),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "stepped state copy");
    check_true(state.sequence == 10U, "stepped state sequence");
    check_true(
        fabs(state.simulation_time - 0.02) < 1e-12,
        "stepped simulation time");

    check_status(
        reachy_sim_step(handle, 0U),
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "zero step count");
    ReachySimErrorInfo last_error = initialized_error();
    check_status(
        reachy_sim_get_last_error(handle, &last_error),
        REACHY_SIM_STATUS_OK,
        "last error query");
    check_status(
        last_error.status,
        REACHY_SIM_STATUS_INVALID_ARGUMENT,
        "last error status");
    check_true(
        strstr(last_error.message, "step_count") != NULL,
        "last error message");

    ReachySimCommandBatchHeader commands = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimCommandBatchHeader),
        1U,
        0U,
        (uint32_t)sizeof(ReachySimCommandBatchHeader)};
    check_status(
        reachy_sim_submit_commands(
            handle,
            &commands,
            sizeof(commands)),
        REACHY_SIM_STATUS_OK,
        "valid command batch");
    check_status(
        reachy_sim_submit_commands(
            handle,
            &commands,
            sizeof(commands)),
        REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR,
        "stale command batch");

    ReachySimWrenchCommand wrench = {0};
    wrench.abi_version = REACHY_SIM_ABI_VERSION;
    wrench.struct_size = (uint32_t)sizeof(wrench);
    wrench.body_id = 1U;
    wrench.force_newtons[0] = 1.0;
    wrench.duration_seconds = 0.1;
    check_status(
        reachy_sim_apply_wrench(handle, &wrench),
        REACHY_SIM_STATUS_OK,
        "valid wrench");
    wrench.abi_version += 1U;
    check_status(
        reachy_sim_apply_wrench(handle, &wrench),
        REACHY_SIM_STATUS_ABI_MISMATCH,
        "wrench ABI mismatch");

    required_size = 0U;
    check_status(
        reachy_sim_copy_snapshot(
            handle,
            NULL,
            0U,
            &required_size),
        REACHY_SIM_STATUS_BUFFER_TOO_SMALL,
        "snapshot size query");
    check_true(
        required_size == sizeof(ReachySimSnapshotHeader),
        "snapshot size query result");

    ReachySimSnapshotHeader snapshot = {0};
    check_status(
        reachy_sim_copy_snapshot(
            handle,
            &snapshot,
            sizeof(snapshot),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "snapshot copy");
    check_status(
        reachy_sim_step(handle, 5U),
        REACHY_SIM_STATUS_OK,
        "step after snapshot");
    check_status(
        reachy_sim_restore_snapshot(
            handle,
            &snapshot,
            sizeof(snapshot)),
        REACHY_SIM_STATUS_OK,
        "snapshot restore");
    check_status(
        reachy_sim_copy_state(
            handle,
            &state,
            sizeof(state),
            &required_size),
        REACHY_SIM_STATUS_OK,
        "restored state copy");
    check_true(
        state.sequence == snapshot.sequence,
        "restored state sequence");

    snapshot.model_hash ^= UINT64_C(1);
    check_status(
        reachy_sim_restore_snapshot(
            handle,
            &snapshot,
            sizeof(snapshot)),
        REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE,
        "snapshot model mismatch");

    check_status(
        reachy_sim_reset(handle, 0U),
        REACHY_SIM_STATUS_OK,
        "neutral reset");
    check_status(
        reachy_sim_destroy(handle),
        REACHY_SIM_STATUS_OK,
        "destroy valid handle");
    check_status(
        reachy_sim_step(handle, 1U),
        REACHY_SIM_STATUS_STALE_HANDLE,
        "stale handle after destroy");
    check_status(
        reachy_sim_destroy(handle),
        REACHY_SIM_STATUS_STALE_HANDLE,
        "double destroy is stale");
}

static void test_lifecycle_stress(void)
{
    ReachySimHandle stale_handle = create_valid_handle();
    check_status(
        reachy_sim_destroy(stale_handle),
        REACHY_SIM_STATUS_OK,
        "destroy stale seed");
    ReachySimHandle replacement = create_valid_handle();
    check_true(
        replacement != stale_handle,
        "handle generation changed after reuse");
    check_status(
        reachy_sim_step(stale_handle, 1U),
        REACHY_SIM_STATUS_STALE_HANDLE,
        "old generation remains stale");
    check_status(
        reachy_sim_destroy(replacement),
        REACHY_SIM_STATUS_OK,
        "destroy replacement");

    for(uint32_t iteration = 0U;
        iteration < 1000U;
        ++iteration)
    {
        ReachySimHandle handle = create_valid_handle();
        check_status(
            reachy_sim_destroy(handle),
            REACHY_SIM_STATUS_OK,
            "lifecycle stress destroy");
    }

    check_status(
        reachy_sim_step(REACHY_SIM_INVALID_HANDLE, 1U),
        REACHY_SIM_STATUS_INVALID_HANDLE,
        "zero handle rejection");
}

int main(void)
{
    test_metadata();
    test_create_failures();
    test_state_commands_wrench_and_snapshots();
    test_lifecycle_stress();

    if(g_failures != 0)
    {
        (void)fprintf(
            stderr,
            "%d contract checks failed.\n",
            g_failures);
        return 1;
    }
    return 0;
}

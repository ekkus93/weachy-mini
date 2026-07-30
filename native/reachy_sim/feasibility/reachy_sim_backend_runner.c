#include "reachy_sim.h"

#include <errno.h>
#include <inttypes.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define RUNNER_MAX_MODEL_BYTES UINT64_C(67108864)

typedef struct RunnerCommandBatch {
    ReachySimCommandBatchHeader header;
    ReachySimActuatorCommand commands[];
} RunnerCommandBatch;

static int read_file(
    const char* path,
    uint8_t** output,
    size_t* output_size)
{
    FILE* const stream = fopen(path, "rb");
    if(stream == NULL)
    {
        (void)fprintf(stderr, "cannot open model %s: %s\n", path, strerror(errno));
        return 0;
    }
    if(fseek(stream, 0L, SEEK_END) != 0)
    {
        (void)fprintf(stderr, "cannot seek model %s\n", path);
        (void)fclose(stream);
        return 0;
    }
    const long length = ftell(stream);
    if(length <= 0L || (uint64_t)length > RUNNER_MAX_MODEL_BYTES)
    {
        (void)fprintf(stderr, "model size is invalid: %ld\n", length);
        (void)fclose(stream);
        return 0;
    }
    if(fseek(stream, 0L, SEEK_SET) != 0)
    {
        (void)fprintf(stderr, "cannot rewind model %s\n", path);
        (void)fclose(stream);
        return 0;
    }

    const size_t byte_count = (size_t)length;
    uint8_t* const bytes = malloc(byte_count);
    if(bytes == NULL)
    {
        (void)fprintf(stderr, "cannot allocate %zu model bytes\n", byte_count);
        (void)fclose(stream);
        return 0;
    }
    const size_t read_count = fread(bytes, 1U, byte_count, stream);
    const int close_status = fclose(stream);
    if(read_count != byte_count || close_status != 0)
    {
        (void)fprintf(stderr, "cannot read model %s\n", path);
        free(bytes);
        return 0;
    }
    *output = bytes;
    *output_size = byte_count;
    return 1;
}

static int parse_step_count(const char* value, uint32_t* output)
{
    errno = 0;
    char* end = NULL;
    const unsigned long parsed = strtoul(value, &end, 10);
    if(errno != 0 || end == value || *end != '\0' || parsed == 0UL ||
       parsed > UINT32_MAX)
    {
        return 0;
    }
    *output = (uint32_t)parsed;
    return 1;
}

static ReachySimErrorInfo initialized_error(void)
{
    ReachySimErrorInfo error = {0};
    error.abi_version = REACHY_SIM_ABI_VERSION;
    error.struct_size = (uint32_t)sizeof(error);
    return error;
}

static void print_handle_error(ReachySimHandle handle, const char* operation)
{
    ReachySimErrorInfo error = initialized_error();
    if(reachy_sim_get_last_error(handle, &error) == REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(
            stderr,
            "%s failed: status=%s recoverability=%u message=%s\n",
            operation,
            reachy_sim_status_string(error.status),
            error.recoverability,
            error.message);
    }
    else
    {
        (void)fprintf(stderr, "%s failed without retrievable diagnostics\n", operation);
    }
}

static int copy_state(
    ReachySimHandle handle,
    ReachySimStateHeader* state)
{
    size_t required_size = 0U;
    const int32_t status = reachy_sim_copy_state(
        handle,
        state,
        sizeof(*state),
        &required_size);
    if(status != REACHY_SIM_STATUS_OK || required_size != sizeof(*state))
    {
        print_handle_error(handle, "copy_state");
        return 0;
    }
    return 1;
}

static uint8_t* copy_snapshot(
    ReachySimHandle handle,
    size_t* byte_count)
{
    *byte_count = 0U;
    int32_t status = reachy_sim_copy_snapshot(handle, NULL, 0U, byte_count);
    if(status != REACHY_SIM_STATUS_BUFFER_TOO_SMALL ||
       *byte_count <= sizeof(ReachySimSnapshotHeader))
    {
        print_handle_error(handle, "snapshot size query");
        return NULL;
    }
    uint8_t* const bytes = malloc(*byte_count);
    if(bytes == NULL)
    {
        (void)fprintf(stderr, "snapshot allocation failed\n");
        return NULL;
    }
    status = reachy_sim_copy_snapshot(handle, bytes, *byte_count, byte_count);
    if(status != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "copy_snapshot");
        free(bytes);
        return NULL;
    }
    return bytes;
}

static RunnerCommandBatch* neutral_command_batch(
    uint32_t actuator_count,
    size_t* byte_count)
{
    const uint64_t command_bytes =
        (uint64_t)sizeof(ReachySimCommandBatchHeader) +
        (uint64_t)actuator_count *
            (uint64_t)sizeof(ReachySimActuatorCommand);
    if(command_bytes > UINT32_MAX)
    {
        return NULL;
    }
    *byte_count = (size_t)command_bytes;

    RunnerCommandBatch* const batch = calloc(1U, *byte_count);
    if(batch == NULL)
    {
        return NULL;
    }
    batch->header.abi_version = REACHY_SIM_ABI_VERSION;
    batch->header.struct_size = (uint32_t)sizeof(ReachySimCommandBatchHeader);
    batch->header.sequence = 1U;
    batch->header.command_count = actuator_count;
    batch->header.byte_count = (uint32_t)*byte_count;
    for(uint32_t index = 0U; index < actuator_count; ++index)
    {
        batch->commands[index].abi_version = REACHY_SIM_ABI_VERSION;
        batch->commands[index].struct_size =
            (uint32_t)sizeof(ReachySimActuatorCommand);
        batch->commands[index].actuator_id = index;
        batch->commands[index].control_value = 0.0;
    }
    return batch;
}

int main(int argc, char** argv)
{
    if(argc < 2 || argc > 3)
    {
        (void)fprintf(stderr, "usage: %s MODEL.mjb [STEP_COUNT]\n", argv[0]);
        return 2;
    }
    uint32_t step_count = 100U;
    if(argc == 3 && !parse_step_count(argv[2], &step_count))
    {
        (void)fprintf(stderr, "invalid step count: %s\n", argv[2]);
        return 2;
    }

    uint8_t* model_bytes = NULL;
    size_t model_size = 0U;
    if(!read_file(argv[1], &model_bytes, &model_size))
    {
        return 1;
    }

    ReachySimConfig config = reachy_sim_default_config();
    config.flags = REACHY_SIM_CONFIG_FLAG_MODEL_MJB;
    ReachySimHandle handle = REACHY_SIM_INVALID_HANDLE;
    ReachySimErrorInfo create_error = initialized_error();
    const int32_t create_status = reachy_sim_create(
        model_bytes,
        model_size,
        &config,
        &handle,
        &create_error);
    free(model_bytes);
    if(create_status != REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(
            stderr,
            "create failed: status=%s recoverability=%u message=%s\n",
            reachy_sim_status_string(create_status),
            create_error.recoverability,
            create_error.message);
        return 1;
    }

    int result = 1;
    ReachySimStateHeader initial = {0};
    ReachySimStateHeader final = {0};
    ReachySimCapabilities capabilities = {0};
    capabilities.abi_version = REACHY_SIM_ABI_VERSION;
    capabilities.struct_size = (uint32_t)sizeof(capabilities);
    if(reachy_sim_get_handle_capabilities(handle, &capabilities) != REACHY_SIM_STATUS_OK ||
       !copy_state(handle, &initial))
    {
        print_handle_error(handle, "initialization");
        goto cleanup;
    }

    size_t command_size = 0U;
    RunnerCommandBatch* const commands = neutral_command_batch(
        initial.actuator_count,
        &command_size);
    if(commands == NULL)
    {
        (void)fprintf(stderr, "command batch allocation failed\n");
        goto cleanup;
    }
    if(reachy_sim_submit_commands(handle, commands, command_size) != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "submit_commands");
        free(commands);
        goto cleanup;
    }
    free(commands);

    if(initial.body_count > 0U)
    {
        ReachySimWrenchCommand wrench = {0};
        wrench.abi_version = REACHY_SIM_ABI_VERSION;
        wrench.struct_size = (uint32_t)sizeof(wrench);
        wrench.body_id = 1U;
        wrench.duration_seconds = 0.0;
        if(reachy_sim_apply_wrench(handle, &wrench) != REACHY_SIM_STATUS_OK)
        {
            print_handle_error(handle, "apply_wrench");
            goto cleanup;
        }
    }

    if(reachy_sim_step(handle, step_count) != REACHY_SIM_STATUS_OK ||
       !copy_state(handle, &final))
    {
        print_handle_error(handle, "step");
        goto cleanup;
    }

    size_t checkpoint_size = 0U;
    uint8_t* const checkpoint = copy_snapshot(handle, &checkpoint_size);
    if(checkpoint == NULL)
    {
        goto cleanup;
    }
    if(reachy_sim_step(handle, 10U) != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "first replay step");
        free(checkpoint);
        goto cleanup;
    }
    size_t expected_size = 0U;
    uint8_t* const expected = copy_snapshot(handle, &expected_size);
    if(expected == NULL)
    {
        free(checkpoint);
        goto cleanup;
    }
    if(reachy_sim_restore_snapshot(handle, checkpoint, checkpoint_size) !=
           REACHY_SIM_STATUS_OK ||
       reachy_sim_step(handle, 10U) != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "snapshot replay");
        free(expected);
        free(checkpoint);
        goto cleanup;
    }
    size_t replay_size = 0U;
    uint8_t* const replay = copy_snapshot(handle, &replay_size);
    const int replay_identical = replay != NULL && replay_size == expected_size &&
        memcmp(replay, expected, expected_size) == 0;
    free(replay);
    free(expected);
    free(checkpoint);
    if(!replay_identical)
    {
        (void)fprintf(stderr, "snapshot replay was not byte-identical\n");
        goto cleanup;
    }

    const int32_t sleep_status = reachy_sim_reset(
        handle,
        (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST);
    if(sleep_status != REACHY_SIM_STATUS_OK &&
       sleep_status != REACHY_SIM_STATUS_UNSUPPORTED)
    {
        print_handle_error(handle, "sleep reset");
        goto cleanup;
    }
    if(reachy_sim_reset(
           handle,
           (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE) !=
       REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "neutral reset");
        goto cleanup;
    }

    (void)printf(
        "{\"status\":\"ok\",\"backend_version\":\"%s\","
        "\"capability_flags\":%" PRIu64 ",\"body_count\":%u,"
        "\"joint_count\":%u,\"actuator_count\":%u,"
        "\"completed_steps\":%u,\"sequence\":%" PRIu64 ","
        "\"simulated_seconds\":%.17g,\"snapshot_replay_identical\":true,"
        "\"sleep_reset_status\":\"%s\"}\n",
        reachy_sim_version_string(),
        capabilities.capability_flags,
        initial.body_count,
        initial.joint_count,
        initial.actuator_count,
        step_count,
        final.sequence,
        final.simulation_time,
        reachy_sim_status_string(sleep_status));
    result = 0;

cleanup:
    if(reachy_sim_destroy(handle) != REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(stderr, "destroy failed\n");
        result = 1;
    }
    return result;
}

#include "reachy_sim_backend.h"

#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct FakeBackendContext {
    ReachySimConfig config;
    ReachySimStateHeader state;
    uint64_t model_hash;
    uint64_t last_command_sequence;
    ReachySimCalibrationProfileId calibration_profile_id;
    uint32_t reset_pose_id;
} FakeBackendContext;

typedef struct FakeSnapshotPayload {
    ReachySimStateHeader state;
    uint64_t last_command_sequence;
    uint32_t reset_pose_id;
    uint32_t reserved;
} FakeSnapshotPayload;

_Static_assert(
    sizeof(FakeSnapshotPayload) == 64U,
    "FakeSnapshotPayload layout changed");

static void write_message(
    char* error,
    size_t error_size,
    const char* message)
{
    if(error != NULL && error_size > 0U)
    {
        (void)snprintf(error, error_size, "%s", message);
    }
}

static uint64_t hash_bytes(
    const uint8_t* bytes,
    size_t byte_count)
{
    uint64_t hash = UINT64_C(1469598103934665603);
    for(size_t index = 0U; index < byte_count; ++index)
    {
        hash ^= (uint64_t)bytes[index];
        hash *= UINT64_C(1099511628211);
    }
    return hash;
}

static bool valid_reset_pose(uint32_t reset_pose_id)
{
    return reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST ||
        reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE;
}

static uint32_t health_flags_for_reset_pose(uint32_t reset_pose_id)
{
    return reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST
        ? (uint32_t)REACHY_SIM_HEALTH_FLAG_SLEEPING
        : 0U;
}

static void fake_destroy(void* context)
{
    free(context);
}

static ReachySimStatus fake_reset(
    void* context,
    uint32_t reset_id,
    char* error,
    size_t error_size)
{
    FakeBackendContext* const fake = context;
    if(!valid_reset_pose(reset_id))
    {
        write_message(error, error_size, "unknown reset pose identifier");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    fake->state.sequence = 0U;
    fake->state.simulation_time = 0.0;
    fake->state.health_flags = health_flags_for_reset_pose(reset_id);
    fake->last_command_sequence = 0U;
    fake->reset_pose_id = reset_id;
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus fake_step(
    void* context,
    uint32_t step_count,
    char* error,
    size_t error_size)
{
    FakeBackendContext* const fake = context;
    if(UINT64_MAX - fake->state.sequence < (uint64_t)step_count)
    {
        write_message(error, error_size, "sequence would overflow");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    const double additional_time =
        (double)step_count * fake->config.timestep_seconds;
    const double next_time =
        fake->state.simulation_time + additional_time;
    if(!isfinite(next_time))
    {
        write_message(
            error,
            error_size,
            "simulation time became non-finite");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    fake->state.sequence += (uint64_t)step_count;
    fake->state.simulation_time = next_time;
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus fake_submit_commands(
    void* context,
    const void* bytes,
    size_t byte_count,
    char* error,
    size_t error_size)
{
    FakeBackendContext* const fake = context;
    ReachySimCommandBatchHeader header = {0};
    memcpy(&header, bytes, sizeof(header));

    if(header.abi_version != REACHY_SIM_ABI_VERSION)
    {
        write_message(error, error_size, "command ABI mismatch");
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(header.struct_size != (uint32_t)sizeof(header))
    {
        write_message(
            error,
            error_size,
            "command header size mismatch");
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if((size_t)header.byte_count != byte_count)
    {
        write_message(
            error,
            error_size,
            "command byte_count does not match buffer size");
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }
    if(header.command_count > fake->config.max_command_count)
    {
        write_message(
            error,
            error_size,
            "command count exceeds configured limit");
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }
    if(header.sequence <= fake->last_command_sequence &&
       fake->last_command_sequence != 0U)
    {
        write_message(error, error_size, "command sequence is stale");
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }

    fake->last_command_sequence = header.sequence;
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus fake_copy_state(
    void* context,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size,
    char* error,
    size_t error_size)
{
    const FakeBackendContext* const fake = context;
    *required_size = sizeof(fake->state);
    if(bytes == NULL || byte_capacity < sizeof(fake->state))
    {
        write_message(error, error_size, "state buffer is too small");
        return REACHY_SIM_STATUS_BUFFER_TOO_SMALL;
    }
    memcpy(bytes, &fake->state, sizeof(fake->state));
    return REACHY_SIM_STATUS_OK;
}

static bool finite_vector(const double values[3])
{
    return isfinite(values[0]) &&
        isfinite(values[1]) &&
        isfinite(values[2]);
}

static ReachySimStatus fake_apply_wrench(
    void* context,
    const ReachySimWrenchCommand* command,
    char* error,
    size_t error_size)
{
    (void)context;
    if(!finite_vector(command->force_newtons) ||
       !finite_vector(command->torque_newton_metres) ||
       !finite_vector(command->application_point_metres) ||
       !isfinite(command->duration_seconds) ||
       command->duration_seconds < 0.0)
    {
        write_message(
            error,
            error_size,
            "wrench contains invalid numeric values");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus fake_copy_snapshot(
    void* context,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size,
    char* error,
    size_t error_size)
{
    const FakeBackendContext* const fake = context;
    const ReachySimSnapshotHeader header = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimSnapshotHeader),
        fake->model_hash,
        fake->state.sequence,
        fake->state.simulation_time,
        (uint32_t)sizeof(FakeSnapshotPayload),
        REACHY_SIM_SNAPSHOT_FORMAT_VERSION,
        fake->calibration_profile_id};
    const FakeSnapshotPayload payload = {
        fake->state,
        fake->last_command_sequence,
        fake->reset_pose_id,
        0U};
    const size_t snapshot_size = sizeof(header) + sizeof(payload);

    *required_size = snapshot_size;
    if(bytes == NULL || byte_capacity < snapshot_size)
    {
        write_message(
            error,
            error_size,
            "snapshot buffer is too small");
        return REACHY_SIM_STATUS_BUFFER_TOO_SMALL;
    }

    uint8_t* const output = bytes;
    memcpy(output, &header, sizeof(header));
    memcpy(output + sizeof(header), &payload, sizeof(payload));
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus fake_restore_snapshot(
    void* context,
    const void* bytes,
    size_t byte_count,
    char* error,
    size_t error_size)
{
    FakeBackendContext* const fake = context;
    const size_t expected_size =
        sizeof(ReachySimSnapshotHeader) + sizeof(FakeSnapshotPayload);
    if(byte_count != expected_size)
    {
        write_message(
            error,
            error_size,
            "snapshot size is unsupported");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    ReachySimSnapshotHeader header = {0};
    FakeSnapshotPayload payload = {0};
    const uint8_t* const input = bytes;
    memcpy(&header, input, sizeof(header));
    memcpy(&payload, input + sizeof(header), sizeof(payload));

    if(header.abi_version != REACHY_SIM_ABI_VERSION ||
       header.struct_size != (uint32_t)sizeof(header) ||
       header.snapshot_version != REACHY_SIM_SNAPSHOT_FORMAT_VERSION ||
       header.payload_size != (uint32_t)sizeof(payload) ||
       header.model_hash != fake->model_hash ||
       header.calibration_profile_id != fake->calibration_profile_id ||
       !isfinite(header.simulation_time) ||
       header.simulation_time < 0.0)
    {
        write_message(
            error,
            error_size,
            "snapshot header is incompatible");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    if(payload.state.abi_version != REACHY_SIM_ABI_VERSION ||
       payload.state.struct_size != (uint32_t)sizeof(payload.state) ||
       payload.state.sequence != header.sequence ||
       payload.state.simulation_time != header.simulation_time ||
       !isfinite(payload.state.simulation_time) ||
       payload.state.simulation_time < 0.0 ||
       !valid_reset_pose(payload.reset_pose_id) ||
       payload.reserved != 0U ||
       payload.state.health_flags !=
           health_flags_for_reset_pose(payload.reset_pose_id))
    {
        write_message(
            error,
            error_size,
            "snapshot payload is incompatible");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    fake->state = payload.state;
    fake->last_command_sequence = payload.last_command_sequence;
    fake->reset_pose_id = payload.reset_pose_id;
    return REACHY_SIM_STATUS_OK;
}

static const ReachySimBackendOperations FAKE_OPERATIONS = {
    fake_destroy,
    fake_reset,
    fake_step,
    fake_submit_commands,
    fake_copy_state,
    fake_apply_wrench,
    fake_copy_snapshot,
    fake_restore_snapshot};

ReachySimStatus reachy_sim_backend_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimBackendInstance* out_backend,
    char* error,
    size_t error_size)
{
    FakeBackendContext* const fake = calloc(1U, sizeof(*fake));
    if(fake == NULL)
    {
        write_message(
            error,
            error_size,
            "fake backend allocation failed");
        return REACHY_SIM_STATUS_ALLOCATION_FAILED;
    }

    fake->config = *config;
    fake->model_hash = hash_bytes(model_bytes, model_size);
    fake->calibration_profile_id =
        REACHY_SIM_CALIBRATION_PROFILE_UNCALIBRATED;
    fake->reset_pose_id =
        (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE;
    fake->state.abi_version = REACHY_SIM_ABI_VERSION;
    fake->state.struct_size = (uint32_t)sizeof(fake->state);
    fake->state.body_count = 1U;
    fake->state.joint_count = 1U;
    fake->state.actuator_count = 1U;

    out_backend->context = fake;
    out_backend->operations = &FAKE_OPERATIONS;
    out_backend->capability_flags =
        REACHY_SIM_CAPABILITY_RESET |
        REACHY_SIM_CAPABILITY_STEP |
        REACHY_SIM_CAPABILITY_COMMANDS |
        REACHY_SIM_CAPABILITY_STATE |
        REACHY_SIM_CAPABILITY_WRENCH |
        REACHY_SIM_CAPABILITY_SNAPSHOT;
    out_backend->model_hash = fake->model_hash;
    return REACHY_SIM_STATUS_OK;
}

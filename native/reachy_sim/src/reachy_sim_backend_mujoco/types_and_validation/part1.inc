#include "reachy_sim_backend.h"

#include <mujoco/mujoco.h>

#include <limits.h>
#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef REACHY_MUJOCO_EXPECTED_VERSION
#define REACHY_MUJOCO_EXPECTED_VERSION "3.9.0"
#endif

#define REACHY_MUJOCO_SNAPSHOT_PAYLOAD_VERSION UINT32_C(1)
#define REACHY_MUJOCO_KNOWN_CONFIG_FLAGS ((uint32_t)(\
    REACHY_SIM_CONFIG_FLAG_MODEL_XML | REACHY_SIM_CONFIG_FLAG_MODEL_MJB))
#define REACHY_MUJOCO_KNOWN_HEALTH_FLAGS ((uint32_t)(\
    REACHY_SIM_HEALTH_FLAG_SLEEPING | REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING))

typedef struct ReachyMujocoSnapshotPayloadHeader {
    uint32_t payload_version;
    uint32_t struct_size;
    uint64_t sequence;
    uint64_t last_command_sequence;
    uint32_t reset_pose_id;
    uint32_t health_flags;
    double timestep_seconds;
    uint32_t model_format;
    uint32_t reserved;
    uint64_t wrench_steps_remaining;
    ReachySimWrenchCommand pending_wrench;
    uint32_t state_count;
    uint32_t state_signature;
} ReachyMujocoSnapshotPayloadHeader;

_Static_assert(
    sizeof(ReachyMujocoSnapshotPayloadHeader) == 160U,
    "ReachyMujocoSnapshotPayloadHeader layout changed");

typedef struct ReachyMujocoBackendContext {
    ReachySimConfig config;
    mjModel* model;
    mjData* data;
    uint64_t model_hash;
    uint64_t sequence;
    uint64_t last_command_sequence;
    ReachySimCalibrationProfileId calibration_profile_id;
    uint32_t reset_pose_id;
    uint32_t health_flags;
    uint32_t model_format;
    uint32_t continuity_id;
    ReachySimWrenchCommand pending_wrench;
    uint64_t wrench_steps_remaining;
    int warning_counts[mjNWARNING];
    int integration_state_count;
    mjtNum* integration_state;
    mjtNum* restore_state;
} ReachyMujocoBackendContext;

typedef enum ReachyMujocoModelFormat {
    REACHY_MUJOCO_MODEL_FORMAT_XML = 1,
    REACHY_MUJOCO_MODEL_FORMAT_MJB = 2
} ReachyMujocoModelFormat;

static void write_message(
    char* error,
    size_t error_size,
    const char* message)
{
    if(error != NULL && error_size > 0U)
    {
        (void)snprintf(error, error_size, "%s", message == NULL ? "" : message);
    }
}

static void write_indexed_message(
    char* error,
    size_t error_size,
    const char* prefix,
    uint64_t index)
{
    if(error != NULL && error_size > 0U)
    {
        (void)snprintf(error, error_size, "%s %llu", prefix, (unsigned long long)index);
    }
}

static uint64_t hash_bytes(const uint8_t* bytes, size_t byte_count)
{
    uint64_t hash = UINT64_C(1469598103934665603);
    for(size_t index = 0U; index < byte_count; ++index)
    {
        hash ^= (uint64_t)bytes[index];
        hash *= UINT64_C(1099511628211);
    }
    return hash;
}

static bool checked_add_size(size_t left, size_t right, size_t* result)
{
    if(result == NULL || left > SIZE_MAX - right)
    {
        return false;
    }
    *result = left + right;
    return true;
}

static bool checked_multiply_size(size_t left, size_t right, size_t* result)
{
    if(result == NULL || (right != 0U && left > SIZE_MAX / right))
    {
        return false;
    }
    *result = left * right;
    return true;
}

static bool finite_values(const mjtNum* values, size_t count)
{
    if(values == NULL)
    {
        return count == 0U;
    }
    for(size_t index = 0U; index < count; ++index)
    {
        if(!isfinite((double)values[index]))
        {
            return false;
        }
    }
    return true;
}

static bool finite_vector3(const double values[3])
{
    return values != NULL &&
        isfinite(values[0]) &&
        isfinite(values[1]) &&
        isfinite(values[2]);
}

static bool valid_reset_pose(uint32_t reset_pose_id)
{
    return reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST ||
        reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE;
}

static uint32_t reset_health_flags(uint32_t reset_pose_id)
{
    return reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST
        ? (uint32_t)REACHY_SIM_HEALTH_FLAG_SLEEPING
        : 0U;
}

static void clear_pending_wrench(ReachyMujocoBackendContext* context)
{
    memset(&context->pending_wrench, 0, sizeof(context->pending_wrench));
    context->wrench_steps_remaining = 0U;
}

static void capture_warning_counts(ReachyMujocoBackendContext* context)
{
    for(int index = 0; index < mjNWARNING; ++index)
    {
        context->warning_counts[index] = context->data->warning[index].number;
    }
}

static bool current_state_has_warning(const ReachyMujocoBackendContext* context)
{
    for(int index = 0; index < mjNWARNING; ++index)
    {
        if(context->data->warning[index].number != 0)
        {
            return true;
        }
    }
    return false;
}

static void clear_warning_statistics(ReachyMujocoBackendContext* context)
{
    memset(context->data->warning, 0, sizeof(context->data->warning));
}

static bool dimension_fits_uint32(mjtSize value)
{
    return value >= 0 && (uint64_t)value <= UINT32_MAX;
}

static bool dimension_fits_int(mjtSize value)
{
    return value >= 0 && value <= (mjtSize)INT_MAX;
}


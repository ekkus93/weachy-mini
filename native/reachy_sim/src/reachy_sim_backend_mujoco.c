#include "reachy_sim_backend_mujoco/types_and_validation.inc"

#define reset_context reachy_mujoco_reset_context_without_continuity
#define mujoco_reset reachy_mujoco_reset_without_continuity
#include "reachy_sim_backend_mujoco/model_and_reset.inc"
#undef mujoco_reset
#undef reset_context

static ReachySimStatus advance_continuity_after(
    ReachyMujocoBackendContext* context,
    ReachySimStatus status,
    char* error,
    size_t error_size)
{
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }
    if(context->continuity_id == UINT32_MAX)
    {
        write_message(error, error_size, "authoritative state continuity identifier exhausted");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    ++context->continuity_id;
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus reset_context(
    ReachyMujocoBackendContext* context,
    uint32_t reset_id,
    char* error,
    size_t error_size)
{
    if(context->continuity_id == UINT32_MAX)
    {
        write_message(error, error_size, "authoritative state continuity identifier exhausted");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    return advance_continuity_after(
        context,
        reachy_mujoco_reset_context_without_continuity(
            context,
            reset_id,
            error,
            error_size),
        error,
        error_size);
}

static ReachySimStatus mujoco_reset(
    void* opaque_context,
    uint32_t reset_id,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    if(context->continuity_id == UINT32_MAX)
    {
        write_message(error, error_size, "authoritative state continuity identifier exhausted");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    return advance_continuity_after(
        context,
        reachy_mujoco_reset_without_continuity(
            opaque_context,
            reset_id,
            error,
            error_size),
        error,
        error_size);
}

#define mju_zero(values, count) reachy_mujoco_zero((values), (count))
#define mujoco_copy_state mujoco_copy_state_header_only
#include "reachy_sim_backend_mujoco/step_commands_wrench.inc"
#undef mujoco_copy_state
#undef mju_zero
#include "reachy_sim_backend_mujoco/state_payload.inc"

#define mujoco_restore_snapshot reachy_mujoco_restore_snapshot_without_continuity
#include "reachy_sim_backend_mujoco/snapshots.inc"
#undef mujoco_restore_snapshot

static ReachySimStatus mujoco_restore_snapshot(
    void* opaque_context,
    const void* bytes,
    size_t byte_count,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    if(context->continuity_id == UINT32_MAX)
    {
        write_message(error, error_size, "authoritative state continuity identifier exhausted");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    return advance_continuity_after(
        context,
        reachy_mujoco_restore_snapshot_without_continuity(
            opaque_context,
            bytes,
            byte_count,
            error,
            error_size),
        error,
        error_size);
}

#include "reachy_sim_backend_mujoco/create.inc"

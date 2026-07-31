#include "reachy_sim_state.h"

_Static_assert(sizeof(ReachySimStateRequest) == 24U, "ReachySimStateRequest layout changed");
_Static_assert(
    sizeof(ReachySimStatePayloadHeader) == 136U,
    "ReachySimStatePayloadHeader layout changed");
_Static_assert(
    sizeof(ReachySimDynamicsStatePayloadHeader) == 208U,
    "ReachySimDynamicsStatePayloadHeader layout changed");
_Static_assert(
    sizeof(ReachySimActuatorObservation) == 40U,
    "ReachySimActuatorObservation layout changed");
_Static_assert(sizeof(ReachySimBodyPose) == 64U, "ReachySimBodyPose layout changed");
_Static_assert(
    sizeof(ReachySimContactObservation) == 104U,
    "ReachySimContactObservation layout changed");
_Static_assert(
    sizeof(ReachySimHardStopObservation) == 64U,
    "ReachySimHardStopObservation layout changed");

typedef struct ReachyMujocoStateLayout {
    size_t total_size;
    size_t qpos_offset;
    size_t qvel_offset;
    size_t actuator_offset;
    size_t body_offset;
} ReachyMujocoStateLayout;

static ReachySimStatus validate_state_request(
    const void* bytes,
    size_t byte_capacity,
    bool* requested,
    uint32_t* requested_version,
    char* error,
    size_t error_size)
{
    *requested = false;
    *requested_version = 0U;
    if(bytes == NULL || byte_capacity < sizeof(uint64_t))
    {
        return REACHY_SIM_STATUS_OK;
    }

    uint64_t magic = 0U;
    memcpy(&magic, bytes, sizeof(magic));
    if(magic != REACHY_SIM_STATE_REQUEST_MAGIC)
    {
        return REACHY_SIM_STATUS_OK;
    }
    *requested = true;
    if(byte_capacity < sizeof(ReachySimStateRequest))
    {
        write_message(error, error_size, "authoritative state request is truncated");
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }

    ReachySimStateRequest request = {0};
    memcpy(&request, bytes, sizeof(request));
    if(request.abi_version != REACHY_SIM_ABI_VERSION)
    {
        write_message(error, error_size, "authoritative state request ABI mismatch");
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(request.struct_size != (uint32_t)sizeof(request))
    {
        write_message(error, error_size, "authoritative state request size mismatch");
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if(request.state_format_version != REACHY_SIM_STATE_FORMAT_VERSION &&
       request.state_format_version != REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION)
    {
        write_message(error, error_size, "authoritative state format is unsupported");
        return REACHY_SIM_STATUS_UNSUPPORTED;
    }
    if(request.reserved != 0U)
    {
        write_message(error, error_size, "authoritative state request reserved field is nonzero");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    *requested_version = request.state_format_version;
    return REACHY_SIM_STATUS_OK;
}

static bool calculate_state_layout(
    const ReachyMujocoBackendContext* context,
    ReachyMujocoStateLayout* layout)
{
    size_t qpos_bytes = 0U;
    size_t qvel_bytes = 0U;
    size_t actuator_bytes = 0U;
    size_t body_bytes = 0U;
    size_t offset = 0U;
    if(context == NULL || layout == NULL || context->model->nbody < 1 ||
       !checked_multiply_size((size_t)context->model->nq, sizeof(double), &qpos_bytes) ||
       !checked_multiply_size((size_t)context->model->nv, sizeof(double), &qvel_bytes) ||
       !checked_multiply_size(
           (size_t)context->model->nu,
           sizeof(ReachySimActuatorObservation),
           &actuator_bytes) ||
       !checked_multiply_size(
           (size_t)(context->model->nbody - 1),
           sizeof(ReachySimBodyPose),
           &body_bytes) ||
       !checked_add_size(
           sizeof(ReachySimStateHeader),
           sizeof(ReachySimStatePayloadHeader),
           &offset))
    {
        return false;
    }

    layout->qpos_offset = offset;
    if(!checked_add_size(offset, qpos_bytes, &offset))
    {
        return false;
    }
    layout->qvel_offset = offset;
    if(!checked_add_size(offset, qvel_bytes, &offset))
    {
        return false;
    }
    layout->actuator_offset = offset;
    if(!checked_add_size(offset, actuator_bytes, &offset))
    {
        return false;
    }
    layout->body_offset = offset;
    if(!checked_add_size(offset, body_bytes, &layout->total_size))
    {
        return false;
    }
    return true;
}

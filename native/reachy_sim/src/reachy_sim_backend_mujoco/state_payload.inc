#include "reachy_sim_state.h"

_Static_assert(sizeof(ReachySimStateRequest) == 24U, "ReachySimStateRequest layout changed");
_Static_assert(
    sizeof(ReachySimStatePayloadHeader) == 136U,
    "ReachySimStatePayloadHeader layout changed");
_Static_assert(
    sizeof(ReachySimActuatorObservation) == 40U,
    "ReachySimActuatorObservation layout changed");
_Static_assert(sizeof(ReachySimBodyPose) == 64U, "ReachySimBodyPose layout changed");

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
    char* error,
    size_t error_size)
{
    *requested = false;
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
    if(request.state_format_version != REACHY_SIM_STATE_FORMAT_VERSION)
    {
        write_message(error, error_size, "authoritative state format is unsupported");
        return REACHY_SIM_STATUS_UNSUPPORTED;
    }
    if(request.reserved != 0U)
    {
        write_message(error, error_size, "authoritative state request reserved field is nonzero");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
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

static ReachySimStatus calculate_constraint_diagnostics(
    const ReachyMujocoBackendContext* context,
    uint32_t* constraint_count,
    uint32_t* equality_constraint_count,
    double* maximum_constraint_residual,
    double* maximum_equality_constraint_residual,
    char* error,
    size_t error_size)
{
    const mjData* const data = context->data;
    if(!dimension_fits_uint32(data->nefc) ||
       (data->nefc > 0 && (data->efc_pos == NULL || data->efc_type == NULL)))
    {
        write_message(error, error_size, "MuJoCo constraint metadata is not representable");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }

    uint32_t equality_count = 0U;
    double maximum = 0.0;
    double equality_maximum = 0.0;
    for(mjtSize index = 0; index < data->nefc; ++index)
    {
        const double residual = fabs((double)data->efc_pos[index]);
        if(!isfinite(residual))
        {
            write_message(error, error_size, "MuJoCo constraint residual is non-finite");
            return REACHY_SIM_STATUS_NUMERIC_FAILURE;
        }
        if(residual > maximum)
        {
            maximum = residual;
        }
        if(data->efc_type[index] == mjCNSTR_EQUALITY)
        {
            ++equality_count;
            if(residual > equality_maximum)
            {
                equality_maximum = residual;
            }
        }
    }

    *constraint_count = (uint32_t)data->nefc;
    *equality_constraint_count = equality_count;
    *maximum_constraint_residual = maximum;
    *maximum_equality_constraint_residual = equality_maximum;
    return REACHY_SIM_STATUS_OK;
}

static bool total_warning_count(
    const ReachyMujocoBackendContext* context,
    uint64_t* warning_count)
{
    uint64_t total = 0U;
    for(int index = 0; index < mjNWARNING; ++index)
    {
        const int count = context->data->warning[index].number;
        if(count < 0 || UINT64_MAX - total < (uint64_t)count)
        {
            return false;
        }
        total += (uint64_t)count;
    }
    *warning_count = total;
    return true;
}

static void copy_numeric_array(
    uint8_t* output,
    size_t offset,
    const mjtNum* values,
    size_t count)
{
    for(size_t index = 0U; index < count; ++index)
    {
        const double value = (double)values[index];
        memcpy(output + offset + index * sizeof(value), &value, sizeof(value));
    }
}

static ReachySimStatus write_actuator_observations(
    const ReachyMujocoBackendContext* context,
    uint8_t* output,
    size_t offset,
    char* error,
    size_t error_size)
{
    const mjModel* const model = context->model;
    const mjData* const data = context->data;
    if(!finite_values(data->ctrl, (size_t)model->nu) ||
       !finite_values(data->actuator_force, (size_t)model->nu) ||
       !finite_values(data->actuator_length, (size_t)model->nu) ||
       !finite_values(data->actuator_velocity, (size_t)model->nu))
    {
        write_message(error, error_size, "MuJoCo actuator observations contain a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    for(uint32_t index = 0U; index < (uint32_t)model->nu; ++index)
    {
        const ReachySimActuatorObservation observation = {
            index,
            0U,
            (double)data->ctrl[index],
            (double)data->actuator_force[index],
            (double)data->actuator_length[index],
            (double)data->actuator_velocity[index]};
        memcpy(
            output + offset + (size_t)index * sizeof(observation),
            &observation,
            sizeof(observation));
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus write_body_poses(
    const ReachyMujocoBackendContext* context,
    uint8_t* output,
    size_t offset,
    char* error,
    size_t error_size)
{
    const mjModel* const model = context->model;
    const mjData* const data = context->data;
    if(!finite_values(data->xpos, (size_t)model->nbody * 3U) ||
       !finite_values(data->xquat, (size_t)model->nbody * 4U))
    {
        write_message(error, error_size, "MuJoCo body poses contain a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    for(uint32_t body_id = 1U; body_id < (uint32_t)model->nbody; ++body_id)
    {
        const size_t position_offset = (size_t)body_id * 3U;
        const size_t quaternion_offset = (size_t)body_id * 4U;
        const ReachySimBodyPose pose = {
            body_id,
            0U,
            {
                (double)data->xpos[position_offset],
                (double)data->xpos[position_offset + 1U],
                (double)data->xpos[position_offset + 2U]
            },
            {
                (double)data->xquat[quaternion_offset],
                (double)data->xquat[quaternion_offset + 1U],
                (double)data->xquat[quaternion_offset + 2U],
                (double)data->xquat[quaternion_offset + 3U]
            }};
        const size_t output_index = (size_t)(body_id - 1U);
        memcpy(
            output + offset + output_index * sizeof(pose),
            &pose,
            sizeof(pose));
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus mujoco_copy_state(
    void* opaque_context,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    bool requested = false;
    ReachySimStatus status = validate_state_request(
        bytes,
        byte_capacity,
        &requested,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }
    if(!requested)
    {
        return mujoco_copy_state_header_only(
            opaque_context,
            bytes,
            byte_capacity,
            required_size,
            error,
            error_size);
    }

    ReachyMujocoStateLayout layout = {0};
    if(!calculate_state_layout(context, &layout))
    {
        write_message(error, error_size, "authoritative state payload size overflow");
        return REACHY_SIM_STATUS_RESOURCE_EXHAUSTED;
    }
    *required_size = layout.total_size;
    if(byte_capacity < layout.total_size)
    {
        write_message(error, error_size, "authoritative state buffer is too small");
        return REACHY_SIM_STATUS_BUFFER_TOO_SMALL;
    }

    status = validate_finite_state(context, error, error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }

    uint32_t constraint_count = 0U;
    uint32_t equality_constraint_count = 0U;
    double maximum_constraint_residual = 0.0;
    double maximum_equality_constraint_residual = 0.0;
    status = calculate_constraint_diagnostics(
        context,
        &constraint_count,
        &equality_constraint_count,
        &maximum_constraint_residual,
        &maximum_equality_constraint_residual,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }

    uint64_t warning_count = 0U;
    if(!total_warning_count(context, &warning_count))
    {
        write_message(error, error_size, "MuJoCo warning count is invalid");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }

    size_t legacy_required_size = 0U;
    status = mujoco_copy_state_header_only(
        opaque_context,
        bytes,
        byte_capacity,
        &legacy_required_size,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK ||
       legacy_required_size != sizeof(ReachySimStateHeader))
    {
        return status == REACHY_SIM_STATUS_OK
            ? REACHY_SIM_STATUS_BACKEND_ERROR
            : status;
    }

    const ReachySimStatePayloadHeader payload = {
        REACHY_SIM_STATE_FORMAT_VERSION,
        (uint32_t)sizeof(ReachySimStatePayloadHeader),
        (uint64_t)layout.total_size,
        context->model_hash,
        context->sequence,
        (double)context->data->time,
        context->continuity_id,
        0U,
        (uint32_t)context->model->nq,
        (uint32_t)context->model->nv,
        (uint32_t)context->model->nu,
        (uint32_t)(context->model->nbody - 1),
        (uint64_t)layout.qpos_offset,
        (uint64_t)layout.qvel_offset,
        (uint64_t)layout.actuator_offset,
        (uint64_t)layout.body_offset,
        context->calibration_profile_id,
        warning_count,
        constraint_count,
        equality_constraint_count,
        maximum_constraint_residual,
        maximum_equality_constraint_residual};

    uint8_t* const output = bytes;
    memcpy(
        output + sizeof(ReachySimStateHeader),
        &payload,
        sizeof(payload));
    copy_numeric_array(
        output,
        layout.qpos_offset,
        context->data->qpos,
        (size_t)context->model->nq);
    copy_numeric_array(
        output,
        layout.qvel_offset,
        context->data->qvel,
        (size_t)context->model->nv);
    status = write_actuator_observations(
        context,
        output,
        layout.actuator_offset,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }
    return write_body_poses(
        context,
        output,
        layout.body_offset,
        error,
        error_size);
}

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

static void apply_pending_wrench(ReachyMujocoBackendContext* context)
{
    mju_zero(context->data->qfrc_applied, context->model->nv);
    if(context->wrench_steps_remaining == 0U)
    {
        return;
    }

    mj_applyFT(
        context->model,
        context->data,
        context->pending_wrench.force_newtons,
        context->pending_wrench.torque_newton_metres,
        context->pending_wrench.application_point_metres,
        (int)context->pending_wrench.body_id,
        context->data->qfrc_applied);
    --context->wrench_steps_remaining;
    if(context->wrench_steps_remaining == 0U)
    {
        memset(&context->pending_wrench, 0, sizeof(context->pending_wrench));
    }
}

static ReachySimStatus check_new_warnings(
    ReachyMujocoBackendContext* context,
    char* error,
    size_t error_size)
{
    for(int index = 0; index < mjNWARNING; ++index)
    {
        const int current = context->data->warning[index].number;
        if(current != context->warning_counts[index])
        {
            context->warning_counts[index] = current;
            context->health_flags |= (uint32_t)REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING;
            if(error != NULL && error_size > 0U)
            {
                (void)snprintf(
                    error,
                    error_size,
                    "MuJoCo warning %d was raised (count=%d, lastinfo=%d)",
                    index,
                    current,
                    context->data->warning[index].lastinfo);
            }
            return REACHY_SIM_STATUS_BACKEND_ERROR;
        }
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus mujoco_step(
    void* opaque_context,
    uint32_t step_count,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    if(UINT64_MAX - context->sequence < (uint64_t)step_count)
    {
        write_message(error, error_size, "simulation sequence would overflow");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    for(uint32_t step = 0U; step < step_count; ++step)
    {
        apply_pending_wrench(context);
        mj_step(context->model, context->data);
        ++context->sequence;

        ReachySimStatus status = validate_finite_state(context, error, error_size);
        if(status != REACHY_SIM_STATUS_OK)
        {
            return status;
        }
        status = check_new_warnings(context, error, error_size);
        if(status != REACHY_SIM_STATUS_OK)
        {
            return status;
        }
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus validate_command(
    const ReachyMujocoBackendContext* context,
    const ReachySimActuatorCommand* command,
    uint32_t command_index,
    char* error,
    size_t error_size)
{
    if(command->abi_version != REACHY_SIM_ABI_VERSION)
    {
        write_indexed_message(error, error_size, "actuator command ABI mismatch at index", command_index);
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(command->struct_size != (uint32_t)sizeof(*command))
    {
        write_indexed_message(error, error_size, "actuator command size mismatch at index", command_index);
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if(command->reserved != 0U)
    {
        write_indexed_message(error, error_size, "actuator command reserved field is nonzero at index", command_index);
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }
    if(command->actuator_id >= (uint32_t)context->model->nu)
    {
        write_indexed_message(error, error_size, "actuator identifier is out of range at index", command_index);
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }
    if(!isfinite(command->control_value))
    {
        write_indexed_message(error, error_size, "actuator control is non-finite at index", command_index);
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }

    const int actuator_id = (int)command->actuator_id;
    if(context->model->actuator_ctrllimited[actuator_id] != 0)
    {
        const mjtNum minimum = context->model->actuator_ctrlrange[2 * actuator_id];
        const mjtNum maximum = context->model->actuator_ctrlrange[2 * actuator_id + 1];
        if(!isfinite((double)minimum) || !isfinite((double)maximum) || minimum > maximum)
        {
            write_indexed_message(error, error_size, "actuator control range is invalid at index", command_index);
            return REACHY_SIM_STATUS_BACKEND_ERROR;
        }
        if(command->control_value < (double)minimum || command->control_value > (double)maximum)
        {
            write_indexed_message(error, error_size, "actuator control exceeds the model range at index", command_index);
            return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
        }
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus mujoco_submit_commands(
    void* opaque_context,
    const void* bytes,
    size_t byte_count,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    ReachySimCommandBatchHeader header = {0};
    memcpy(&header, bytes, sizeof(header));

    if(header.abi_version != REACHY_SIM_ABI_VERSION)
    {
        write_message(error, error_size, "command batch ABI mismatch");
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(header.struct_size != (uint32_t)sizeof(header))
    {
        write_message(error, error_size, "command batch header size mismatch");
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if(header.sequence == 0U ||
       (context->last_command_sequence != 0U &&
        header.sequence <= context->last_command_sequence))
    {
        write_message(error, error_size, "command batch sequence is zero or stale");
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }
    if(header.command_count > context->config.max_command_count)
    {
        write_message(error, error_size, "command count exceeds configured limit");
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }

    size_t commands_size = 0U;
    size_t expected_size = 0U;
    if(!checked_multiply_size(
           (size_t)header.command_count,
           sizeof(ReachySimActuatorCommand),
           &commands_size) ||
       !checked_add_size(sizeof(header), commands_size, &expected_size) ||
       expected_size != byte_count ||
       (size_t)header.byte_count != byte_count)
    {
        write_message(error, error_size, "command byte_count does not match the exact batch layout");
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }

    const uint8_t* const input = bytes;
    for(uint32_t index = 0U; index < header.command_count; ++index)
    {
        ReachySimActuatorCommand command = {0};
        memcpy(
            &command,
            input + sizeof(header) + (size_t)index * sizeof(command),
            sizeof(command));
        ReachySimStatus status = validate_command(
            context,
            &command,
            index,
            error,
            error_size);
        if(status != REACHY_SIM_STATUS_OK)
        {
            return status;
        }

        for(uint32_t prior = 0U; prior < index; ++prior)
        {
            ReachySimActuatorCommand prior_command = {0};
            memcpy(
                &prior_command,
                input + sizeof(header) + (size_t)prior * sizeof(prior_command),
                sizeof(prior_command));
            if(prior_command.actuator_id == command.actuator_id)
            {
                write_indexed_message(
                    error,
                    error_size,
                    "duplicate actuator identifier at command index",
                    index);
                return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
            }
        }
    }

    for(uint32_t index = 0U; index < header.command_count; ++index)
    {
        ReachySimActuatorCommand command = {0};
        memcpy(
            &command,
            input + sizeof(header) + (size_t)index * sizeof(command),
            sizeof(command));
        context->data->ctrl[command.actuator_id] = (mjtNum)command.control_value;
    }
    context->last_command_sequence = header.sequence;
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
    const ReachyMujocoBackendContext* const context = opaque_context;
    *required_size = sizeof(ReachySimStateHeader);
    if(bytes == NULL || byte_capacity < sizeof(ReachySimStateHeader))
    {
        write_message(error, error_size, "state buffer is too small");
        return REACHY_SIM_STATUS_BUFFER_TOO_SMALL;
    }

    const ReachySimStateHeader state = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimStateHeader),
        context->sequence,
        (double)context->data->time,
        (uint32_t)(context->model->nbody - 1),
        (uint32_t)context->model->njnt,
        (uint32_t)context->model->nu,
        (uint32_t)context->data->ncon,
        context->health_flags,
        0U};
    memcpy(bytes, &state, sizeof(state));
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus validate_wrench(
    const ReachyMujocoBackendContext* context,
    const ReachySimWrenchCommand* command,
    char* error,
    size_t error_size)
{
    if(command->abi_version != REACHY_SIM_ABI_VERSION)
    {
        write_message(error, error_size, "wrench ABI mismatch");
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(command->struct_size != (uint32_t)sizeof(*command))
    {
        write_message(error, error_size, "wrench structure size mismatch");
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if(command->body_id == 0U || command->body_id >= (uint64_t)context->model->nbody)
    {
        write_message(error, error_size, "wrench body identifier must name a non-world body");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(!finite_vector3(command->force_newtons) ||
       !finite_vector3(command->torque_newton_metres) ||
       !finite_vector3(command->application_point_metres) ||
       !isfinite(command->duration_seconds) ||
       command->duration_seconds < 0.0)
    {
        write_message(error, error_size, "wrench contains invalid numeric values");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus wrench_step_count(
    const ReachyMujocoBackendContext* context,
    const ReachySimWrenchCommand* command,
    uint64_t* steps,
    char* error,
    size_t error_size)
{
    if(command->duration_seconds == 0.0)
    {
        *steps = 1U;
        return REACHY_SIM_STATUS_OK;
    }

    const double raw_steps = ceil(command->duration_seconds / context->config.timestep_seconds);
    if(!isfinite(raw_steps) || raw_steps < 1.0 || raw_steps >= ldexp(1.0, 64))
    {
        write_message(error, error_size, "wrench duration exceeds the representable step count");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    *steps = (uint64_t)raw_steps;
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus mujoco_apply_wrench(
    void* opaque_context,
    const ReachySimWrenchCommand* command,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    ReachySimStatus status = validate_wrench(context, command, error, error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }

    uint64_t steps = 0U;
    status = wrench_step_count(context, command, &steps, error, error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }
    context->pending_wrench = *command;
    context->wrench_steps_remaining = steps;
    return REACHY_SIM_STATUS_OK;
}

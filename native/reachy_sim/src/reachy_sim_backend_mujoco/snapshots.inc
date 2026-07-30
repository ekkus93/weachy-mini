static bool snapshot_size(
    const ReachyMujocoBackendContext* context,
    size_t* total_size,
    uint32_t* payload_size)
{
    size_t state_bytes = 0U;
    size_t private_payload_size = 0U;
    if(context->integration_state_count < 0 ||
       !checked_multiply_size(
           (size_t)context->integration_state_count,
           sizeof(mjtNum),
           &state_bytes) ||
       !checked_add_size(
           sizeof(ReachyMujocoSnapshotPayloadHeader),
           state_bytes,
           &private_payload_size) ||
       private_payload_size > UINT32_MAX ||
       !checked_add_size(
           sizeof(ReachySimSnapshotHeader),
           private_payload_size,
           total_size))
    {
        return false;
    }
    *payload_size = (uint32_t)private_payload_size;
    return true;
}

static ReachySimStatus mujoco_copy_snapshot(
    void* opaque_context,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    if((context->health_flags &
        (uint32_t)REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING) != 0U)
    {
        write_message(error, error_size, "cannot snapshot a MuJoCo warning-faulted state");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }
    size_t total_size = 0U;
    uint32_t payload_size = 0U;
    if(!snapshot_size(context, &total_size, &payload_size))
    {
        write_message(error, error_size, "MuJoCo snapshot size overflow");
        return REACHY_SIM_STATUS_RESOURCE_EXHAUSTED;
    }
    *required_size = total_size;
    if(bytes == NULL || byte_capacity < total_size)
    {
        write_message(error, error_size, "snapshot buffer is too small");
        return REACHY_SIM_STATUS_BUFFER_TOO_SMALL;
    }

    mj_getState(
        context->model,
        context->data,
        context->integration_state,
        mjSTATE_INTEGRATION);
    if(!finite_values(
           context->integration_state,
           (size_t)context->integration_state_count))
    {
        write_message(error, error_size, "MuJoCo integration state contains a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    const ReachySimSnapshotHeader header = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimSnapshotHeader),
        context->model_hash,
        context->sequence,
        (double)context->data->time,
        payload_size,
        REACHY_SIM_SNAPSHOT_FORMAT_VERSION,
        context->calibration_profile_id};
    const ReachyMujocoSnapshotPayloadHeader payload = {
        REACHY_MUJOCO_SNAPSHOT_PAYLOAD_VERSION,
        (uint32_t)sizeof(ReachyMujocoSnapshotPayloadHeader),
        context->sequence,
        context->last_command_sequence,
        context->reset_pose_id,
        context->health_flags,
        context->config.timestep_seconds,
        context->model_format,
        0U,
        context->wrench_steps_remaining,
        context->pending_wrench,
        (uint32_t)context->integration_state_count,
        (uint32_t)mjSTATE_INTEGRATION};

    uint8_t* const output = bytes;
    memcpy(output, &header, sizeof(header));
    memcpy(output + sizeof(header), &payload, sizeof(payload));
    memcpy(
        output + sizeof(header) + sizeof(payload),
        context->integration_state,
        (size_t)context->integration_state_count * sizeof(mjtNum));
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus validate_restored_pending_wrench(
    const ReachyMujocoBackendContext* context,
    const ReachyMujocoSnapshotPayloadHeader* payload,
    char* error,
    size_t error_size)
{
    if(payload->wrench_steps_remaining == 0U)
    {
        ReachySimWrenchCommand empty = {0};
        if(memcmp(&payload->pending_wrench, &empty, sizeof(empty)) != 0)
        {
            write_message(error, error_size, "snapshot has a wrench value without remaining steps");
            return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
        }
        return REACHY_SIM_STATUS_OK;
    }

    const ReachySimStatus status = validate_wrench(
        context,
        &payload->pending_wrench,
        error,
        error_size);
    return status == REACHY_SIM_STATUS_OK
        ? REACHY_SIM_STATUS_OK
        : REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
}

static ReachySimStatus mujoco_restore_snapshot(
    void* opaque_context,
    const void* bytes,
    size_t byte_count,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    size_t expected_size = 0U;
    uint32_t expected_payload_size = 0U;
    if(!snapshot_size(context, &expected_size, &expected_payload_size) ||
       byte_count != expected_size)
    {
        write_message(error, error_size, "snapshot size is unsupported for this model");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    const uint8_t* const input = bytes;
    ReachySimSnapshotHeader header = {0};
    ReachyMujocoSnapshotPayloadHeader payload = {0};
    memcpy(&header, input, sizeof(header));
    memcpy(&payload, input + sizeof(header), sizeof(payload));

    if(header.abi_version != REACHY_SIM_ABI_VERSION ||
       header.struct_size != (uint32_t)sizeof(header) ||
       header.model_hash != context->model_hash ||
       header.snapshot_version != REACHY_SIM_SNAPSHOT_FORMAT_VERSION ||
       header.payload_size != expected_payload_size ||
       header.calibration_profile_id != context->calibration_profile_id ||
       !isfinite(header.simulation_time) ||
       header.simulation_time < 0.0)
    {
        write_message(error, error_size, "snapshot envelope is incompatible");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }
    if(payload.payload_version != REACHY_MUJOCO_SNAPSHOT_PAYLOAD_VERSION ||
       payload.struct_size != (uint32_t)sizeof(payload) ||
       payload.sequence != header.sequence ||
       !valid_reset_pose(payload.reset_pose_id) ||
       (payload.health_flags & (uint32_t)(~REACHY_MUJOCO_KNOWN_HEALTH_FLAGS)) != 0U ||
       (payload.health_flags &
        (uint32_t)REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING) != 0U ||
       ((payload.health_flags & REACHY_SIM_HEALTH_FLAG_SLEEPING) != 0U) !=
           (payload.reset_pose_id == (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST) ||
       payload.timestep_seconds != context->config.timestep_seconds ||
       payload.model_format != context->model_format ||
       payload.reserved != 0U ||
       payload.state_count != (uint32_t)context->integration_state_count ||
       payload.state_signature != (uint32_t)mjSTATE_INTEGRATION)
    {
        write_message(error, error_size, "snapshot MuJoCo payload header is incompatible");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    ReachySimStatus status = validate_restored_pending_wrench(
        context,
        &payload,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }

    mj_getState(
        context->model,
        context->data,
        context->restore_state,
        mjSTATE_INTEGRATION);
    mjWarningStat previous_warnings[mjNWARNING];
    memcpy(
        previous_warnings,
        context->data->warning,
        sizeof(previous_warnings));

    memcpy(
        context->integration_state,
        input + sizeof(header) + sizeof(payload),
        (size_t)context->integration_state_count * sizeof(mjtNum));
    if(!finite_values(
           context->integration_state,
           (size_t)context->integration_state_count))
    {
        write_message(error, error_size, "snapshot MuJoCo integration state is non-finite");
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    clear_warning_statistics(context);
    mj_setState(
        context->model,
        context->data,
        context->integration_state,
        mjSTATE_INTEGRATION);
    mj_forward(context->model, context->data);
    const bool time_matches =
        (double)context->data->time == header.simulation_time;
    status = validate_finite_state(context, error, error_size);
    const bool candidate_warned = current_state_has_warning(context);
    if(!time_matches || status != REACHY_SIM_STATUS_OK || candidate_warned)
    {
        mj_setState(
            context->model,
            context->data,
            context->restore_state,
            mjSTATE_INTEGRATION);
        clear_warning_statistics(context);
        mj_forward(context->model, context->data);
        memcpy(
            context->data->warning,
            previous_warnings,
            sizeof(previous_warnings));
        capture_warning_counts(context);
        if(!time_matches)
        {
            write_message(
                error,
                error_size,
                "snapshot simulation time does not match its MuJoCo state");
        }
        else if(candidate_warned)
        {
            write_message(
                error,
                error_size,
                "snapshot restore raised a MuJoCo warning");
        }
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    context->sequence = payload.sequence;
    context->last_command_sequence = payload.last_command_sequence;
    context->reset_pose_id = payload.reset_pose_id;
    context->health_flags = payload.health_flags;
    context->wrench_steps_remaining = payload.wrench_steps_remaining;
    context->pending_wrench = payload.pending_wrench;
    capture_warning_counts(context);
    return REACHY_SIM_STATUS_OK;
}

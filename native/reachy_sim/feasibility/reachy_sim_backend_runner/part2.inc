    if(result == NULL || left > UINT64_MAX - right)
    {
        return 0;
    }
    *result = left + right;
    return 1;
}

static int checked_multiply_u64(uint64_t left, uint64_t right, uint64_t* result)
{
    if(result == NULL || (right != 0U && left > UINT64_MAX / right))
    {
        return 0;
    }
    *result = left * right;
    return 1;
}

static double read_double(const uint8_t* bytes, uint64_t offset)
{
    double value = 0.0;
    memcpy(&value, bytes + (size_t)offset, sizeof(value));
    return value;
}

static int validate_state(const RunnerState* state)
{
    if(state == NULL || state->bytes == NULL ||
       state->byte_count < sizeof(ReachySimStateHeader) +
           sizeof(ReachySimStatePayloadHeader))
    {
        return 0;
    }
    const ReachySimStateHeader* const legacy = &state->legacy;
    const ReachySimStatePayloadHeader* const payload = &state->payload;
    if(legacy->abi_version != REACHY_SIM_ABI_VERSION ||
       legacy->struct_size != sizeof(*legacy) ||
       legacy->reserved != 0U ||
       payload->state_format_version != REACHY_SIM_STATE_FORMAT_VERSION ||
       payload->struct_size != sizeof(*payload) ||
       payload->total_size != state->byte_count ||
       payload->model_hash == 0U ||
       payload->sequence != legacy->sequence ||
       payload->simulation_time != legacy->simulation_time ||
       payload->continuity_id == 0U ||
       payload->reserved != 0U ||
       payload->body_pose_count != legacy->body_count ||
       payload->actuator_observation_count != legacy->actuator_count ||
       payload->equality_constraint_count > payload->constraint_count ||
       !isfinite(payload->maximum_constraint_residual) ||
       payload->maximum_constraint_residual < 0.0 ||
       !isfinite(payload->maximum_equality_constraint_residual) ||
       payload->maximum_equality_constraint_residual < 0.0)
    {
        return 0;
    }

    uint64_t qpos_bytes = 0U;
    uint64_t qvel_bytes = 0U;
    uint64_t actuator_bytes = 0U;
    uint64_t body_bytes = 0U;
    uint64_t qvel_offset = 0U;
    uint64_t actuator_offset = 0U;
    uint64_t body_offset = 0U;
    uint64_t total_size = 0U;
    const uint64_t qpos_offset =
        sizeof(ReachySimStateHeader) + sizeof(ReachySimStatePayloadHeader);
    if(!checked_multiply_u64(payload->qpos_count, sizeof(double), &qpos_bytes) ||
       !checked_multiply_u64(payload->qvel_count, sizeof(double), &qvel_bytes) ||
       !checked_multiply_u64(
           payload->actuator_observation_count,
           sizeof(ReachySimActuatorObservation),
           &actuator_bytes) ||
       !checked_multiply_u64(
           payload->body_pose_count,
           sizeof(ReachySimBodyPose),
           &body_bytes) ||
       !checked_add_u64(qpos_offset, qpos_bytes, &qvel_offset) ||
       !checked_add_u64(qvel_offset, qvel_bytes, &actuator_offset) ||
       !checked_add_u64(actuator_offset, actuator_bytes, &body_offset) ||
       !checked_add_u64(body_offset, body_bytes, &total_size) ||
       payload->qpos_offset != qpos_offset ||
       payload->qvel_offset != qvel_offset ||
       payload->actuator_observation_offset != actuator_offset ||
       payload->body_pose_offset != body_offset ||
       total_size != payload->total_size ||
       total_size > SIZE_MAX)
    {
        return 0;
    }

    for(uint32_t index = 0U; index < payload->qpos_count; ++index)
    {
        if(!isfinite(read_double(
               state->bytes,
               payload->qpos_offset + (uint64_t)index * sizeof(double))))
        {
            return 0;
        }
    }
    for(uint32_t index = 0U; index < payload->qvel_count; ++index)
    {
        if(!isfinite(read_double(
               state->bytes,
               payload->qvel_offset + (uint64_t)index * sizeof(double))))
        {
            return 0;
        }
    }
    for(uint32_t index = 0U;
        index < payload->actuator_observation_count;
        ++index)
    {
        ReachySimActuatorObservation observation = {0};
        memcpy(
            &observation,
            state->bytes + (size_t)payload->actuator_observation_offset +
                (size_t)index * sizeof(observation),
            sizeof(observation));
        if(observation.actuator_id != index || observation.reserved != 0U ||

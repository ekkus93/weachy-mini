           !isfinite(observation.control_value) ||
           !isfinite(observation.actuator_force) ||
           !isfinite(observation.length) ||
           !isfinite(observation.velocity))
        {
            return 0;
        }
    }
    for(uint32_t index = 0U; index < payload->body_pose_count; ++index)
    {
        ReachySimBodyPose pose = {0};
        memcpy(
            &pose,
            state->bytes + (size_t)payload->body_pose_offset +
                (size_t)index * sizeof(pose),
            sizeof(pose));
        double norm_squared = 0.0;
        if(pose.body_id != index + 1U || pose.reserved != 0U)
        {
            return 0;
        }
        for(size_t component = 0U; component < 3U; ++component)
        {
            if(!isfinite(pose.position_metres[component]))
            {
                return 0;
            }
        }
        for(size_t component = 0U; component < 4U; ++component)
        {
            const double value = pose.quaternion_wxyz[component];
            if(!isfinite(value))
            {
                return 0;
            }
            norm_squared += value * value;
        }
        if(!isfinite(norm_squared) || norm_squared <= 1.0e-24)
        {
            return 0;
        }
    }
    return 1;
}

static void free_state(RunnerState* state)
{
    if(state != NULL)
    {
        free(state->bytes);
        memset(state, 0, sizeof(*state));
    }
}

static int copy_authoritative_state(
    ReachySimHandle handle,
    RunnerState* state)
{
    const ReachySimStateRequest request = {
        REACHY_SIM_STATE_REQUEST_MAGIC,
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimStateRequest),
        REACHY_SIM_STATE_FORMAT_VERSION,
        0U};
    uint8_t query[sizeof(request)] = {0};
    memcpy(query, &request, sizeof(request));
    size_t required_size = 0U;
    int32_t status = reachy_sim_copy_state(
        handle,
        query,
        sizeof(query),
        &required_size);
    if(status != REACHY_SIM_STATUS_BUFFER_TOO_SMALL ||
       required_size <= sizeof(ReachySimStateHeader) +
           sizeof(ReachySimStatePayloadHeader))
    {
        print_handle_error(handle, "authoritative state size query");
        return 0;
    }

    uint8_t* const bytes = calloc(required_size, 1U);
    if(bytes == NULL)
    {
        (void)fprintf(stderr, "authoritative state allocation failed\n");
        return 0;
    }
    memcpy(bytes, &request, sizeof(request));
    size_t actual_size = 0U;
    status = reachy_sim_copy_state(
        handle,
        bytes,
        required_size,
        &actual_size);
    if(status != REACHY_SIM_STATUS_OK || actual_size != required_size)
    {
        print_handle_error(handle, "copy authoritative state");
        free(bytes);
        return 0;
    }

    memset(state, 0, sizeof(*state));
    state->bytes = bytes;
    state->byte_count = actual_size;
    memcpy(&state->legacy, bytes, sizeof(state->legacy));
    memcpy(
        &state->payload,
        bytes + sizeof(state->legacy),
        sizeof(state->payload));
    if(!validate_state(state))
    {
        (void)fprintf(stderr, "authoritative state payload validation failed\n");
        free_state(state);
        return 0;
    }
    return 1;
}

static uint8_t* copy_snapshot(
    ReachySimHandle handle,
    size_t* byte_count)

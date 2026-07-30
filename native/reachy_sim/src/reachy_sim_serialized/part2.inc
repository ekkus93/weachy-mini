int32_t reachy_sim_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimHandle* out_handle,
    ReachySimErrorInfo* out_error)
{
    if(out_handle != NULL)
    {
        *out_handle = REACHY_SIM_INVALID_HANDLE;
    }
    const ReachySimStatus validation_status =
        validate_create_error_output(out_error);
    if(validation_status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)validation_status;
    }
    return reachy_sim_create_unserialized(
        model_bytes,
        model_size,
        config,
        out_handle,
        out_error);
}

int32_t reachy_sim_destroy(ReachySimHandle handle)
{
    return public_busy_or_call(handle, reachy_sim_destroy_unserialized);
}

int32_t reachy_sim_reset(ReachySimHandle handle, uint32_t reset_id)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_reset_unserialized(handle, reset_id);
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_step(ReachySimHandle handle, uint32_t step_count)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_step_unserialized(handle, step_count);
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_submit_commands(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_submit_commands_unserialized(
        handle,
        bytes,
        byte_count);
    release_public_operation(lease);
    return status;
}

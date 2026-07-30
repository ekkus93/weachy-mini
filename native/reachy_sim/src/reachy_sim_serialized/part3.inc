int32_t reachy_sim_copy_state(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    if(required_size == NULL)
    {
        release_public_operation(lease);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(bytes == NULL && byte_capacity != 0U)
    {
        release_public_operation(lease);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    size_t local_required_size = 0U;
    int32_t status = reachy_sim_copy_state_unserialized(
        handle,
        bytes,
        byte_capacity,
        &local_required_size);
    const ReachySimStatus validated_status = validate_copy_result(
        status,
        bytes,
        byte_capacity,
        local_required_size);
    if(validated_status != (ReachySimStatus)status)
    {
        status = (int32_t)validated_status;
    }
    if(status == REACHY_SIM_STATUS_OK ||
       status == REACHY_SIM_STATUS_BUFFER_TOO_SMALL)
    {
        *required_size = local_required_size;
    }
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_apply_wrench(
    ReachySimHandle handle,
    const ReachySimWrenchCommand* command)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_apply_wrench_unserialized(
        handle,
        command);
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_copy_snapshot(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    if(required_size == NULL)
    {
        release_public_operation(lease);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(bytes == NULL && byte_capacity != 0U)
    {
        release_public_operation(lease);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    size_t local_required_size = 0U;
    int32_t status = reachy_sim_copy_snapshot_unserialized(
        handle,
        bytes,
        byte_capacity,
        &local_required_size);
    const ReachySimStatus validated_status = validate_copy_result(
        status,
        bytes,
        byte_capacity,
        local_required_size);
    if(validated_status != (ReachySimStatus)status)
    {
        status = (int32_t)validated_status;
    }
    if(status == REACHY_SIM_STATUS_OK ||
       status == REACHY_SIM_STATUS_BUFFER_TOO_SMALL)
    {
        *required_size = local_required_size;
    }
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_restore_snapshot(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_restore_snapshot_unserialized(
        handle,
        bytes,
        byte_count);
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_get_last_error(
    ReachySimHandle handle,
    ReachySimErrorInfo* out_error)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_get_last_error_unserialized(
        handle,
        out_error);
    release_public_operation(lease);
    return status;
}

int32_t reachy_sim_get_handle_capabilities(
    ReachySimHandle handle,
    ReachySimCapabilities* capabilities)
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = reachy_sim_get_handle_capabilities_unserialized(
        handle,
        capabilities);
    release_public_operation(lease);
    return status;
}

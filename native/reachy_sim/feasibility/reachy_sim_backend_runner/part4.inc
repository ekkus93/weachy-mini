{
    *byte_count = 0U;
    int32_t status = reachy_sim_copy_snapshot(handle, NULL, 0U, byte_count);
    if(status != REACHY_SIM_STATUS_BUFFER_TOO_SMALL ||
       *byte_count <= sizeof(ReachySimSnapshotHeader))
    {
        print_handle_error(handle, "snapshot size query");
        return NULL;
    }
    uint8_t* const bytes = malloc(*byte_count);
    if(bytes == NULL)
    {
        (void)fprintf(stderr, "snapshot allocation failed\n");
        return NULL;
    }
    status = reachy_sim_copy_snapshot(handle, bytes, *byte_count, byte_count);
    if(status != REACHY_SIM_STATUS_OK)
    {
        print_handle_error(handle, "copy snapshot");
        free(bytes);
        return NULL;
    }
    return bytes;
}

static RunnerCommandBatch* neutral_command_batch(
    uint32_t actuator_count,
    size_t* byte_count)
{
    const uint64_t command_bytes =
        (uint64_t)sizeof(ReachySimCommandBatchHeader) +
        (uint64_t)actuator_count * sizeof(ReachySimActuatorCommand);
    if(command_bytes > UINT32_MAX || command_bytes > SIZE_MAX)
    {
        return NULL;
    }
    *byte_count = (size_t)command_bytes;
    RunnerCommandBatch* const batch = calloc(1U, *byte_count);
    if(batch == NULL)
    {
        return NULL;
    }
    batch->header.abi_version = REACHY_SIM_ABI_VERSION;
    batch->header.struct_size = (uint32_t)sizeof(ReachySimCommandBatchHeader);
    batch->header.sequence = 1U;
    batch->header.command_count = actuator_count;
    batch->header.byte_count = (uint32_t)*byte_count;
    for(uint32_t index = 0U; index < actuator_count; ++index)
    {
        batch->commands[index].abi_version = REACHY_SIM_ABI_VERSION;
        batch->commands[index].struct_size =
            (uint32_t)sizeof(ReachySimActuatorCommand);
        batch->commands[index].actuator_id = index;
        batch->commands[index].control_value = 0.0;
    }
    return batch;
}

int main(int argc, char** argv)
{
    if(argc < 2 || argc > 3)
    {
        (void)fprintf(stderr, "usage: %s MODEL.mjb [STEP_COUNT]\n", argv[0]);
        return 2;
    }
    uint32_t step_count = 100U;
    if(argc == 3 && !parse_step_count(argv[2], &step_count))
    {
        (void)fprintf(stderr, "invalid step count: %s\n", argv[2]);
        return 2;
    }

    uint8_t* model_bytes = NULL;
    size_t model_size = 0U;
    if(!read_file(argv[1], &model_bytes, &model_size))
    {
        return 1;
    }

    ReachySimConfig config = reachy_sim_default_config();
    config.flags = REACHY_SIM_CONFIG_FLAG_MODEL_MJB;
    ReachySimHandle handle = REACHY_SIM_INVALID_HANDLE;
    ReachySimErrorInfo create_error = initialized_error();
    const int32_t create_status = reachy_sim_create(
        model_bytes,
        model_size,
        &config,
        &handle,
        &create_error);
    free(model_bytes);
    if(create_status != REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(
            stderr,
            "create failed: status=%s recoverability=%u message=%s\n",
            reachy_sim_status_string(create_status),
            create_error.recoverability,
            create_error.message);
        return 1;
    }

    int result = 1;
    RunnerState initial = {0};
    RunnerState final = {0};
    RunnerState restored = {0};
    RunnerState reset = {0};
    ReachySimCapabilities capabilities = {0};
    capabilities.abi_version = REACHY_SIM_ABI_VERSION;
    capabilities.struct_size = (uint32_t)sizeof(capabilities);
    if(reachy_sim_get_handle_capabilities(handle, &capabilities) !=
           REACHY_SIM_STATUS_OK ||
       !copy_authoritative_state(handle, &initial))
    {
        print_handle_error(handle, "initialization");
        goto cleanup;
    }

    size_t command_size = 0U;
    RunnerCommandBatch* const commands = neutral_command_batch(
        initial.legacy.actuator_count,

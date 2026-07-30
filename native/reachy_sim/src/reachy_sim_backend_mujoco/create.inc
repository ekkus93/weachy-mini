static const ReachySimBackendOperations MUJOCO_OPERATIONS = {
    mujoco_destroy,
    mujoco_reset,
    mujoco_step,
    mujoco_submit_commands,
    mujoco_copy_state,
    mujoco_apply_wrench,
    mujoco_copy_snapshot,
    mujoco_restore_snapshot};

ReachySimStatus reachy_sim_backend_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimBackendInstance* out_backend,
    char* error,
    size_t error_size)
{
    if(out_backend == NULL)
    {
        write_message(error, error_size, "backend output is null");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    memset(out_backend, 0, sizeof(*out_backend));
    if(model_bytes == NULL || model_size == 0U || config == NULL)
    {
        write_message(error, error_size, "model bytes and config are required");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(config->abi_version != REACHY_SIM_ABI_VERSION)
    {
        write_message(error, error_size, "MuJoCo backend config ABI mismatch");
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(config->struct_size != (uint32_t)sizeof(*config))
    {
        write_message(error, error_size, "MuJoCo backend config size mismatch");
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if(!isfinite(config->timestep_seconds) ||
       config->timestep_seconds <= 0.0 ||
       config->max_command_count == 0U)
    {
        write_message(error, error_size, "MuJoCo backend config values are invalid");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    if(mj_version() != mjVERSION_HEADER ||
       strcmp(mj_versionString(), REACHY_MUJOCO_EXPECTED_VERSION) != 0)
    {
        if(error != NULL && error_size > 0U)
        {
            (void)snprintf(
                error,
                error_size,
                "MuJoCo runtime mismatch: expected %s/header %d, received %s/header %d",
                REACHY_MUJOCO_EXPECTED_VERSION,
                mjVERSION_HEADER,
                mj_versionString(),
                mj_version());
        }
        return REACHY_SIM_STATUS_BACKEND_UNAVAILABLE;
    }

    ReachySimStatus format_status = REACHY_SIM_STATUS_OK;
    const ReachyMujocoModelFormat format = detect_model_format(
        model_bytes,
        model_size,
        config->flags,
        &format_status,
        error,
        error_size);
    if(format_status != REACHY_SIM_STATUS_OK)
    {
        return format_status;
    }

    mjModel* model = format == REACHY_MUJOCO_MODEL_FORMAT_XML
        ? load_xml_model(model_bytes, model_size, error, error_size)
        : load_mjb_model(model_bytes, model_size, error, error_size);
    if(model == NULL)
    {
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }
    if(!dimensions_fit_public_state(model))
    {
        mj_deleteModel(model);
        write_message(error, error_size, "MuJoCo model dimensions cannot be represented by the public state header");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }

    model->opt.timestep = (mjtNum)config->timestep_seconds;
    mjData* const data = mj_makeData(model);
    if(data == NULL)
    {
        mj_deleteModel(model);
        write_message(error, error_size, "MuJoCo could not allocate model data");
        return REACHY_SIM_STATUS_ALLOCATION_FAILED;
    }

    const int integration_state_count = mj_stateSize(model, mjSTATE_INTEGRATION);
    if(integration_state_count <= 0 ||
       (size_t)integration_state_count > SIZE_MAX / sizeof(mjtNum))
    {
        mj_deleteData(data);
        mj_deleteModel(model);
        write_message(error, error_size, "MuJoCo integration state size is invalid");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }
    mjtNum* const integration_state = calloc(
        (size_t)integration_state_count,
        sizeof(*integration_state));
    mjtNum* const restore_state = calloc(
        (size_t)integration_state_count,
        sizeof(*restore_state));
    if(integration_state == NULL || restore_state == NULL)
    {
        free(restore_state);
        free(integration_state);
        mj_deleteData(data);
        mj_deleteModel(model);
        write_message(error, error_size, "MuJoCo snapshot state allocation failed");
        return REACHY_SIM_STATUS_ALLOCATION_FAILED;
    }

    ReachyMujocoBackendContext* const context = calloc(1U, sizeof(*context));
    if(context == NULL)
    {
        free(restore_state);
        free(integration_state);
        mj_deleteData(data);
        mj_deleteModel(model);
        write_message(error, error_size, "MuJoCo backend context allocation failed");
        return REACHY_SIM_STATUS_ALLOCATION_FAILED;
    }

    context->config = *config;
    context->model = model;
    context->data = data;
    context->model_hash = hash_bytes(model_bytes, model_size);
    context->calibration_profile_id = REACHY_SIM_CALIBRATION_PROFILE_UNCALIBRATED;
    context->model_format = (uint32_t)format;
    context->integration_state_count = integration_state_count;
    context->integration_state = integration_state;
    context->restore_state = restore_state;

    ReachySimStatus status = reset_context(
        context,
        (uint32_t)REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        mujoco_destroy(context);
        return status;
    }

    uint64_t capabilities =
        REACHY_SIM_CAPABILITY_RESET |
        REACHY_SIM_CAPABILITY_STEP |
        REACHY_SIM_CAPABILITY_STATE |
        REACHY_SIM_CAPABILITY_SNAPSHOT;
    if(model->nu > 0)
    {
        capabilities |= REACHY_SIM_CAPABILITY_COMMANDS;
    }
    if(model->nbody > 1)
    {
        capabilities |= REACHY_SIM_CAPABILITY_WRENCH;
    }

    out_backend->context = context;
    out_backend->operations = &MUJOCO_OPERATIONS;
    out_backend->capability_flags = capabilities;
    out_backend->model_hash = context->model_hash;
    return REACHY_SIM_STATUS_OK;
}

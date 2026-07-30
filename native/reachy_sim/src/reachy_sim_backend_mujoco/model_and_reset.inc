static ReachySimStatus validate_finite_state(
    const ReachyMujocoBackendContext* context,
    char* error,
    size_t error_size)
{
    const mjModel* const model = context->model;
    const mjData* const data = context->data;
    if(!isfinite((double)data->time) || data->time < 0.0)
    {
        write_message(error, error_size, "MuJoCo simulation time is invalid");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->qpos, (size_t)model->nq))
    {
        write_message(error, error_size, "MuJoCo qpos contains a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->qvel, (size_t)model->nv))
    {
        write_message(error, error_size, "MuJoCo qvel contains a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->qacc, (size_t)model->nv))
    {
        write_message(error, error_size, "MuJoCo qacc contains a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->act, (size_t)model->na))
    {
        write_message(error, error_size, "MuJoCo actuator state contains a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->ctrl, (size_t)model->nu))
    {
        write_message(error, error_size, "MuJoCo controls contain a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->xpos, (size_t)model->nbody * 3U))
    {
        write_message(error, error_size, "MuJoCo body positions contain a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    if(!finite_values(data->xquat, (size_t)model->nbody * 4U))
    {
        write_message(error, error_size, "MuJoCo body orientations contain a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachyMujocoModelFormat detect_model_format(
    const uint8_t* model_bytes,
    size_t model_size,
    uint32_t flags,
    ReachySimStatus* status,
    char* error,
    size_t error_size)
{
    *status = REACHY_SIM_STATUS_OK;
    if((flags & (uint32_t)(~REACHY_MUJOCO_KNOWN_CONFIG_FLAGS)) != 0U)
    {
        write_message(error, error_size, "MuJoCo backend received unknown config flags");
        *status = REACHY_SIM_STATUS_INVALID_ARGUMENT;
        return REACHY_MUJOCO_MODEL_FORMAT_XML;
    }
    if((flags & REACHY_MUJOCO_KNOWN_CONFIG_FLAGS) == REACHY_MUJOCO_KNOWN_CONFIG_FLAGS)
    {
        write_message(error, error_size, "model format flags are mutually exclusive");
        *status = REACHY_SIM_STATUS_INVALID_ARGUMENT;
        return REACHY_MUJOCO_MODEL_FORMAT_XML;
    }
    if((flags & REACHY_SIM_CONFIG_FLAG_MODEL_XML) != 0U)
    {
        return REACHY_MUJOCO_MODEL_FORMAT_XML;
    }
    if((flags & REACHY_SIM_CONFIG_FLAG_MODEL_MJB) != 0U)
    {
        return REACHY_MUJOCO_MODEL_FORMAT_MJB;
    }

    size_t index = 0U;
    while(index < model_size)
    {
        const uint8_t value = model_bytes[index];
        if(value != (uint8_t)' ' && value != (uint8_t)'\t' &&
           value != (uint8_t)'\r' && value != (uint8_t)'\n')
        {
            return value == (uint8_t)'<'
                ? REACHY_MUJOCO_MODEL_FORMAT_XML
                : REACHY_MUJOCO_MODEL_FORMAT_MJB;
        }
        ++index;
    }

    write_message(error, error_size, "model buffer contains only whitespace");
    *status = REACHY_SIM_STATUS_MODEL_EMPTY;
    return REACHY_MUJOCO_MODEL_FORMAT_XML;
}

static mjModel* load_xml_model(
    const uint8_t* model_bytes,
    size_t model_size,
    char* error,
    size_t error_size)
{
    if(model_size > (size_t)INT_MAX)
    {
        write_message(error, error_size, "XML model buffer exceeds MuJoCo's integer size limit");
        return NULL;
    }

    mjVFS vfs;
    mj_defaultVFS(&vfs);
    if(mj_addBufferVFS(
           &vfs,
           "reachy_sim_model.xml",
           model_bytes,
           (int)model_size) != 0)
    {
        mj_deleteVFS(&vfs);
        write_message(error, error_size, "MuJoCo could not add XML model to its virtual filesystem");
        return NULL;
    }

    char mujoco_error[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    mjModel* const model = mj_loadXML(
        "reachy_sim_model.xml",
        &vfs,
        mujoco_error,
        (int)sizeof(mujoco_error));
    mj_deleteVFS(&vfs);
    if(model == NULL)
    {
        if(mujoco_error[0] == '\0')
        {
            write_message(error, error_size, "MuJoCo rejected the XML model buffer");
        }
        else
        {
            write_message(error, error_size, mujoco_error);
        }
    }
    return model;
}

static mjModel* load_mjb_model(
    const uint8_t* model_bytes,
    size_t model_size,
    char* error,
    size_t error_size)
{
    if(model_size > (size_t)INT_MAX)
    {
        write_message(error, error_size, "MJB model buffer exceeds MuJoCo's integer size limit");
        return NULL;
    }
    mjModel* const model = mj_loadModelBuffer(model_bytes, (int)model_size);
    if(model == NULL)
    {
        write_message(error, error_size, "MuJoCo rejected the binary MJB model buffer");
    }
    return model;
}

static int find_sleep_keyframe(const mjModel* model)
{
    static const char* const NAMES[] = {"sleep_rest", "sleep", "rest"};
    for(size_t index = 0U; index < sizeof(NAMES) / sizeof(NAMES[0]); ++index)
    {
        const int keyframe = mj_name2id(model, mjOBJ_KEY, NAMES[index]);
        if(keyframe >= 0)
        {
            return keyframe;
        }
    }
    return -1;
}

static ReachySimStatus reset_context(
    ReachyMujocoBackendContext* context,
    uint32_t reset_id,
    char* error,
    size_t error_size)
{
    if(!valid_reset_pose(reset_id))
    {
        write_message(error, error_size, "unknown reset pose identifier");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    if(reset_id == (uint32_t)REACHY_SIM_RESET_POSE_SLEEP_REST)
    {
        const int keyframe = find_sleep_keyframe(context->model);
        if(keyframe < 0)
        {
            write_message(
                error,
                error_size,
                "model does not define a sleep/rest keyframe; no synthetic sleep pose is permitted");
            return REACHY_SIM_STATUS_UNSUPPORTED;
        }
        mj_resetDataKeyframe(context->model, context->data, keyframe);
    }
    else
    {
        mj_resetData(context->model, context->data);
    }

    mj_forward(context->model, context->data);
    context->sequence = 0U;
    context->last_command_sequence = 0U;
    context->reset_pose_id = reset_id;
    context->health_flags = reset_health_flags(reset_id);
    clear_pending_wrench(context);
    if(current_state_has_warning(context))
    {
        context->health_flags |=
            (uint32_t)REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING;
        capture_warning_counts(context);
        write_message(error, error_size, "MuJoCo raised a warning while resetting the model");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }
    capture_warning_counts(context);
    return validate_finite_state(context, error, error_size);
}

static void mujoco_destroy(void* opaque_context)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    if(context == NULL)
    {
        return;
    }
    free(context->restore_state);
    free(context->integration_state);
    mj_deleteData(context->data);
    mj_deleteModel(context->model);
    free(context);
}

static ReachySimStatus mujoco_reset(
    void* opaque_context,
    uint32_t reset_id,
    char* error,
    size_t error_size)
{
    ReachyMujocoBackendContext* const context = opaque_context;
    return reset_context(context, reset_id, error, error_size);
}

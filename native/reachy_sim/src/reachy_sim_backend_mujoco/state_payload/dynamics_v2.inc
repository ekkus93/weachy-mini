typedef struct ReachyMujocoDynamicsLayout {
    size_t total_size;
    size_t qpos_offset;
    size_t qvel_offset;
    size_t actuator_offset;
    size_t body_offset;
    size_t contact_offset;
    size_t hard_stop_offset;
    uint32_t contact_count;
    uint32_t hard_stop_count;
} ReachyMujocoDynamicsLayout;

typedef struct ReachyMujocoDynamicsMetrics {
    uint32_t contact_overload_count;
    uint32_t hard_stop_event_count;
    double maximum_contact_penetration_metres;
    double maximum_contact_normal_force_newtons;
    double maximum_contact_tangent_force_newtons;
    double maximum_contact_impulse_newton_seconds;
    double maximum_hard_stop_force;
} ReachyMujocoDynamicsMetrics;

#ifndef REACHY_FAKE_MUJOCO
static double rma065_custom_numeric(
    const ReachyMujocoBackendContext* context,
    const char* name,
    double fallback)
{
#ifdef REACHY_FAKE_MUJOCO
    (void)context;
    (void)name;
    return fallback;
#else
    const int numeric_id = mj_name2id(context->model, mjOBJ_NUMERIC, name);
    if(numeric_id < 0 || context->model->numeric_size[numeric_id] < 1)
    {
        return fallback;
    }
    const int address = context->model->numeric_adr[numeric_id];
    if(address < 0)
    {
        return fallback;
    }
    const double value = (double)context->model->numeric_data[address];
    return isfinite(value) && value > 0.0 ? value : fallback;
#endif
}

#endif

static uint32_t rma065_hard_stop_count(const ReachyMujocoBackendContext* context)
{
#ifdef REACHY_FAKE_MUJOCO
    (void)context;
    return 0U;
#else
    uint32_t count = 0U;
    for(int joint_id = 0; joint_id < context->model->njnt; ++joint_id)
    {
        const int joint_type = context->model->jnt_type[joint_id];
        if(context->model->jnt_limited[joint_id] != 0 &&
           (joint_type == mjJNT_HINGE || joint_type == mjJNT_SLIDE))
        {
            ++count;
        }
    }
    return count;
#endif
}

static uint32_t rma065_contact_count(const ReachyMujocoBackendContext* context)
{
#ifdef REACHY_FAKE_MUJOCO
    (void)context;
    return 0U;
#else
    return context->data->ncon >= 0 ? (uint32_t)context->data->ncon : 0U;
#endif
}

static bool calculate_dynamics_layout(
    const ReachyMujocoBackendContext* context,
    ReachyMujocoDynamicsLayout* layout)
{
    size_t qpos_bytes = 0U;
    size_t qvel_bytes = 0U;
    size_t actuator_bytes = 0U;
    size_t body_bytes = 0U;
    size_t contact_bytes = 0U;
    size_t hard_stop_bytes = 0U;
    size_t offset = 0U;
    if(context == NULL || layout == NULL || context->model->nbody < 1)
    {
        return false;
    }
    layout->contact_count = rma065_contact_count(context);
    layout->hard_stop_count = rma065_hard_stop_count(context);
    if(!checked_multiply_size((size_t)context->model->nq, sizeof(double), &qpos_bytes) ||
       !checked_multiply_size((size_t)context->model->nv, sizeof(double), &qvel_bytes) ||
       !checked_multiply_size(
           (size_t)context->model->nu,
           sizeof(ReachySimActuatorObservation),
           &actuator_bytes) ||
       !checked_multiply_size(
           (size_t)(context->model->nbody - 1),
           sizeof(ReachySimBodyPose),
           &body_bytes) ||
       !checked_multiply_size(
           (size_t)layout->contact_count,
           sizeof(ReachySimContactObservation),
           &contact_bytes) ||
       !checked_multiply_size(
           (size_t)layout->hard_stop_count,
           sizeof(ReachySimHardStopObservation),
           &hard_stop_bytes) ||
       !checked_add_size(
           sizeof(ReachySimStateHeader),
           sizeof(ReachySimDynamicsStatePayloadHeader),
           &offset))
    {
        return false;
    }
    layout->qpos_offset = offset;
    if(!checked_add_size(offset, qpos_bytes, &offset))
    {
        return false;
    }
    layout->qvel_offset = offset;
    if(!checked_add_size(offset, qvel_bytes, &offset))
    {
        return false;
    }
    layout->actuator_offset = offset;
    if(!checked_add_size(offset, actuator_bytes, &offset))
    {
        return false;
    }
    layout->body_offset = offset;
    if(!checked_add_size(offset, body_bytes, &offset))
    {
        return false;
    }
    layout->contact_offset = offset;
    if(!checked_add_size(offset, contact_bytes, &offset))
    {
        return false;
    }
    layout->hard_stop_offset = offset;
    return checked_add_size(offset, hard_stop_bytes, &layout->total_size);
}

#ifndef REACHY_FAKE_MUJOCO
static uint32_t rma065_joint_actuator_id(const mjModel* model, int joint_id)
{
    for(int actuator_id = 0; actuator_id < model->nu; ++actuator_id)
    {
        if(model->actuator_trntype[actuator_id] == mjTRN_JOINT &&
           model->actuator_trnid[2 * actuator_id] == joint_id)
        {
            return (uint32_t)actuator_id;
        }
    }
    return REACHY_SIM_INVALID_OBJECT_ID;
}

static double rma065_joint_limit_force(
    const mjData* data,
    int joint_id,
    uint32_t* active_flags)
{
    double maximum_force = 0.0;
    for(int row = 0; row < data->nefc; ++row)
    {
        if(data->efc_type[row] != mjCNSTR_LIMIT_JOINT ||
           data->efc_id[row] != joint_id)
        {
            continue;
        }
        const double force = fabs((double)data->efc_force[row]);
        if(force > maximum_force)
        {
            maximum_force = force;
        }
        const double residual = (double)data->efc_pos[row];
        if(residual < 0.0)
        {
            *active_flags |= REACHY_SIM_HARD_STOP_FLAG_LOWER_ACTIVE;
        }
        else
        {
            *active_flags |= REACHY_SIM_HARD_STOP_FLAG_UPPER_ACTIVE;
        }
    }
    return maximum_force;
}
#endif

static ReachySimStatus write_contact_observations(
    ReachyMujocoBackendContext* context,
    uint8_t* output,
    size_t offset,
    ReachyMujocoDynamicsMetrics* metrics,
    char* error,
    size_t error_size)
{
#ifdef REACHY_FAKE_MUJOCO
    (void)context;
    (void)output;
    (void)offset;
    (void)metrics;
    (void)error;
    (void)error_size;
    return REACHY_SIM_STATUS_OK;
#else
    const double force_threshold = rma065_custom_numeric(
        context,
        "rma065_contact_overload_newtons",
        HUGE_VAL);
    const double impulse_threshold = rma065_custom_numeric(
        context,
        "rma065_contact_overload_impulse_newton_seconds",
        HUGE_VAL);
    for(uint32_t index = 0U; index < (uint32_t)context->data->ncon; ++index)
    {
        const mjContact* const contact = &context->data->contact[index];
        if(contact->geom[0] < 0 || contact->geom[0] >= context->model->ngeom ||
           contact->geom[1] < 0 || contact->geom[1] >= context->model->ngeom)
        {
            write_message(error, error_size, "MuJoCo contact references an invalid geom");
            return REACHY_SIM_STATUS_BACKEND_ERROR;
        }
        mjtNum force[6] = {0};
        mj_contactForce(context->model, context->data, (int)index, force);
        const double normal_force = fabs((double)force[0]);
        const double tangent_force = hypot((double)force[1], (double)force[2]);
        const double penetration = fmax(0.0, -(double)contact->dist);
        const double impulse = normal_force * context->config.timestep_seconds;
        if(!isfinite(normal_force) || !isfinite(tangent_force) ||
           !isfinite(penetration) || !isfinite(impulse))
        {
            write_message(error, error_size, "MuJoCo contact diagnostics are non-finite");
            return REACHY_SIM_STATUS_NUMERIC_FAILURE;
        }
        const uint32_t body1 = (uint32_t)context->model->geom_bodyid[contact->geom[0]];
        const uint32_t body2 = (uint32_t)context->model->geom_bodyid[contact->geom[1]];
        uint32_t flags = body1 == 0U || body2 == 0U
            ? (uint32_t)REACHY_SIM_CONTACT_FLAG_EXTERNAL
            : (uint32_t)REACHY_SIM_CONTACT_FLAG_INTERNAL;
        if(normal_force > force_threshold || impulse > impulse_threshold)
        {
            flags |= (uint32_t)REACHY_SIM_CONTACT_FLAG_OVERLOAD;
            ++metrics->contact_overload_count;
            context->health_flags |= (uint32_t)REACHY_SIM_HEALTH_FLAG_CONTACT_OVERLOAD;
        }
        const ReachySimContactObservation observation = {
            index,
            (uint32_t)contact->geom[0],
            (uint32_t)contact->geom[1],
            body1,
            body2,
            flags,
            {
                (double)contact->pos[0],
                (double)contact->pos[1],
                (double)contact->pos[2]
            },
            {
                (double)contact->frame[0],
                (double)contact->frame[1],
                (double)contact->frame[2]
            },
            penetration,
            normal_force,
            tangent_force,
            impulse};
        memcpy(
            output + offset + (size_t)index * sizeof(observation),
            &observation,
            sizeof(observation));
        metrics->maximum_contact_penetration_metres = fmax(
            metrics->maximum_contact_penetration_metres,
            penetration);
        metrics->maximum_contact_normal_force_newtons = fmax(
            metrics->maximum_contact_normal_force_newtons,
            normal_force);
        metrics->maximum_contact_tangent_force_newtons = fmax(
            metrics->maximum_contact_tangent_force_newtons,
            tangent_force);
        metrics->maximum_contact_impulse_newton_seconds = fmax(
            metrics->maximum_contact_impulse_newton_seconds,
            impulse);
    }
    return REACHY_SIM_STATUS_OK;
#endif
}

static ReachySimStatus write_hard_stop_observations(
    ReachyMujocoBackendContext* context,
    uint8_t* output,
    size_t offset,
    ReachyMujocoDynamicsMetrics* metrics,
    char* error,
    size_t error_size)
{
#ifdef REACHY_FAKE_MUJOCO
    (void)context;
    (void)output;
    (void)offset;
    (void)metrics;
    (void)error;
    (void)error_size;
    return REACHY_SIM_STATUS_OK;
#else
    const double overload_threshold = rma065_custom_numeric(
        context,
        "rma065_hard_stop_overload_newton_metres",
        HUGE_VAL);
    uint32_t output_index = 0U;
    for(int joint_id = 0; joint_id < context->model->njnt; ++joint_id)
    {
        const int joint_type = context->model->jnt_type[joint_id];
        if(context->model->jnt_limited[joint_id] == 0 ||
           (joint_type != mjJNT_HINGE && joint_type != mjJNT_SLIDE))
        {
            continue;
        }
        const int qpos_address = context->model->jnt_qposadr[joint_id];
        const double position = (double)context->data->qpos[qpos_address];
        const double lower = (double)context->model->jnt_range[2 * joint_id];
        const double upper = (double)context->model->jnt_range[2 * joint_id + 1];
        const double lower_distance = position - lower;
        const double upper_distance = upper - position;
        const bool lower_is_nearest = lower_distance <= upper_distance;
        const double signed_distance = lower_is_nearest
            ? lower_distance
            : upper_distance;
        uint32_t flags = 0U;
        const double limit_force = rma065_joint_limit_force(
            context->data,
            joint_id,
            &flags);
        if(position < lower || position > upper)
        {
            flags |= (uint32_t)REACHY_SIM_HARD_STOP_FLAG_POSITION_VIOLATION;
        }
        if(flags != 0U)
        {
            ++metrics->hard_stop_event_count;
            context->health_flags |= (uint32_t)REACHY_SIM_HEALTH_FLAG_HARD_STOP;
        }
        if(limit_force > overload_threshold)
        {
            flags |= (uint32_t)REACHY_SIM_HARD_STOP_FLAG_OVERLOAD;
            context->health_flags |= (uint32_t)REACHY_SIM_HEALTH_FLAG_HARD_STOP;
        }
        const double impulse = limit_force * context->config.timestep_seconds;
        if(!isfinite(position) || !isfinite(lower) || !isfinite(upper) ||
           !isfinite(signed_distance) || !isfinite(limit_force) || !isfinite(impulse))
        {
            write_message(error, error_size, "MuJoCo hard-stop diagnostics are non-finite");
            return REACHY_SIM_STATUS_NUMERIC_FAILURE;
        }
        const ReachySimHardStopObservation observation = {
            (uint32_t)joint_id,
            rma065_joint_actuator_id(context->model, joint_id),
            flags,
            0U,
            position,
            lower,
            upper,
            signed_distance,
            limit_force,
            impulse};
        memcpy(
            output + offset + (size_t)output_index * sizeof(observation),
            &observation,
            sizeof(observation));
        metrics->maximum_hard_stop_force = fmax(
            metrics->maximum_hard_stop_force,
            limit_force);
        ++output_index;
    }
    return REACHY_SIM_STATUS_OK;
#endif
}

static ReachySimStatus mujoco_copy_dynamics_state(
    ReachyMujocoBackendContext* context,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size,
    char* error,
    size_t error_size)
{
    ReachyMujocoDynamicsLayout layout = {0};
    if(!calculate_dynamics_layout(context, &layout))
    {
        write_message(error, error_size, "dynamics state payload size overflow");
        return REACHY_SIM_STATUS_RESOURCE_EXHAUSTED;
    }
    *required_size = layout.total_size;
    if(byte_capacity < layout.total_size)
    {
        write_message(error, error_size, "dynamics state buffer is too small");
        return REACHY_SIM_STATUS_BUFFER_TOO_SMALL;
    }

    ReachySimStatus status = validate_finite_state(context, error, error_size);
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

    uint8_t* const output = bytes;
    ReachyMujocoDynamicsMetrics metrics = {0};
    status = write_contact_observations(
        context,
        output,
        layout.contact_offset,
        &metrics,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }
    status = write_hard_stop_observations(
        context,
        output,
        layout.hard_stop_offset,
        &metrics,
        error,
        error_size);
    if(status != REACHY_SIM_STATUS_OK)
    {
        return status;
    }

    size_t legacy_required_size = 0U;
    status = mujoco_copy_state_header_only(
        context,
        output,
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

    const ReachySimDynamicsStatePayloadHeader payload = {
        REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION,
        (uint32_t)sizeof(ReachySimDynamicsStatePayloadHeader),
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
        layout.contact_count,
        layout.hard_stop_count,
        metrics.contact_overload_count,
        metrics.hard_stop_event_count,
        (uint64_t)layout.qpos_offset,
        (uint64_t)layout.qvel_offset,
        (uint64_t)layout.actuator_offset,
        (uint64_t)layout.body_offset,
        (uint64_t)layout.contact_offset,
        (uint64_t)layout.hard_stop_offset,
        context->calibration_profile_id,
        warning_count,
        constraint_count,
        equality_constraint_count,
        maximum_constraint_residual,
        maximum_equality_constraint_residual,
        metrics.maximum_contact_penetration_metres,
        metrics.maximum_contact_normal_force_newtons,
        metrics.maximum_contact_tangent_force_newtons,
        metrics.maximum_contact_impulse_newton_seconds,
        metrics.maximum_hard_stop_force};
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

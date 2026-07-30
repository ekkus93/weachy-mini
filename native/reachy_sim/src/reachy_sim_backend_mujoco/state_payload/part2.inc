static ReachySimStatus calculate_constraint_diagnostics(
    const ReachyMujocoBackendContext* context,
    uint32_t* constraint_count,
    uint32_t* equality_constraint_count,
    double* maximum_constraint_residual,
    double* maximum_equality_constraint_residual,
    char* error,
    size_t error_size)
{
    const mjData* const data = context->data;
    if(!dimension_fits_uint32(data->nefc) ||
       (data->nefc > 0 && (data->efc_pos == NULL || data->efc_type == NULL)))
    {
        write_message(error, error_size, "MuJoCo constraint metadata is not representable");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }

    uint32_t equality_count = 0U;
    double maximum = 0.0;
    double equality_maximum = 0.0;
    for(mjtSize index = 0; index < data->nefc; ++index)
    {
        const double residual = fabs((double)data->efc_pos[index]);
        if(!isfinite(residual))
        {
            write_message(error, error_size, "MuJoCo constraint residual is non-finite");
            return REACHY_SIM_STATUS_NUMERIC_FAILURE;
        }
        if(residual > maximum)
        {
            maximum = residual;
        }
        if(data->efc_type[index] == mjCNSTR_EQUALITY)
        {
            ++equality_count;
            if(residual > equality_maximum)
            {
                equality_maximum = residual;
            }
        }
    }

    *constraint_count = (uint32_t)data->nefc;
    *equality_constraint_count = equality_count;
    *maximum_constraint_residual = maximum;
    *maximum_equality_constraint_residual = equality_maximum;
    return REACHY_SIM_STATUS_OK;
}

static bool total_warning_count(
    const ReachyMujocoBackendContext* context,
    uint64_t* warning_count)
{
    uint64_t total = 0U;
    for(int index = 0; index < mjNWARNING; ++index)
    {
        const int count = context->data->warning[index].number;
        if(count < 0 || UINT64_MAX - total < (uint64_t)count)
        {
            return false;
        }
        total += (uint64_t)count;
    }
    *warning_count = total;
    return true;
}

static void copy_numeric_array(
    uint8_t* output,
    size_t offset,
    const mjtNum* values,
    size_t count)
{
    for(size_t index = 0U; index < count; ++index)
    {
        const double value = (double)values[index];
        memcpy(output + offset + index * sizeof(value), &value, sizeof(value));
    }
}

static ReachySimStatus write_actuator_observations(
    const ReachyMujocoBackendContext* context,
    uint8_t* output,
    size_t offset,
    char* error,
    size_t error_size)
{
    const mjModel* const model = context->model;
    const mjData* const data = context->data;
#ifdef REACHY_FAKE_MUJOCO
    if(!finite_values(data->ctrl, (size_t)model->nu) ||
       !finite_values(data->qpos, (size_t)model->nu) ||
       !finite_values(data->qvel, (size_t)model->nu))
#else
    if(!finite_values(data->ctrl, (size_t)model->nu) ||
       !finite_values(data->actuator_force, (size_t)model->nout) ||
       !finite_values(data->actuator_length, (size_t)model->nout) ||
       !finite_values(data->actuator_velocity, (size_t)model->nout))
#endif
    {
        write_message(error, error_size, "MuJoCo actuator observations contain a non-finite value");
        return REACHY_SIM_STATUS_NUMERIC_FAILURE;
    }

    for(uint32_t index = 0U; index < (uint32_t)model->nu; ++index)
    {
#ifdef REACHY_FAKE_MUJOCO
        const ReachySimActuatorObservation observation = {
            index,
            0U,
            (double)data->ctrl[index],
            (double)data->ctrl[index],
            (double)data->qpos[index],
            (double)data->qvel[index]};
#else
        const size_t output_index = (size_t)model->actuator_outadr[index];
        const ReachySimActuatorObservation observation = {
            index,
            0U,
            (double)data->ctrl[index],
            (double)data->actuator_force[output_index],
            (double)data->actuator_length[output_index],
            (double)data->actuator_velocity[output_index]};
#endif
        memcpy(
            output + offset + (size_t)index * sizeof(observation),
            &observation,
            sizeof(observation));
    }
    return REACHY_SIM_STATUS_OK;
}


static bool dimensions_fit_public_state(const mjModel* model)
{
    return model != NULL &&
        model->nbody >= 1 &&
        dimension_fits_uint32(model->nbody) &&
        dimension_fits_uint32(model->njnt) &&
        dimension_fits_uint32(model->nu) &&
#ifndef REACHY_FAKE_MUJOCO
        dimension_fits_uint32(model->nactuator) &&
        dimension_fits_uint32(model->nout) &&
#endif
        dimension_fits_int(model->nq) &&
        dimension_fits_int(model->nv) &&
        dimension_fits_int(model->na) &&
        dimension_fits_int(model->neq) &&
        dimension_fits_int(model->nkey) &&
        dimension_fits_int(model->nmocap) &&
        dimension_fits_int(model->nuserdata);
}


static bool actuator_observation_mapping_supported(const mjModel* model)
{
    if(model == NULL || model->nu == 0)
    {
        return model != NULL;
    }
#ifdef REACHY_FAKE_MUJOCO
    return true;
#else
    if(model->nactuator != model->nu || model->nout != model->nactuator ||
       model->actuator_ctrladr == NULL || model->actuator_ctrlnum == NULL ||
       model->actuator_outadr == NULL || model->actuator_outnum == NULL)
    {
        return false;
    }
    for(mjtSize index = 0; index < model->nactuator; ++index)
    {
        const int control_address = model->actuator_ctrladr[index];
        const int output_address = model->actuator_outadr[index];
        if(model->actuator_ctrlnum[index] != 1 ||
           model->actuator_outnum[index] != 1 ||
           control_address != (int)index ||
           output_address < 0 ||
           (mjtSize)output_address >= model->nout)
        {
            return false;
        }
        for(mjtSize previous = 0; previous < index; ++previous)
        {
            if(model->actuator_outadr[previous] == output_address)
            {
                return false;
            }
        }
    }
    return true;
#endif
}

static void reachy_mujoco_zero(mjtNum* values, mjtSize count)
{
    mju_zero(values, (int)count);
}

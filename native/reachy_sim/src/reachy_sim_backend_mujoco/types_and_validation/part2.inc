static bool dimensions_fit_public_state(const mjModel* model)
{
    return model != NULL &&
        model->nbody >= 1 &&
        dimension_fits_uint32(model->nbody) &&
        dimension_fits_uint32(model->njnt) &&
        dimension_fits_uint32(model->nu) &&
        dimension_fits_int(model->nq) &&
        dimension_fits_int(model->nv) &&
        dimension_fits_int(model->na) &&
        dimension_fits_int(model->neq) &&
        dimension_fits_int(model->nkey) &&
        dimension_fits_int(model->nmocap) &&
        dimension_fits_int(model->nuserdata);
}

static void reachy_mujoco_zero(mjtNum* values, mjtSize count)
{
    mju_zero(values, (int)count);
}

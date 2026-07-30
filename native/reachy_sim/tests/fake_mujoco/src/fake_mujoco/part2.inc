    }
    FILE* const stream = fopen(filename, "rb");
    if(stream == NULL)
    {
        return NULL;
    }
    unsigned char content[4] = {0};
    const size_t read_count = fread(content, 1U, sizeof(content), stream);
    const int close_status = fclose(stream);
    if(read_count != sizeof(content) || close_status != 0)
    {
        return NULL;
    }
    return mj_loadModelBuffer(content, (int)sizeof(content));
}

static int state_size(const mjModel* model)
{
    return 1 +
        model->nq +
        model->nv +
        model->na +
        model->nu +
        model->nv +
        model->nv +
        6 * model->nbody +
        model->neq +
        3 * model->nmocap +
        4 * model->nmocap +
        model->nuserdata;
}

mjData* mj_makeData(const mjModel* model)
{
    if(model == NULL)
    {
        return NULL;
    }
    mjData* const data = calloc(1U, sizeof(*data));
    if(data == NULL)
    {
        return NULL;
    }
    data->qpos = allocate_values((size_t)model->nq);
    data->qvel = allocate_values((size_t)model->nv);
    data->qacc = allocate_values((size_t)model->nv);
    data->act = allocate_values((size_t)model->na);
    data->ctrl = allocate_values((size_t)model->nu);
    data->qacc_warmstart = allocate_values((size_t)model->nv);
    data->qfrc_applied = allocate_values((size_t)model->nv);
    data->xfrc_applied = allocate_values((size_t)model->nbody * 6U);
    data->eq_active = allocate_bytes((size_t)model->neq);
    data->mocap_pos = allocate_values((size_t)model->nmocap * 3U);
    data->mocap_quat = allocate_values((size_t)model->nmocap * 4U);
    data->userdata = allocate_values((size_t)model->nuserdata);
    data->nefc = (mjtSize)model->neq + 1;
    data->efc_pos = allocate_values((size_t)data->nefc);
    data->efc_type = calloc((size_t)data->nefc, sizeof(*data->efc_type));
    data->xpos = allocate_values((size_t)model->nbody * 3U);
    data->xquat = allocate_values((size_t)model->nbody * 4U);
    if(data->qpos == NULL || data->qvel == NULL || data->qacc == NULL ||
       (model->na > 0 && data->act == NULL) ||
       (model->nu > 0 && data->ctrl == NULL) ||
       data->qacc_warmstart == NULL || data->qfrc_applied == NULL ||
       data->xfrc_applied == NULL || data->eq_active == NULL ||
       (model->nuserdata > 0 && data->userdata == NULL) ||
       data->efc_pos == NULL || data->efc_type == NULL ||
       data->xpos == NULL || data->xquat == NULL)
    {
        mj_deleteData(data);
        return NULL;
    }
    mj_resetData(model, data);
    return data;
}

void mj_resetData(const mjModel* model, mjData* data)
{
    data->time = 0.0;
    data->ncon = 0;
    mju_zero(data->qpos, model->nq);
    mju_zero(data->qvel, model->nv);
    mju_zero(data->qacc, model->nv);
    mju_zero(data->act, model->na);
    mju_zero(data->ctrl, model->nu);
    mju_zero(data->qacc_warmstart, model->nv);
    mju_zero(data->qfrc_applied, model->nv);
    mju_zero(data->xfrc_applied, 6 * model->nbody);
    memset(data->eq_active, 1, (size_t)model->neq);
    mju_zero(data->userdata, model->nuserdata);
    mju_zero(data->efc_pos, (int)data->nefc);
    memset(data->efc_type, 0, (size_t)data->nefc * sizeof(*data->efc_type));
    if(data->nefc > 0)
    {
        data->efc_type[0] = mjCNSTR_EQUALITY;
    }
    if(data->nefc > 1)
    {
        data->efc_type[1] = mjCNSTR_CONTACT_FRICTIONLESS;
    }
    memset(data->warning, 0, sizeof(data->warning));
    data->fake_steps = 0;
    data->fake_emit_warning = 0;
    mj_forward(model, data);
}

void mj_resetDataKeyframe(const mjModel* model, mjData* data, int key)
{
    mj_resetData(model, data);
    if(key >= 0 && key < model->nkey)
    {
        data->qpos[0] = -0.5;
        data->qpos[1] = 0.25;
        data->ctrl[0] = -0.5;
        mj_forward(model, data);
    }
}

void mj_forward(const mjModel* model, mjData* data)
{
    for(int body = 0; body < model->nbody; ++body)
    {
        data->xpos[3 * body] = body == 0 ? 0.0 : data->qpos[0] + 0.1 * body;
        data->xpos[3 * body + 1] = body == 0 ? 0.0 : data->qpos[1];
        data->xpos[3 * body + 2] = body == 0 ? 0.0 : 0.2 * body;
        data->xquat[4 * body] = 1.0;
        data->xquat[4 * body + 1] = 0.0;
        data->xquat[4 * body + 2] = 0.0;
        data->xquat[4 * body + 3] = 0.0;
    }
}

void mj_step(const mjModel* model, mjData* data)
{
    for(int index = 0; index < model->nv; ++index)
    {
        const mjtNum control = index < model->nu ? data->ctrl[index] : 0.0;
        data->qacc[index] = control + data->qfrc_applied[index];
        data->qvel[index] += data->qacc[index] * model->opt.timestep;
        data->qpos[index] += data->qvel[index] * model->opt.timestep;
        data->qacc_warmstart[index] = data->qacc[index] * 0.5;
    }
    data->time += model->opt.timestep;
    data->ncon = data->qpos[0] > 0.25 ? 1 : 0;
    if(data->nefc > 0)
    {
        data->efc_pos[0] = 0.0000001;
    }
    if(data->nefc > 1)
    {
        data->efc_pos[1] = -0.05;
    }
    ++data->fake_steps;
    if(model->nu > 2 && data->ctrl[2] == 99.0)
    {
        data->warning[2].lastinfo = 77;
        ++data->warning[2].number;
    }
    mj_forward(model, data);
}

void mj_deleteData(mjData* data)
{
    if(data == NULL)
    {
        return;
    }
    free(data->qpos);
    free(data->qvel);
    free(data->qacc);

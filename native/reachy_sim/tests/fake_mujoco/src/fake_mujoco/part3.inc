    free(data->act);
    free(data->ctrl);
    free(data->qacc_warmstart);
    free(data->qfrc_applied);
    free(data->xfrc_applied);
    free(data->eq_active);
    free(data->mocap_pos);
    free(data->mocap_quat);
    free(data->userdata);
    free(data->efc_pos);
    free(data->efc_type);
    free(data->xpos);
    free(data->xquat);
    free(data);
}

void mj_deleteModel(mjModel* model)
{
    if(model == NULL)
    {
        return;
    }
    free(model->actuator_ctrllimited);
    free(model->actuator_ctrlrange);
    free(model);
}

int mj_name2id(const mjModel* model, int type, const char* name)
{
    if(model != NULL && type == mjOBJ_KEY && model->nkey > 0 && name != NULL &&
       (strcmp(name, "sleep_rest") == 0 || strcmp(name, "sleep") == 0 ||
        strcmp(name, "rest") == 0))
    {
        return 0;
    }
    return -1;
}

int mj_stateSize(const mjModel* model, int sig)
{
    return model != NULL && sig == mjSTATE_INTEGRATION ? state_size(model) : 0;
}

static void copy_out(mjtNum* state, int* offset, const mjtNum* values, int count)
{
    if(count > 0)
    {
        memcpy(state + *offset, values, (size_t)count * sizeof(*values));
        *offset += count;
    }
}

static void copy_in(mjtNum* values, int* offset, const mjtNum* state, int count)
{
    if(count > 0)
    {
        memcpy(values, state + *offset, (size_t)count * sizeof(*values));
        *offset += count;
    }
}

void mj_getState(const mjModel* model, const mjData* data, mjtNum* state, int sig)
{
    if(sig != mjSTATE_INTEGRATION)
    {
        return;
    }
    int offset = 0;
    state[offset++] = data->time;
    copy_out(state, &offset, data->qpos, model->nq);
    copy_out(state, &offset, data->qvel, model->nv);
    copy_out(state, &offset, data->act, model->na);
    copy_out(state, &offset, data->ctrl, model->nu);
    copy_out(state, &offset, data->qacc_warmstart, model->nv);
    copy_out(state, &offset, data->qfrc_applied, model->nv);
    copy_out(state, &offset, data->xfrc_applied, 6 * model->nbody);
    for(int index = 0; index < model->neq; ++index)
    {
        state[offset++] = (mjtNum)data->eq_active[index];
    }
    copy_out(state, &offset, data->mocap_pos, 3 * model->nmocap);
    copy_out(state, &offset, data->mocap_quat, 4 * model->nmocap);
    copy_out(state, &offset, data->userdata, model->nuserdata);
}

void mj_setState(const mjModel* model, mjData* data, const mjtNum* state, int sig)
{
    if(sig != mjSTATE_INTEGRATION)
    {
        return;
    }
    int offset = 0;
    data->time = state[offset++];
    copy_in(data->qpos, &offset, state, model->nq);
    copy_in(data->qvel, &offset, state, model->nv);
    copy_in(data->act, &offset, state, model->na);
    copy_in(data->ctrl, &offset, state, model->nu);
    copy_in(data->qacc_warmstart, &offset, state, model->nv);
    copy_in(data->qfrc_applied, &offset, state, model->nv);
    copy_in(data->xfrc_applied, &offset, state, 6 * model->nbody);
    for(int index = 0; index < model->neq; ++index)
    {
        data->eq_active[index] = state[offset++] != 0.0 ? 1U : 0U;
    }
    copy_in(data->mocap_pos, &offset, state, 3 * model->nmocap);
    copy_in(data->mocap_quat, &offset, state, 4 * model->nmocap);
    copy_in(data->userdata, &offset, state, model->nuserdata);
}

void mj_applyFT(
    const mjModel* model,
    mjData* data,
    const mjtNum force[3],
    const mjtNum torque[3],
    const mjtNum point[3],
    int body,
    mjtNum* qfrc_target)
{
    (void)data;
    (void)point;
    if(model == NULL || body <= 0 || body >= model->nbody || qfrc_target == NULL)
    {
        return;
    }
    if(model->nv > 0)
    {
        qfrc_target[0] += force == NULL ? 0.0 : force[0];
    }
    if(model->nv > 1)
    {
        qfrc_target[1] += torque == NULL ? 0.0 : torque[1];
    }
}

void mju_zero(mjtNum* values, int count)
{
    if(values != NULL && count > 0)
    {
        memset(values, 0, (size_t)count * sizeof(*values));
    }
}

int mj_version(void)
{
    return mjVERSION_HEADER;
}

const char* mj_versionString(void)
{
    return "3.9.0";
}

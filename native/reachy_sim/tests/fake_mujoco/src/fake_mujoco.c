#include <mujoco/mujoco.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static mjtNum* allocate_values(int count)
{
    if(count <= 0)
    {
        return NULL;
    }
    return calloc((size_t)count, sizeof(mjtNum));
}

void mj_defaultVFS(mjVFS* vfs)
{
    vfs->buffer = NULL;
    vfs->buffer_size = 0;
}

int mj_addBufferVFS(mjVFS* vfs, const char* name, const void* buffer, int nbuffer)
{
    if(vfs == NULL || name == NULL || buffer == NULL || nbuffer <= 0)
    {
        return -1;
    }
    vfs->buffer = buffer;
    vfs->buffer_size = nbuffer;
    return 0;
}

void mj_deleteVFS(mjVFS* vfs)
{
    if(vfs != NULL)
    {
        vfs->buffer = NULL;
        vfs->buffer_size = 0;
    }
}

mjModel* mj_loadXML(const char* filename, const mjVFS* vfs, char* error, int error_size)
{
    if(filename == NULL || vfs == NULL || vfs->buffer == NULL || vfs->buffer_size <= 0)
    {
        if(error != NULL && error_size > 0)
        {
            (void)snprintf(error, (size_t)error_size, "%s", "missing model buffer");
        }
        return NULL;
    }

    const char* const xml = vfs->buffer;
    if(strstr(xml, "malformed") != NULL || strstr(xml, "missing-close") != NULL)
    {
        if(error != NULL && error_size > 0)
        {
            (void)snprintf(error, (size_t)error_size, "%s", "XML parse error");
        }
        return NULL;
    }

    mjModel* model = calloc(1U, sizeof(*model));
    if(model == NULL)
    {
        return NULL;
    }
    model->opt.timestep = 0.002;
    model->nq = 2;
    model->nv = 2;
    model->na = 0;
    model->nu = 0;
    model->neq = 1;
    return model;
}

mjData* mj_makeData(const mjModel* model)
{
    if(model == NULL)
    {
        return NULL;
    }

    mjData* data = calloc(1U, sizeof(*data));
    if(data == NULL)
    {
        return NULL;
    }
    data->nefc = model->neq;
    data->qpos = allocate_values(model->nq);
    data->qvel = allocate_values(model->nv);
    data->qacc = allocate_values(model->nv);
    data->act = allocate_values(model->na);
    data->ctrl = allocate_values(model->nu);
    data->efc_pos = allocate_values(data->nefc);
    if(data->qpos == NULL || data->qvel == NULL || data->qacc == NULL ||
       data->efc_pos == NULL)
    {
        mj_deleteData(data);
        return NULL;
    }
    return data;
}

void mj_step(const mjModel* model, mjData* data)
{
    data->time += model->opt.timestep;
    data->qpos[0] += 0.000001;
    data->qpos[1] -= 0.000001;
    data->qvel[0] = 0.0005;
    data->qvel[1] = -0.0005;
    data->qacc[0] = 0.0;
    data->qacc[1] = 0.0;
    data->efc_pos[0] = 0.0000001;
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
    free(data->act);
    free(data->ctrl);
    free(data->efc_pos);
    free(data);
}

void mj_deleteModel(mjModel* model)
{
    free(model);
}

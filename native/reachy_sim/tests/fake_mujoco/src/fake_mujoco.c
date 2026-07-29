#include <mujoco/mujoco.h>

#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static mjtNum* allocate_values(mjtSize count)
{
    if(count <= 0)
    {
        return NULL;
    }
    if((uint64_t)count > (uint64_t)(SIZE_MAX / sizeof(mjtNum)))
    {
        return NULL;
    }
    return calloc((size_t)count, sizeof(mjtNum));
}

static int* allocate_types(mjtSize count)
{
    if(count <= 0)
    {
        return NULL;
    }
    if((uint64_t)count > (uint64_t)(SIZE_MAX / sizeof(int)))
    {
        return NULL;
    }
    return calloc((size_t)count, sizeof(int));
}

static void write_error(char* error, int error_size, const char* message)
{
    if(error != NULL && error_size > 0)
    {
        (void)snprintf(error, (size_t)error_size, "%s", message);
    }
}

static char* read_model_file(const char* filename)
{
    FILE* stream = fopen(filename, "rb");
    if(stream == NULL)
    {
        return NULL;
    }
    if(fseek(stream, 0L, SEEK_END) != 0)
    {
        (void)fclose(stream);
        return NULL;
    }
    const long length = ftell(stream);
    if(length <= 0L || fseek(stream, 0L, SEEK_SET) != 0)
    {
        (void)fclose(stream);
        return NULL;
    }

    char* bytes = malloc((size_t)length + 1U);
    if(bytes == NULL)
    {
        (void)fclose(stream);
        return NULL;
    }
    const size_t byte_count = (size_t)length;
    const size_t read_count = fread(bytes, 1U, byte_count, stream);
    const int close_result = fclose(stream);
    if(read_count != byte_count || close_result != 0)
    {
        free(bytes);
        return NULL;
    }
    bytes[byte_count] = '\0';
    return bytes;
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
    char* owned_xml = NULL;
    const char* xml = NULL;
    if(vfs != NULL && vfs->buffer != NULL && vfs->buffer_size > 0)
    {
        xml = vfs->buffer;
    }
    else if(filename != NULL)
    {
        owned_xml = read_model_file(filename);
        xml = owned_xml;
    }
    if(xml == NULL)
    {
        char message[256];
        const int result = snprintf(
            message,
            sizeof(message),
            "cannot read model: %s",
            errno == 0 ? "missing model buffer or file" : strerror(errno));
        write_error(
            error,
            error_size,
            result < 0 ? "cannot read model" : message);
        free(owned_xml);
        return NULL;
    }

    if(strstr(xml, "malformed") != NULL || strstr(xml, "missing-close") != NULL)
    {
        write_error(error, error_size, "XML parse error");
        free(owned_xml);
        return NULL;
    }

    mjModel* model = calloc(1U, sizeof(*model));
    if(model == NULL)
    {
        free(owned_xml);
        return NULL;
    }
    model->opt.timestep = 0.002;
    model->nq = 2;
    model->nv = 2;
    model->na = 0;
    model->nu = 0;
    model->neq = 1;
    model->nbody = 3;
    model->njnt = 2;
    model->nsite = 2;
    model->ncam = 0;
    free(owned_xml);
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
    data->nefc = model->neq + 1;
    data->qpos = allocate_values(model->nq);
    data->qvel = allocate_values(model->nv);
    data->qacc = allocate_values(model->nv);
    data->act = allocate_values(model->na);
    data->ctrl = allocate_values(model->nu);
    data->efc_pos = allocate_values(data->nefc);
    data->efc_type = allocate_types(data->nefc);
    if(data->qpos == NULL || data->qvel == NULL || data->qacc == NULL ||
       data->efc_pos == NULL || data->efc_type == NULL)
    {
        mj_deleteData(data);
        return NULL;
    }
    data->efc_type[0] = mjCNSTR_EQUALITY;
    data->efc_type[1] = mjCNSTR_CONTACT_FRICTIONLESS;
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
    data->efc_pos[1] = -0.05;
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
    free(data->efc_type);
    free(data);
}

void mj_deleteModel(mjModel* model)
{
    free(model);
}

#include <mujoco/mujoco.h>

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void write_error(char* error, int error_size, const char* message)
{
    if(error != NULL && error_size > 0)
    {
        (void)snprintf(error, (size_t)error_size, "%s", message);
    }
}

static mjtNum* allocate_values(size_t count)
{
    return count == 0U ? NULL : calloc(count, sizeof(mjtNum));
}

static mjtByte* allocate_bytes(size_t count)
{
    return count == 0U ? NULL : calloc(count, sizeof(mjtByte));
}

static mjModel* allocate_model(int backend_fixture)
{
    mjModel* const model = calloc(1U, sizeof(*model));
    if(model == NULL)
    {
        return NULL;
    }
    model->opt.timestep = 0.002;
    model->nq = backend_fixture != 0 ? 4 : 2;
    model->nv = backend_fixture != 0 ? 4 : 2;
    model->na = 0;
    model->nu = backend_fixture != 0 ? 3 : 0;
    model->neq = 1;
    model->nbody = backend_fixture != 0 ? 4 : 3;
    model->njnt = backend_fixture != 0 ? 4 : 2;
    model->nsite = 2;
    model->ncam = 0;
    model->nkey = backend_fixture != 0 ? 1 : 0;
    model->nmocap = 0;
    model->nuserdata = backend_fixture != 0 ? 2 : 0;
    model->actuator_ctrllimited = allocate_bytes((size_t)model->nu);
    model->actuator_ctrlrange = allocate_values((size_t)model->nu * 2U);
    if(model->nu > 0 &&
       (model->actuator_ctrllimited == NULL || model->actuator_ctrlrange == NULL))
    {
        mj_deleteModel(model);
        return NULL;
    }
    if(model->nu > 0)
    {
        model->actuator_ctrllimited[0] = 1U;
        model->actuator_ctrllimited[1] = 1U;
        model->actuator_ctrllimited[2] = 0U;
        model->actuator_ctrlrange[0] = -1.0;
        model->actuator_ctrlrange[1] = 1.0;
        model->actuator_ctrlrange[2] = -2.0;
        model->actuator_ctrlrange[3] = 2.0;
        model->actuator_ctrlrange[4] = 0.0;
        model->actuator_ctrlrange[5] = 0.0;
    }
    return model;
}

void mj_defaultVFS(mjVFS* vfs)
{
    if(vfs != NULL)
    {
        vfs->buffer = NULL;
        vfs->buffer_size = 0;
    }
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

static char* copy_vfs_xml(
    const mjVFS* vfs,
    char* error,
    int error_size)
{
    if(vfs == NULL || vfs->buffer == NULL || vfs->buffer_size <= 0)
    {
        write_error(error, error_size, "XML model buffer is missing");
        return NULL;
    }

    const size_t byte_count = (size_t)vfs->buffer_size;
    char* const xml = malloc(byte_count + 1U);
    if(xml == NULL)
    {
        write_error(error, error_size, "XML model buffer allocation failed");
        return NULL;
    }
    memcpy(xml, vfs->buffer, byte_count);
    xml[byte_count] = '\0';
    return xml;
}

static char* read_xml_file(
    const char* filename,
    char* error,
    int error_size)
{
    if(filename == NULL)
    {
        write_error(error, error_size, "XML model filename is missing");
        return NULL;
    }

    FILE* const stream = fopen(filename, "rb");
    if(stream == NULL)
    {
        write_error(error, error_size, "XML model file could not be opened");
        return NULL;
    }
    if(fseek(stream, 0L, SEEK_END) != 0)
    {
        write_error(error, error_size, "XML model file could not be sized");
        (void)fclose(stream);
        return NULL;
    }
    const long file_length = ftell(stream);
    if(file_length <= 0L || fseek(stream, 0L, SEEK_SET) != 0)
    {
        write_error(error, error_size, "XML model file is empty or unreadable");
        (void)fclose(stream);
        return NULL;
    }

    const size_t byte_count = (size_t)file_length;
    char* const xml = malloc(byte_count + 1U);
    if(xml == NULL)
    {
        write_error(error, error_size, "XML model file allocation failed");
        (void)fclose(stream);
        return NULL;
    }
    const size_t read_count = fread(xml, 1U, byte_count, stream);
    const int close_status = fclose(stream);
    if(read_count != byte_count || close_status != 0)
    {
        write_error(error, error_size, "XML model file could not be read completely");
        free(xml);
        return NULL;
    }
    xml[byte_count] = '\0';
    return xml;
}

mjModel* mj_loadXML(
    const char* filename,
    const mjVFS* vfs,
    char* error,
    int error_size)
{
    char* const xml = vfs != NULL
        ? copy_vfs_xml(vfs, error, error_size)
        : read_xml_file(filename, error, error_size);
    if(xml == NULL)
    {
        return NULL;
    }

    mjModel* model = NULL;
    if(strstr(xml, "<mujoco") == NULL || strstr(xml, "malformed") != NULL)
    {
        write_error(error, error_size, "XML parse error");
    }
    else if(strstr(xml, "external_asset") != NULL)
    {
        write_error(error, error_size, "resource not found via VFS");
    }
    else
    {
        model = allocate_model(strstr(xml, "backend_fixture") != NULL);
    }
    free(xml);
    return model;
}

mjModel* mj_loadModelBuffer(const void* buffer, int buffer_size)
{
    if(buffer == NULL || buffer_size < 3)
    {
        return NULL;
    }
    const unsigned char* const bytes = buffer;
    if(bytes[0] != (unsigned char)'M' ||
       bytes[1] != (unsigned char)'J' ||
       bytes[2] != (unsigned char)'B')
    {
        return NULL;
    }
    return allocate_model(1);
}

void mj_saveModel(
    const mjModel* model,
    const char* filename,
    void* buffer,
    int buffer_size)
{
    static const unsigned char CONTENT[] = {'M', 'J', 'B', 1U};
    (void)model;
    if(buffer != NULL && buffer_size >= (int)sizeof(CONTENT))
    {
        memcpy(buffer, CONTENT, sizeof(CONTENT));
        return;
    }
    if(filename != NULL)
    {
        FILE* const stream = fopen(filename, "wb");
        if(stream != NULL)
        {
            (void)fwrite(CONTENT, 1U, sizeof(CONTENT), stream);
            (void)fclose(stream);
        }
    }
}

mjModel* mj_loadModel(const char* filename, const mjVFS* vfs)
{
    (void)vfs;
    if(filename == NULL)
    {
        return NULL;

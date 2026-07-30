#include "reachy_sim.h"
#include "reachy_sim_state.h"

#include <errno.h>
#include <inttypes.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define RUNNER_MAX_MODEL_BYTES UINT64_C(67108864)

typedef struct RunnerCommandBatch {
    ReachySimCommandBatchHeader header;
    ReachySimActuatorCommand commands[];
} RunnerCommandBatch;

typedef struct RunnerState {
    uint8_t* bytes;
    size_t byte_count;
    ReachySimStateHeader legacy;
    ReachySimStatePayloadHeader payload;
} RunnerState;

static int read_file(
    const char* path,
    uint8_t** output,
    size_t* output_size)
{
    FILE* const stream = fopen(path, "rb");
    if(stream == NULL)
    {
        (void)fprintf(stderr, "cannot open model %s: %s\n", path, strerror(errno));
        return 0;
    }
    if(fseek(stream, 0L, SEEK_END) != 0)
    {
        (void)fprintf(stderr, "cannot seek model %s\n", path);
        (void)fclose(stream);
        return 0;
    }
    const long length = ftell(stream);
    if(length <= 0L || (uint64_t)length > RUNNER_MAX_MODEL_BYTES)
    {
        (void)fprintf(stderr, "model size is invalid: %ld\n", length);
        (void)fclose(stream);
        return 0;
    }
    if(fseek(stream, 0L, SEEK_SET) != 0)
    {
        (void)fprintf(stderr, "cannot rewind model %s\n", path);
        (void)fclose(stream);
        return 0;
    }

    const size_t byte_count = (size_t)length;
    uint8_t* const bytes = malloc(byte_count);
    if(bytes == NULL)
    {
        (void)fprintf(stderr, "cannot allocate %zu model bytes\n", byte_count);
        (void)fclose(stream);
        return 0;
    }
    const size_t read_count = fread(bytes, 1U, byte_count, stream);
    const int close_status = fclose(stream);
    if(read_count != byte_count || close_status != 0)
    {
        (void)fprintf(stderr, "cannot read model %s\n", path);
        free(bytes);
        return 0;
    }
    *output = bytes;
    *output_size = byte_count;
    return 1;
}

static int parse_step_count(const char* value, uint32_t* output)
{
    errno = 0;
    char* end = NULL;
    const unsigned long parsed = strtoul(value, &end, 10);
    if(errno != 0 || end == value || *end != '\0' || parsed == 0UL ||
       parsed > UINT32_MAX)
    {
        return 0;
    }
    *output = (uint32_t)parsed;
    return 1;
}

static ReachySimErrorInfo initialized_error(void)
{
    ReachySimErrorInfo error = {0};
    error.abi_version = REACHY_SIM_ABI_VERSION;
    error.struct_size = (uint32_t)sizeof(error);
    return error;
}

static void print_handle_error(ReachySimHandle handle, const char* operation)
{
    ReachySimErrorInfo error = initialized_error();
    if(reachy_sim_get_last_error(handle, &error) == REACHY_SIM_STATUS_OK)
    {
        (void)fprintf(
            stderr,
            "%s failed: status=%s recoverability=%u message=%s\n",
            operation,
            reachy_sim_status_string(error.status),
            error.recoverability,
            error.message);
    }
    else
    {
        (void)fprintf(stderr, "%s failed without retrievable diagnostics\n", operation);
    }
}

static int checked_add_u64(uint64_t left, uint64_t right, uint64_t* result)
{

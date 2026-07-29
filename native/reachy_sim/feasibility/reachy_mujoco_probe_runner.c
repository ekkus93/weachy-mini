#include "reachy_mujoco_probe.h"

#include <errno.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int read_file(const char* path, char** bytes, size_t* size)
{
    FILE* stream = fopen(path, "rb");
    if(stream == NULL)
    {
        (void)fprintf(stderr, "cannot open model %s: %s\n", path, strerror(errno));
        return -1;
    }
    if(fseek(stream, 0L, SEEK_END) != 0)
    {
        (void)fprintf(stderr, "cannot seek model %s\n", path);
        (void)fclose(stream);
        return -1;
    }
    const long length = ftell(stream);
    if(length <= 0L)
    {
        (void)fprintf(stderr, "model is empty or its size cannot be read: %s\n", path);
        (void)fclose(stream);
        return -1;
    }
    if(fseek(stream, 0L, SEEK_SET) != 0)
    {
        (void)fprintf(stderr, "cannot rewind model %s\n", path);
        (void)fclose(stream);
        return -1;
    }

    const size_t byte_count = (size_t)length;
    char* buffer = malloc(byte_count + 1U);
    if(buffer == NULL)
    {
        (void)fprintf(stderr, "cannot allocate %zu bytes for model\n", byte_count);
        (void)fclose(stream);
        return -1;
    }
    const size_t bytes_read = fread(buffer, 1U, byte_count, stream);
    const int close_result = fclose(stream);
    if(bytes_read != byte_count || close_result != 0)
    {
        (void)fprintf(stderr, "cannot read complete model %s\n", path);
        free(buffer);
        return -1;
    }
    buffer[byte_count] = '\0';
    *bytes = buffer;
    *size = byte_count;
    return 0;
}

static int parse_step_count(const char* text, uint64_t* step_count)
{
    errno = 0;
    char* end = NULL;
    const uintmax_t parsed = strtoumax(text, &end, 10);
    if(errno != 0 || end == text || *end != '\0' || parsed == 0U || parsed > UINT64_MAX)
    {
        return -1;
    }
    *step_count = (uint64_t)parsed;
    return 0;
}

static void print_json_string(const char* text)
{
    (void)putchar('"');
    for(const unsigned char* cursor = (const unsigned char*)text; *cursor != '\0'; ++cursor)
    {
        switch(*cursor)
        {
            case '"':
                (void)fputs("\\\"", stdout);
                break;
            case '\\':
                (void)fputs("\\\\", stdout);
                break;
            case '\n':
                (void)fputs("\\n", stdout);
                break;
            case '\r':
                (void)fputs("\\r", stdout);
                break;
            case '\t':
                (void)fputs("\\t", stdout);
                break;
            default:
                if(*cursor < 0x20U)
                {
                    (void)printf("\\u%04x", (unsigned int)*cursor);
                }
                else
                {
                    (void)putchar((int)*cursor);
                }
                break;
        }
    }
    (void)putchar('"');
}

static void print_report(
    ReachyMujocoProbeStatus status,
    const ReachyMujocoProbeReport* report,
    const char* error)
{
    (void)fputs("{\"status\":", stdout);
    print_json_string(reachy_mujoco_probe_status_string(status));
    (void)printf(
        ",\"status_code\":%u,\"completed_steps\":%" PRIu64
        ",\"simulated_seconds\":%.9f,\"maximum_constraint_residual\":%.17g"
        ",\"median_step_microseconds\":%.9f,\"p95_step_microseconds\":%.9f"
        ",\"maximum_step_microseconds\":%.9f,\"warning_count\":%" PRIu64
        ",\"error\":",
        report->status,
        report->completed_steps,
        report->simulated_seconds,
        report->maximum_constraint_residual,
        report->median_step_microseconds,
        report->p95_step_microseconds,
        report->maximum_step_microseconds,
        report->warning_count);
    print_json_string(error);
    (void)fputs("}\n", stdout);
}

int main(int argc, char** argv)
{
    if(argc < 2 || argc > 3)
    {
        (void)fprintf(stderr, "usage: %s MODEL_XML [STEP_COUNT]\n", argv[0]);
        return 2;
    }

    ReachyMujocoProbeConfig config = reachy_mujoco_probe_default_config();
    if(argc == 3 && parse_step_count(argv[2], &config.step_count) != 0)
    {
        (void)fprintf(stderr, "invalid step count: %s\n", argv[2]);
        return 2;
    }

    char* xml = NULL;
    size_t xml_size = 0U;
    if(read_file(argv[1], &xml, &xml_size) != 0)
    {
        return 2;
    }

    ReachyMujocoProbeReport report;
    char error[1024];
    const ReachyMujocoProbeStatus status = reachy_mujoco_probe_run_xml(
        xml,
        xml_size,
        &config,
        &report,
        error,
        sizeof(error));
    free(xml);
    print_report(status, &report, error);
    return status == REACHY_MUJOCO_PROBE_OK ? 0 : 1;
}

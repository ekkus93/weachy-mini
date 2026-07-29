#include "reachy_mujoco_probe.h"

#include <errno.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>

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
        ",\"compiled_counts\":{\"bodies_including_world\":%u,\"joints\":%u"
        ",\"actuators\":%u,\"equalities\":%u,\"sites\":%u,\"cameras\":%u"
        ",\"nq\":%u,\"nv\":%u},\"error\":",
        report->status,
        report->completed_steps,
        report->simulated_seconds,
        report->maximum_constraint_residual,
        report->median_step_microseconds,
        report->p95_step_microseconds,
        report->maximum_step_microseconds,
        report->warning_count,
        report->body_count,
        report->joint_count,
        report->actuator_count,
        report->equality_count,
        report->site_count,
        report->camera_count,
        report->position_count,
        report->velocity_count);
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

    ReachyMujocoProbeReport report;
    char error[1024];
    const ReachyMujocoProbeStatus status = reachy_mujoco_probe_run_path(
        argv[1],
        &config,
        &report,
        error,
        sizeof(error));
    print_report(status, &report, error);
    return status == REACHY_MUJOCO_PROBE_OK ? 0 : 1;
}

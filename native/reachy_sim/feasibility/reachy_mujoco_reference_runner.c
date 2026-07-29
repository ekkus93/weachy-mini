#include "reachy_reference_scenario.generated.h"

#include <mujoco/mujoco.h>

#include <inttypes.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct ReachyReferenceCheckpoint {
    uint64_t step;
    double simulation_time;
    double maximum_equality_residual;
    uint64_t warning_count;
    double* qpos;
    double* qvel;
    double* body_positions;
    double* body_quaternions;
} ReachyReferenceCheckpoint;

typedef struct ReachyReferenceTrace {
    ReachyReferenceCheckpoint checkpoints[REACHY_REFERENCE_CHECKPOINT_COUNT];
    int actuator_ids[REACHY_REFERENCE_ACTUATOR_COUNT];
    int body_ids[REACHY_REFERENCE_BODY_COUNT];
} ReachyReferenceTrace;

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

static int checked_count(mjtSize value, size_t* output)
{
    if(value < 0 || (uint64_t)value > (uint64_t)SIZE_MAX)
    {
        return 0;
    }
    *output = (size_t)value;
    return 1;
}

static int allocate_values(size_t count, double** output)
{
    if(count > SIZE_MAX / sizeof(**output))
    {
        return 0;
    }
    *output = calloc(count, sizeof(**output));
    return *output != NULL;
}

static void free_trace(ReachyReferenceTrace* trace)
{
    if(trace == NULL)
    {
        return;
    }
    for(size_t index = 0U; index < REACHY_REFERENCE_CHECKPOINT_COUNT; ++index)
    {
        free(trace->checkpoints[index].qpos);
        free(trace->checkpoints[index].qvel);
        free(trace->checkpoints[index].body_positions);
        free(trace->checkpoints[index].body_quaternions);
    }
    free(trace);
}

static ReachyReferenceTrace* create_trace(const mjModel* model)
{
    size_t nq = 0U;
    size_t nv = 0U;
    if(!checked_count(model->nq, &nq) || !checked_count(model->nv, &nv))
    {
        return NULL;
    }
    if(REACHY_REFERENCE_BODY_COUNT > SIZE_MAX / 4U)
    {
        return NULL;
    }

    ReachyReferenceTrace* trace = calloc(1U, sizeof(*trace));
    if(trace == NULL)
    {
        return NULL;
    }
    for(size_t index = 0U; index < REACHY_REFERENCE_CHECKPOINT_COUNT; ++index)
    {
        ReachyReferenceCheckpoint* checkpoint = &trace->checkpoints[index];
        checkpoint->step = REACHY_REFERENCE_CHECKPOINT_STEPS[index];
        if(!allocate_values(nq, &checkpoint->qpos) ||
           !allocate_values(nv, &checkpoint->qvel) ||
           !allocate_values(REACHY_REFERENCE_BODY_COUNT * 3U, &checkpoint->body_positions) ||
           !allocate_values(REACHY_REFERENCE_BODY_COUNT * 4U, &checkpoint->body_quaternions))
        {
            free_trace(trace);
            return NULL;
        }
    }
    return trace;
}

static int model_counts_match(const mjModel* model)
{
    return model->nbody == (mjtSize)REACHY_REFERENCE_EXPECTED_BODY_COUNT &&
           model->njnt == (mjtSize)REACHY_REFERENCE_EXPECTED_JOINT_COUNT &&
           model->nu == (mjtSize)REACHY_REFERENCE_EXPECTED_ACTUATOR_COUNT &&
           model->neq == (mjtSize)REACHY_REFERENCE_EXPECTED_EQUALITY_COUNT &&
           model->nsite == (mjtSize)REACHY_REFERENCE_EXPECTED_SITE_COUNT &&
           model->ncam == (mjtSize)REACHY_REFERENCE_EXPECTED_CAMERA_COUNT &&
           model->nq == (mjtSize)REACHY_REFERENCE_EXPECTED_NQ &&
           model->nv == (mjtSize)REACHY_REFERENCE_EXPECTED_NV;
}

static int resolve_names(const mjModel* model, ReachyReferenceTrace* trace, char* error, size_t error_size)
{
    for(size_t index = 0U; index < REACHY_REFERENCE_ACTUATOR_COUNT; ++index)
    {
        const int id = mj_name2id(model, mjOBJ_ACTUATOR, REACHY_REFERENCE_ACTUATOR_NAMES[index]);
        if(id < 0)
        {
            (void)snprintf(
                error,
                error_size,
                "missing actuator: %s",
                REACHY_REFERENCE_ACTUATOR_NAMES[index]);
            return 0;
        }
        trace->actuator_ids[index] = id;
    }
    for(size_t index = 0U; index < REACHY_REFERENCE_BODY_COUNT; ++index)
    {
        const int id = mj_name2id(model, mjOBJ_BODY, REACHY_REFERENCE_BODY_NAMES[index]);
        if(id < 0)
        {
            (void)snprintf(
                error,
                error_size,
                "missing body: %s",
                REACHY_REFERENCE_BODY_NAMES[index]);
            return 0;
        }
        trace->body_ids[index] = id;
    }
    return 1;
}

static int finite_values(const mjtNum* values, mjtSize count)
{
    if(count < 0 || (count > 0 && values == NULL))
    {
        return 0;
    }
    for(mjtSize index = 0; index < count; ++index)
    {
        if(!isfinite((double)values[index]))
        {
            return 0;
        }
    }
    return 1;
}

static int maximum_equality_residual(const mjData* data, double* output)
{
    if(data == NULL || output == NULL || data->nefc < 0 ||
       (data->nefc > 0 && (data->efc_pos == NULL || data->efc_type == NULL)))
    {
        return 0;
    }
    double maximum = 0.0;
    for(mjtSize index = 0; index < data->nefc; ++index)
    {
        if(data->efc_type[index] != mjCNSTR_EQUALITY)
        {
            continue;
        }
        const double residual = fabs((double)data->efc_pos[index]);
        if(!isfinite(residual))
        {
            return 0;
        }
        if(residual > maximum)
        {
            maximum = residual;
        }
    }
    *output = maximum;
    return 1;
}

static uint64_t warning_count(const mjData* data)
{
    uint64_t total = 0U;
    for(int index = 0; index < mjNWARNING; ++index)
    {
        if(data->warning[index].number > 0)
        {
            total += (uint64_t)data->warning[index].number;
        }
    }
    return total;
}

static size_t phase_index_for_step(uint64_t step)
{
    size_t phase_index = 0U;
    for(size_t index = 1U; index < REACHY_REFERENCE_PHASE_COUNT; ++index)
    {
        if(step < REACHY_REFERENCE_PHASES[index].start_step)
        {
            break;
        }
        phase_index = index;
    }
    return phase_index;
}

static void apply_targets(mjData* data, const ReachyReferenceTrace* trace, uint64_t step)
{
    const ReachyReferencePhase* phase = &REACHY_REFERENCE_PHASES[phase_index_for_step(step)];
    for(size_t index = 0U; index < REACHY_REFERENCE_ACTUATOR_COUNT; ++index)
    {
        data->ctrl[trace->actuator_ids[index]] = (mjtNum)phase->targets[index];
    }
}

static int capture_checkpoint(
    const mjModel* model,
    const mjData* data,
    ReachyReferenceTrace* trace,
    size_t checkpoint_index,
    char* error,
    size_t error_size)
{
    ReachyReferenceCheckpoint* checkpoint = &trace->checkpoints[checkpoint_index];
    size_t nq = 0U;
    size_t nv = 0U;
    if(!checked_count(model->nq, &nq) || !checked_count(model->nv, &nv))
    {
        (void)snprintf(error, error_size, "invalid model dimensions");
        return 0;
    }
    checkpoint->simulation_time = (double)data->time;
    checkpoint->warning_count = warning_count(data);
    if(!maximum_equality_residual(data, &checkpoint->maximum_equality_residual))
    {
        (void)snprintf(error, error_size, "invalid equality constraint metadata");
        return 0;
    }
    if(checkpoint->maximum_equality_residual > REACHY_REFERENCE_MAXIMUM_EQUALITY_RESIDUAL)
    {
        (void)snprintf(
            error,
            error_size,
            "equality residual %.17g exceeds %.17g at step %" PRIu64,
            checkpoint->maximum_equality_residual,
            REACHY_REFERENCE_MAXIMUM_EQUALITY_RESIDUAL,
            checkpoint->step);
        return 0;
    }
    for(size_t index = 0U; index < nq; ++index)
    {
        checkpoint->qpos[index] = (double)data->qpos[index];
    }
    for(size_t index = 0U; index < nv; ++index)
    {
        checkpoint->qvel[index] = (double)data->qvel[index];
    }
    for(size_t index = 0U; index < REACHY_REFERENCE_BODY_COUNT; ++index)
    {
        const int body_id = trace->body_ids[index];
        for(size_t component = 0U; component < 3U; ++component)
        {
            checkpoint->body_positions[(index * 3U) + component] =
                (double)data->xpos[(body_id * 3) + (int)component];
        }
        for(size_t component = 0U; component < 4U; ++component)
        {
            checkpoint->body_quaternions[(index * 4U) + component] =
                (double)data->xquat[(body_id * 4) + (int)component];
        }
    }
    return 1;
}

static void print_double_array(const double* values, size_t count)
{
    (void)putchar('[');
    for(size_t index = 0U; index < count; ++index)
    {
        if(index > 0U)
        {
            (void)putchar(',');
        }
        (void)printf("%.17g", values[index]);
    }
    (void)putchar(']');
}

static void print_counts(const mjModel* model)
{
    (void)printf(
        "{\"bodies_including_world\":%" PRId64
        ",\"joints\":%" PRId64 ",\"actuators\":%" PRId64
        ",\"equalities\":%" PRId64 ",\"sites\":%" PRId64
        ",\"cameras\":%" PRId64 ",\"nq\":%" PRId64 ",\"nv\":%" PRId64 "}",
        (int64_t)model->nbody,
        (int64_t)model->njnt,
        (int64_t)model->nu,
        (int64_t)model->neq,
        (int64_t)model->nsite,
        (int64_t)model->ncam,
        (int64_t)model->nq,
        (int64_t)model->nv);
}

static void print_trace(
    const mjModel* model,
    const ReachyReferenceTrace* trace,
    const char* platform)
{
    (void)fputs("{\"schema_version\":1,\"status\":\"ok\",\"platform\":", stdout);
    print_json_string(platform);
    (void)fputs(",\"scenario_id\":", stdout);
    print_json_string(REACHY_REFERENCE_SCENARIO_ID);
    (void)fputs(",\"scenario_sha256\":", stdout);
    print_json_string(REACHY_REFERENCE_SCENARIO_SHA256);
    (void)fputs(",\"source_model_sha256\":", stdout);
    print_json_string(REACHY_REFERENCE_MODEL_SHA256);
    (void)fputs(",\"mujoco_version\":", stdout);
    print_json_string(mj_versionString());
    (void)fputs(",\"compiled_counts\":", stdout);
    print_counts(model);
    (void)fputs(",\"checkpoints\":[", stdout);
    for(size_t checkpoint_index = 0U;
        checkpoint_index < REACHY_REFERENCE_CHECKPOINT_COUNT;
        ++checkpoint_index)
    {
        const ReachyReferenceCheckpoint* checkpoint = &trace->checkpoints[checkpoint_index];
        if(checkpoint_index > 0U)
        {
            (void)putchar(',');
        }
        (void)printf(
            "{\"step\":%" PRIu64 ",\"simulation_time\":%.17g"
            ",\"maximum_equality_residual\":%.17g,\"warning_count\":%" PRIu64
            ",\"qpos\":",
            checkpoint->step,
            checkpoint->simulation_time,
            checkpoint->maximum_equality_residual,
            checkpoint->warning_count);
        print_double_array(checkpoint->qpos, (size_t)model->nq);
        (void)fputs(",\"qvel\":", stdout);
        print_double_array(checkpoint->qvel, (size_t)model->nv);
        (void)fputs(",\"bodies\":[", stdout);
        for(size_t body_index = 0U; body_index < REACHY_REFERENCE_BODY_COUNT; ++body_index)
        {
            if(body_index > 0U)
            {
                (void)putchar(',');
            }
            (void)fputs("{\"name\":", stdout);
            print_json_string(REACHY_REFERENCE_BODY_NAMES[body_index]);
            (void)fputs(",\"position_metres\":", stdout);
            print_double_array(&checkpoint->body_positions[body_index * 3U], 3U);
            (void)fputs(",\"quaternion_wxyz\":", stdout);
            print_double_array(&checkpoint->body_quaternions[body_index * 4U], 4U);
            (void)putchar('}');
        }
        (void)fputs("]}", stdout);
    }
    (void)fputs("]}\n", stdout);
}

static void print_failure(const char* error)
{
    (void)fputs("{\"schema_version\":1,\"status\":\"failed\",\"error\":", stdout);
    print_json_string(error);
    (void)fputs("}\n", stdout);
}

int main(int argc, char** argv)
{
    if(argc < 2 || argc > 3)
    {
        (void)fprintf(stderr, "usage: %s MODEL_XML [PLATFORM_LABEL]\n", argv[0]);
        return 2;
    }
    const char* const platform = argc == 3 ? argv[2] : "native";
    char error[1024] = {0};
    char model_error[1024] = {0};
    mjModel* model = mj_loadXML(argv[1], NULL, model_error, (int)sizeof(model_error));
    if(model == NULL)
    {
        print_failure(model_error);
        return 1;
    }
    if(strcmp(mj_versionString(), REACHY_REFERENCE_MUJOCO_VERSION) != 0)
    {
        (void)snprintf(
            error,
            sizeof(error),
            "MuJoCo version mismatch: expected %s, found %s",
            REACHY_REFERENCE_MUJOCO_VERSION,
            mj_versionString());
        print_failure(error);
        mj_deleteModel(model);
        return 1;
    }
    if(fabs((double)model->opt.timestep - REACHY_REFERENCE_TIMESTEP_SECONDS) > 1e-12)
    {
        (void)snprintf(error, sizeof(error), "model timestep differs from scenario");
        print_failure(error);
        mj_deleteModel(model);
        return 1;
    }
    if(!model_counts_match(model))
    {
        print_failure("compiled model counts differ from the reference scenario");
        mj_deleteModel(model);
        return 1;
    }

    ReachyReferenceTrace* trace = create_trace(model);
    mjData* data = mj_makeData(model);
    if(trace == NULL || data == NULL)
    {
        print_failure("cannot allocate reference trace state");
        if(data != NULL)
        {
            mj_deleteData(data);
        }
        free_trace(trace);
        mj_deleteModel(model);
        return 1;
    }
    if(!resolve_names(model, trace, error, sizeof(error)))
    {
        print_failure(error);
        mj_deleteData(data);
        free_trace(trace);
        mj_deleteModel(model);
        return 1;
    }

    apply_targets(data, trace, 0U);
    mj_forward(model, data);
    if(!capture_checkpoint(model, data, trace, 0U, error, sizeof(error)))
    {
        print_failure(error);
        mj_deleteData(data);
        free_trace(trace);
        mj_deleteModel(model);
        return 1;
    }

    size_t next_checkpoint = 1U;
    for(uint64_t step = 0U; step < REACHY_REFERENCE_TOTAL_STEPS; ++step)
    {
        apply_targets(data, trace, step);
        mj_step(model, data);
        if(!finite_values(data->qpos, model->nq) ||
           !finite_values(data->qvel, model->nv) ||
           !finite_values(data->xpos, model->nbody * 3) ||
           !finite_values(data->xquat, model->nbody * 4) ||
           !finite_values(data->efc_pos, data->nefc))
        {
            (void)snprintf(error, sizeof(error), "non-finite state after step %" PRIu64, step + 1U);
            print_failure(error);
            mj_deleteData(data);
            free_trace(trace);
            mj_deleteModel(model);
            return 1;
        }
        if(warning_count(data) != 0U)
        {
            (void)snprintf(error, sizeof(error), "MuJoCo warning after step %" PRIu64, step + 1U);
            print_failure(error);
            mj_deleteData(data);
            free_trace(trace);
            mj_deleteModel(model);
            return 1;
        }
        const uint64_t completed_step = step + 1U;
        if(next_checkpoint < REACHY_REFERENCE_CHECKPOINT_COUNT &&
           completed_step == REACHY_REFERENCE_CHECKPOINT_STEPS[next_checkpoint])
        {
            if(!capture_checkpoint(
                   model,
                   data,
                   trace,
                   next_checkpoint,
                   error,
                   sizeof(error)))
            {
                print_failure(error);
                mj_deleteData(data);
                free_trace(trace);
                mj_deleteModel(model);
                return 1;
            }
            ++next_checkpoint;
        }
    }
    if(next_checkpoint != REACHY_REFERENCE_CHECKPOINT_COUNT)
    {
        print_failure("not all reference checkpoints were captured");
        mj_deleteData(data);
        free_trace(trace);
        mj_deleteModel(model);
        return 1;
    }

    print_trace(model, trace, platform);
    mj_deleteData(data);
    free_trace(trace);
    mj_deleteModel(model);
    return 0;
}

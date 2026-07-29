#define _POSIX_C_SOURCE 200809L

#include "reachy_mujoco_probe.h"

#include <mujoco/mujoco.h>

#include <limits.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#define REACHY_PROBE_ERROR_CAPACITY 1024
#define REACHY_PROBE_MODEL_NAME "reachy_probe.xml"

static void copy_error(char* destination, size_t capacity, const char* message)
{
    if(destination == NULL || capacity == 0U)
    {
        return;
    }

    const char* const source = message == NULL ? "unspecified error" : message;
    const int result = snprintf(destination, capacity, "%s", source);
    if(result < 0)
    {
        destination[0] = '\0';
    }
}

static void initialize_output(
    ReachyMujocoProbeReport* report,
    char* error_buffer,
    size_t error_buffer_size)
{
    if(report != NULL)
    {
        memset(report, 0, sizeof(*report));
        report->struct_size = (uint32_t)sizeof(*report);
    }
    if(error_buffer != NULL && error_buffer_size > 0U)
    {
        error_buffer[0] = '\0';
    }
}

static ReachyMujocoProbeStatus validate_common_arguments(
    const ReachyMujocoProbeConfig* config,
    ReachyMujocoProbeReport* report,
    char* error_buffer,
    size_t error_buffer_size)
{
    if(config == NULL || report == NULL || config->struct_size != sizeof(*config) ||
       config->step_count == 0U || !isfinite(config->expected_timestep_seconds) ||
       config->expected_timestep_seconds <= 0.0 ||
       !isfinite(config->maximum_constraint_residual) ||
       config->maximum_constraint_residual < 0.0)
    {
        copy_error(error_buffer, error_buffer_size, "invalid probe argument or structure size");
        if(report != NULL)
        {
            report->status = (uint32_t)REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
        }
        return REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
    }
    if(config->step_count > (uint64_t)(SIZE_MAX / sizeof(double)))
    {
        copy_error(error_buffer, error_buffer_size, "step timing allocation would overflow");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_ALLOCATION_FAILED;
        return REACHY_MUJOCO_PROBE_ALLOCATION_FAILED;
    }
    return REACHY_MUJOCO_PROBE_OK;
}

static double monotonic_seconds(void)
{
    struct timespec timestamp;
    if(clock_gettime(CLOCK_MONOTONIC, &timestamp) != 0)
    {
        return -1.0;
    }

    return (double)timestamp.tv_sec + ((double)timestamp.tv_nsec / 1000000000.0);
}

static int compare_double(const void* left, const void* right)
{
    const double left_value = *(const double*)left;
    const double right_value = *(const double*)right;
    if(left_value < right_value)
    {
        return -1;
    }
    if(left_value > right_value)
    {
        return 1;
    }
    return 0;
}

static double percentile(const double* sorted_values, size_t count, double fraction)
{
    if(count == 0U)
    {
        return 0.0;
    }

    const double scaled_index = fraction * (double)(count - 1U);
    const size_t lower_index = (size_t)floor(scaled_index);
    const size_t upper_index = (size_t)ceil(scaled_index);
    if(lower_index == upper_index)
    {
        return sorted_values[lower_index];
    }

    const double interpolation = scaled_index - (double)lower_index;
    return sorted_values[lower_index] +
           ((sorted_values[upper_index] - sorted_values[lower_index]) * interpolation);
}

static int array_is_finite(const mjtNum* values, mjtSize count)
{
    if(count <= 0)
    {
        return 1;
    }
    if(values == NULL)
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

        const double absolute_value = fabs((double)data->efc_pos[index]);
        if(!isfinite(absolute_value))
        {
            return 0;
        }
        if(absolute_value > maximum)
        {
            maximum = absolute_value;
        }
    }

    *output = maximum;
    return 1;
}

static uint64_t total_warning_count(const mjData* data)
{
    uint64_t total = 0U;
    for(int index = 0; index < mjNWARNING; ++index)
    {
        const int warning_count = data->warning[index].number;
        if(warning_count > 0)
        {
            total += (uint64_t)warning_count;
        }
    }
    return total;
}

static int write_u32_count(mjtSize value, uint32_t* output)
{
    if(value < 0 || (uint64_t)value > UINT32_MAX)
    {
        return 0;
    }
    *output = (uint32_t)value;
    return 1;
}

static int write_model_counts(
    const mjModel* model,
    ReachyMujocoProbeReport* report)
{
    return write_u32_count(model->nbody, &report->body_count) &&
           write_u32_count(model->njnt, &report->joint_count) &&
           write_u32_count(model->nu, &report->actuator_count) &&
           write_u32_count(model->neq, &report->equality_count) &&
           write_u32_count(model->nsite, &report->site_count) &&
           write_u32_count(model->ncam, &report->camera_count) &&
           write_u32_count(model->nq, &report->position_count) &&
           write_u32_count(model->nv, &report->velocity_count);
}

static ReachyMujocoProbeStatus run_loaded_model(
    mjModel* model,
    const ReachyMujocoProbeConfig* config,
    ReachyMujocoProbeReport* report,
    char* error_buffer,
    size_t error_buffer_size)
{
    if(!write_model_counts(model, report))
    {
        copy_error(
            error_buffer,
            error_buffer_size,
            "compiled model dimensions exceed the fixed-width probe report");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
        return REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
    }

    const double timestep_difference =
        fabs((double)model->opt.timestep - config->expected_timestep_seconds);
    if(timestep_difference > 1e-12)
    {
        copy_error(error_buffer, error_buffer_size, "model timestep does not match probe config");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
        return REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
    }

    double* step_microseconds = calloc((size_t)config->step_count, sizeof(*step_microseconds));
    if(step_microseconds == NULL)
    {
        copy_error(error_buffer, error_buffer_size, "cannot allocate step timing buffer");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_ALLOCATION_FAILED;
        return REACHY_MUJOCO_PROBE_ALLOCATION_FAILED;
    }

    mjData* data = mj_makeData(model);
    if(data == NULL)
    {
        copy_error(error_buffer, error_buffer_size, "MuJoCo data allocation failed");
        free(step_microseconds);
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_DATA_ALLOCATION_FAILED;
        return REACHY_MUJOCO_PROBE_DATA_ALLOCATION_FAILED;
    }

    ReachyMujocoProbeStatus status = REACHY_MUJOCO_PROBE_OK;
    double maximum_residual = 0.0;
    for(uint64_t step = 0U; step < config->step_count; ++step)
    {
        const double previous_time = (double)data->time;
        const double start = monotonic_seconds();
        if(start < 0.0)
        {
            copy_error(error_buffer, error_buffer_size, "monotonic clock failed");
            status = REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
            break;
        }

        mj_step(model, data);

        const double finish = monotonic_seconds();
        if(finish < start)
        {
            copy_error(error_buffer, error_buffer_size, "monotonic clock moved backward");
            status = REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
            break;
        }
        step_microseconds[step] = (finish - start) * 1000000.0;
        report->completed_steps = step + 1U;

        if((double)data->time <= previous_time)
        {
            copy_error(error_buffer, error_buffer_size, "MuJoCo simulation time did not advance");
            status = REACHY_MUJOCO_PROBE_TIME_DID_NOT_ADVANCE;
            break;
        }

        if(!array_is_finite(data->qpos, model->nq) ||
           !array_is_finite(data->qvel, model->nv) ||
           !array_is_finite(data->qacc, model->nv) ||
           !array_is_finite(data->act, model->na) ||
           !array_is_finite(data->ctrl, model->nu) ||
           !array_is_finite(data->efc_pos, data->nefc))
        {
            copy_error(error_buffer, error_buffer_size, "non-finite MuJoCo state detected");
            status = REACHY_MUJOCO_PROBE_NONFINITE_STATE;
            break;
        }

        double residual = 0.0;
        if(!maximum_equality_residual(data, &residual))
        {
            copy_error(error_buffer, error_buffer_size, "invalid MuJoCo constraint metadata");
            status = REACHY_MUJOCO_PROBE_NONFINITE_STATE;
            break;
        }
        if(residual > maximum_residual)
        {
            maximum_residual = residual;
        }
        if(residual > config->maximum_constraint_residual)
        {
            copy_error(error_buffer, error_buffer_size, "equality residual exceeded threshold");
            status = REACHY_MUJOCO_PROBE_CONSTRAINT_DIVERGENCE;
            break;
        }
    }

    report->simulated_seconds = (double)data->time;
    report->maximum_constraint_residual = maximum_residual;
    report->warning_count = total_warning_count(data);
    if(report->completed_steps > 0U)
    {
        const size_t completed_count = (size_t)report->completed_steps;
        qsort(step_microseconds, completed_count, sizeof(*step_microseconds), compare_double);
        report->median_step_microseconds = percentile(step_microseconds, completed_count, 0.5);
        report->p95_step_microseconds = percentile(step_microseconds, completed_count, 0.95);
        report->maximum_step_microseconds = step_microseconds[completed_count - 1U];
    }

    mj_deleteData(data);
    free(step_microseconds);
    report->status = (uint32_t)status;
    return status;
}

ReachyMujocoProbeConfig reachy_mujoco_probe_default_config(void)
{
    ReachyMujocoProbeConfig config;
    config.struct_size = (uint32_t)sizeof(config);
    config.step_count = 900000U;
    config.expected_timestep_seconds = 0.002;
    config.maximum_constraint_residual = 0.001;
    return config;
}

const char* reachy_mujoco_probe_status_string(ReachyMujocoProbeStatus status)
{
    switch(status)
    {
        case REACHY_MUJOCO_PROBE_OK:
            return "ok";
        case REACHY_MUJOCO_PROBE_INVALID_ARGUMENT:
            return "invalid_argument";
        case REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED:
            return "model_load_failed";
        case REACHY_MUJOCO_PROBE_DATA_ALLOCATION_FAILED:
            return "data_allocation_failed";
        case REACHY_MUJOCO_PROBE_NONFINITE_STATE:
            return "nonfinite_state";
        case REACHY_MUJOCO_PROBE_CONSTRAINT_DIVERGENCE:
            return "constraint_divergence";
        case REACHY_MUJOCO_PROBE_TIME_DID_NOT_ADVANCE:
            return "time_did_not_advance";
        case REACHY_MUJOCO_PROBE_ALLOCATION_FAILED:
            return "allocation_failed";
        case REACHY_MUJOCO_PROBE_VFS_FAILED:
            return "vfs_failed";
        default:
            return "unknown";
    }
}

ReachyMujocoProbeStatus reachy_mujoco_probe_run_xml(
    const char* xml,
    size_t xml_size,
    const ReachyMujocoProbeConfig* config,
    ReachyMujocoProbeReport* report,
    char* error_buffer,
    size_t error_buffer_size)
{
    initialize_output(report, error_buffer, error_buffer_size);
    ReachyMujocoProbeStatus status = validate_common_arguments(
        config,
        report,
        error_buffer,
        error_buffer_size);
    if(status != REACHY_MUJOCO_PROBE_OK)
    {
        return status;
    }
    if(xml == NULL || xml_size == 0U || xml_size > (size_t)INT_MAX)
    {
        copy_error(error_buffer, error_buffer_size, "invalid XML buffer");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
        return REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
    }

    mjVFS vfs;
    mj_defaultVFS(&vfs);
    const int add_result = mj_addBufferVFS(&vfs, REACHY_PROBE_MODEL_NAME, xml, (int)xml_size);
    if(add_result != 0)
    {
        copy_error(error_buffer, error_buffer_size, "cannot add model buffer to MuJoCo VFS");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_VFS_FAILED;
        mj_deleteVFS(&vfs);
        return REACHY_MUJOCO_PROBE_VFS_FAILED;
    }

    char model_error[REACHY_PROBE_ERROR_CAPACITY] = {0};
    mjModel* model = mj_loadXML(
        REACHY_PROBE_MODEL_NAME,
        &vfs,
        model_error,
        (int)sizeof(model_error));
    if(model == NULL)
    {
        copy_error(error_buffer, error_buffer_size, model_error);
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED;
        mj_deleteVFS(&vfs);
        return REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED;
    }

    status = run_loaded_model(model, config, report, error_buffer, error_buffer_size);
    mj_deleteModel(model);
    mj_deleteVFS(&vfs);
    return status;
}

ReachyMujocoProbeStatus reachy_mujoco_probe_run_path(
    const char* model_path,
    const ReachyMujocoProbeConfig* config,
    ReachyMujocoProbeReport* report,
    char* error_buffer,
    size_t error_buffer_size)
{
    initialize_output(report, error_buffer, error_buffer_size);
    ReachyMujocoProbeStatus status = validate_common_arguments(
        config,
        report,
        error_buffer,
        error_buffer_size);
    if(status != REACHY_MUJOCO_PROBE_OK)
    {
        return status;
    }
    if(model_path == NULL || model_path[0] == '\0')
    {
        copy_error(error_buffer, error_buffer_size, "model path is empty");
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
        return REACHY_MUJOCO_PROBE_INVALID_ARGUMENT;
    }

    char model_error[REACHY_PROBE_ERROR_CAPACITY] = {0};
    mjModel* model = mj_loadXML(model_path, NULL, model_error, (int)sizeof(model_error));
    if(model == NULL)
    {
        copy_error(error_buffer, error_buffer_size, model_error);
        report->status = (uint32_t)REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED;
        return REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED;
    }

    status = run_loaded_model(model, config, report, error_buffer, error_buffer_size);
    mj_deleteModel(model);
    return status;
}

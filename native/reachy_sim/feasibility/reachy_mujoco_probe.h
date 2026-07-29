#ifndef REACHY_MUJOCO_PROBE_H
#define REACHY_MUJOCO_PROBE_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef enum ReachyMujocoProbeStatus {
    REACHY_MUJOCO_PROBE_OK = 0,
    REACHY_MUJOCO_PROBE_INVALID_ARGUMENT = 1,
    REACHY_MUJOCO_PROBE_MODEL_LOAD_FAILED = 2,
    REACHY_MUJOCO_PROBE_DATA_ALLOCATION_FAILED = 3,
    REACHY_MUJOCO_PROBE_NONFINITE_STATE = 4,
    REACHY_MUJOCO_PROBE_CONSTRAINT_DIVERGENCE = 5,
    REACHY_MUJOCO_PROBE_TIME_DID_NOT_ADVANCE = 6,
    REACHY_MUJOCO_PROBE_ALLOCATION_FAILED = 7,
    REACHY_MUJOCO_PROBE_VFS_FAILED = 8
} ReachyMujocoProbeStatus;

typedef struct ReachyMujocoProbeConfig {
    uint32_t struct_size;
    uint64_t step_count;
    double expected_timestep_seconds;
    double maximum_constraint_residual;
} ReachyMujocoProbeConfig;

typedef struct ReachyMujocoProbeReport {
    uint32_t struct_size;
    uint32_t status;
    uint64_t completed_steps;
    double simulated_seconds;
    double maximum_constraint_residual;
    double median_step_microseconds;
    double p95_step_microseconds;
    double maximum_step_microseconds;
    uint64_t warning_count;
} ReachyMujocoProbeReport;

ReachyMujocoProbeConfig reachy_mujoco_probe_default_config(void);

ReachyMujocoProbeStatus reachy_mujoco_probe_run_xml(
    const char* xml,
    size_t xml_size,
    const ReachyMujocoProbeConfig* config,
    ReachyMujocoProbeReport* report,
    char* error_buffer,
    size_t error_buffer_size);

const char* reachy_mujoco_probe_status_string(ReachyMujocoProbeStatus status);

#ifdef __cplusplus
}
#endif

#endif

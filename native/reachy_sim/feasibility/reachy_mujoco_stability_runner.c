#define _POSIX_C_SOURCE 200809L

#include "reachy_stability_profile.generated.h"

#include <mujoco/mujoco.h>

#include <errno.h>
#include <inttypes.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

typedef struct ReachyStabilityMetrics {
    uint64_t completed_steps;
    double maximum_equality_residual;
    double maximum_joint_limit_violation;
    double maximum_contact_penetration;
    uint64_t maximum_contact_count;
    double minimum_total_energy;
    double maximum_total_energy;
    double maximum_absolute_total_energy;
    uint64_t warning_count;
    uint64_t deadline_miss_count;
    double total_step_microseconds;
    double maximum_step_microseconds;
} ReachyStabilityMetrics;

typedef struct ReachyStabilityRun {
    int actuator_ids[REACHY_STABILITY_ACTUATOR_COUNT];
    ReachyStabilityMetrics aggregate;
    ReachyStabilityMetrics phases[REACHY_STABILITY_PHASE_COUNT];
    double* step_timings;
    size_t timing_count;
    size_t timing_capacity;
} ReachyStabilityRun;

#include "reachy_mujoco_stability_runner_support.inc"
#include "reachy_mujoco_stability_runner_report.inc"
#include "reachy_mujoco_stability_runner_main.inc"

#ifndef REACHY_SIM_STATE_H
#define REACHY_SIM_STATE_H

#include "reachy_sim.h"

#ifdef __cplusplus
extern "C" {
#endif

enum {
    REACHY_SIM_STATE_FORMAT_VERSION = 1
};

#define REACHY_SIM_STATE_REQUEST_MAGIC UINT64_C(0x5253494d53544154)

typedef struct ReachySimStateRequest {
    uint64_t magic;
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t state_format_version;
    uint32_t reserved;
} ReachySimStateRequest;

typedef struct ReachySimStatePayloadHeader {
    uint32_t state_format_version;
    uint32_t struct_size;
    uint64_t total_size;
    uint64_t model_hash;
    uint64_t sequence;
    double simulation_time;
    uint32_t continuity_id;
    uint32_t reserved;
    uint32_t qpos_count;
    uint32_t qvel_count;
    uint32_t actuator_observation_count;
    uint32_t body_pose_count;
    uint64_t qpos_offset;
    uint64_t qvel_offset;
    uint64_t actuator_observation_offset;
    uint64_t body_pose_offset;
    ReachySimCalibrationProfileId calibration_profile_id;
    uint64_t warning_count;
    uint32_t constraint_count;
    uint32_t equality_constraint_count;
    double maximum_constraint_residual;
    double maximum_equality_constraint_residual;
} ReachySimStatePayloadHeader;

typedef struct ReachySimActuatorObservation {
    uint32_t actuator_id;
    uint32_t reserved;
    double control_value;
    double actuator_force;
    double length;
    double velocity;
} ReachySimActuatorObservation;

typedef struct ReachySimBodyPose {
    uint32_t body_id;
    uint32_t reserved;
    double position_metres[3];
    double quaternion_wxyz[4];
} ReachySimBodyPose;

#ifdef __cplusplus
}
#endif

#endif

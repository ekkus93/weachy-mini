#ifndef REACHY_SIM_STATE_H
#define REACHY_SIM_STATE_H

#include "reachy_sim.h"

#ifdef __cplusplus
extern "C" {
#endif

enum {
    REACHY_SIM_STATE_FORMAT_VERSION = 1,
    REACHY_SIM_DYNAMICS_STATE_FORMAT_VERSION = 2
};

#define REACHY_SIM_STATE_REQUEST_MAGIC UINT64_C(0x5253494d53544154)
#define REACHY_SIM_INVALID_OBJECT_ID UINT32_MAX

typedef enum ReachySimContactObservationFlag {
    REACHY_SIM_CONTACT_FLAG_INTERNAL = UINT32_C(1) << 0,
    REACHY_SIM_CONTACT_FLAG_EXTERNAL = UINT32_C(1) << 1,
    REACHY_SIM_CONTACT_FLAG_OVERLOAD = UINT32_C(1) << 2
} ReachySimContactObservationFlag;

typedef enum ReachySimHardStopObservationFlag {
    REACHY_SIM_HARD_STOP_FLAG_LOWER_ACTIVE = UINT32_C(1) << 0,
    REACHY_SIM_HARD_STOP_FLAG_UPPER_ACTIVE = UINT32_C(1) << 1,
    REACHY_SIM_HARD_STOP_FLAG_OVERLOAD = UINT32_C(1) << 2,
    REACHY_SIM_HARD_STOP_FLAG_POSITION_VIOLATION = UINT32_C(1) << 3
} ReachySimHardStopObservationFlag;

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

typedef struct ReachySimDynamicsStatePayloadHeader {
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
    uint32_t contact_observation_count;
    uint32_t hard_stop_observation_count;
    uint32_t contact_overload_count;
    uint32_t hard_stop_event_count;
    uint64_t qpos_offset;
    uint64_t qvel_offset;
    uint64_t actuator_observation_offset;
    uint64_t body_pose_offset;
    uint64_t contact_observation_offset;
    uint64_t hard_stop_observation_offset;
    ReachySimCalibrationProfileId calibration_profile_id;
    uint64_t warning_count;
    uint32_t constraint_count;
    uint32_t equality_constraint_count;
    double maximum_constraint_residual;
    double maximum_equality_constraint_residual;
    double maximum_contact_penetration_metres;
    double maximum_contact_normal_force_newtons;
    double maximum_contact_tangent_force_newtons;
    double maximum_contact_impulse_newton_seconds;
    double maximum_hard_stop_force;
} ReachySimDynamicsStatePayloadHeader;

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

typedef struct ReachySimContactObservation {
    uint32_t contact_id;
    uint32_t geom1_id;
    uint32_t geom2_id;
    uint32_t body1_id;
    uint32_t body2_id;
    uint32_t flags;
    double position_metres[3];
    double normal[3];
    double penetration_metres;
    double normal_force_newtons;
    double tangent_force_newtons;
    double impulse_newton_seconds;
} ReachySimContactObservation;

typedef struct ReachySimHardStopObservation {
    uint32_t joint_id;
    uint32_t actuator_id;
    uint32_t flags;
    uint32_t reserved;
    double position;
    double lower_limit;
    double upper_limit;
    double signed_distance_to_limit;
    double limit_force;
    double impulse;
} ReachySimHardStopObservation;

#ifdef __cplusplus
}
#endif

#endif

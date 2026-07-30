#ifndef REACHY_SIM_H
#define REACHY_SIM_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#if defined(REACHY_SIM_BUILD_SHARED)
#define REACHY_SIM_API __declspec(dllexport)
#elif defined(REACHY_SIM_USE_SHARED)
#define REACHY_SIM_API __declspec(dllimport)
#else
#define REACHY_SIM_API
#endif
#else
#define REACHY_SIM_API __attribute__((visibility("default")))
#endif

enum {
    REACHY_SIM_ABI_VERSION = 2,
    REACHY_SIM_SNAPSHOT_FORMAT_VERSION = 1,
    REACHY_SIM_ERROR_MESSAGE_CAPACITY = 256
};

typedef uint64_t ReachySimHandle;
#define REACHY_SIM_INVALID_HANDLE UINT64_C(0)

typedef uint64_t ReachySimCalibrationProfileId;
#define REACHY_SIM_CALIBRATION_PROFILE_UNCALIBRATED UINT64_C(0)

typedef enum ReachySimStatus {
    REACHY_SIM_STATUS_OK = 0,
    REACHY_SIM_STATUS_INVALID_ARGUMENT = 1,
    REACHY_SIM_STATUS_ABI_MISMATCH = 2,
    REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH = 3,
    REACHY_SIM_STATUS_MODEL_EMPTY = 4,
    REACHY_SIM_STATUS_MODEL_TOO_LARGE = 5,
    REACHY_SIM_STATUS_ALLOCATION_FAILED = 6,
    REACHY_SIM_STATUS_RESOURCE_EXHAUSTED = 7,
    REACHY_SIM_STATUS_BACKEND_UNAVAILABLE = 8,
    REACHY_SIM_STATUS_BACKEND_ERROR = 9,
    REACHY_SIM_STATUS_INVALID_HANDLE = 10,
    REACHY_SIM_STATUS_STALE_HANDLE = 11,
    REACHY_SIM_STATUS_HANDLE_BUSY = 12,
    REACHY_SIM_STATUS_BUFFER_TOO_SMALL = 13,
    REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR = 14,
    REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE = 15,
    REACHY_SIM_STATUS_UNSUPPORTED = 16,
    REACHY_SIM_STATUS_NUMERIC_FAILURE = 17
} ReachySimStatus;

typedef enum ReachySimRecoverability {
    REACHY_SIM_RECOVERABILITY_NONE = 0,
    REACHY_SIM_RECOVERABILITY_RETRY = 1,
    REACHY_SIM_RECOVERABILITY_RECREATE_HANDLE = 2,
    REACHY_SIM_RECOVERABILITY_RELOAD_MODEL = 3,
    REACHY_SIM_RECOVERABILITY_FATAL_CONFIGURATION = 4
} ReachySimRecoverability;

typedef enum ReachySimCapabilityFlag {
    REACHY_SIM_CAPABILITY_RESET = UINT64_C(1) << 0,
    REACHY_SIM_CAPABILITY_STEP = UINT64_C(1) << 1,
    REACHY_SIM_CAPABILITY_COMMANDS = UINT64_C(1) << 2,
    REACHY_SIM_CAPABILITY_STATE = UINT64_C(1) << 3,
    REACHY_SIM_CAPABILITY_WRENCH = UINT64_C(1) << 4,
    REACHY_SIM_CAPABILITY_SNAPSHOT = UINT64_C(1) << 5
} ReachySimCapabilityFlag;

typedef enum ReachySimConfigFlag {
    REACHY_SIM_CONFIG_FLAG_MODEL_XML = UINT32_C(1) << 0,
    REACHY_SIM_CONFIG_FLAG_MODEL_MJB = UINT32_C(1) << 1
} ReachySimConfigFlag;

typedef enum ReachySimResetPose {
    REACHY_SIM_RESET_POSE_SLEEP_REST = 0,
    REACHY_SIM_RESET_POSE_NEUTRAL_AWAKE = 1
} ReachySimResetPose;

typedef enum ReachySimHealthFlag {
    REACHY_SIM_HEALTH_FLAG_SLEEPING = UINT32_C(1) << 0,
    REACHY_SIM_HEALTH_FLAG_MUJOCO_WARNING = UINT32_C(1) << 1
} ReachySimHealthFlag;

typedef struct ReachySimConfig {
    uint32_t abi_version;
    uint32_t struct_size;
    double timestep_seconds;
    uint32_t max_command_count;
    uint32_t flags;
} ReachySimConfig;

typedef struct ReachySimCapabilities {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t capability_flags;
    uint64_t max_model_bytes;
    uint64_t max_command_bytes;
    uint64_t max_snapshot_bytes;
} ReachySimCapabilities;

typedef struct ReachySimStateHeader {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t sequence;
    double simulation_time;
    uint32_t body_count;
    uint32_t joint_count;
    uint32_t actuator_count;
    uint32_t contact_count;
    uint32_t health_flags;
    uint32_t reserved;
} ReachySimStateHeader;

typedef struct ReachySimCommandBatchHeader {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t sequence;
    uint32_t command_count;
    uint32_t byte_count;
} ReachySimCommandBatchHeader;

typedef struct ReachySimActuatorCommand {
    uint32_t abi_version;
    uint32_t struct_size;
    uint32_t actuator_id;
    uint32_t reserved;
    double control_value;
} ReachySimActuatorCommand;

typedef struct ReachySimWrenchCommand {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t body_id;
    double force_newtons[3];
    double torque_newton_metres[3];
    double application_point_metres[3];
    double duration_seconds;
} ReachySimWrenchCommand;

typedef struct ReachySimSnapshotHeader {
    uint32_t abi_version;
    uint32_t struct_size;
    uint64_t model_hash;
    uint64_t sequence;
    double simulation_time;
    uint32_t payload_size;
    uint32_t snapshot_version;
    ReachySimCalibrationProfileId calibration_profile_id;
} ReachySimSnapshotHeader;

typedef struct ReachySimErrorInfo {
    uint32_t abi_version;
    uint32_t struct_size;
    int32_t status;
    uint32_t recoverability;
    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY];
} ReachySimErrorInfo;

REACHY_SIM_API uint32_t reachy_sim_abi_version(void);
REACHY_SIM_API const char* reachy_sim_version_string(void);
REACHY_SIM_API const char* reachy_sim_status_string(int32_t status);
REACHY_SIM_API uint32_t reachy_sim_status_recoverability(int32_t status);
REACHY_SIM_API ReachySimConfig reachy_sim_default_config(void);
REACHY_SIM_API int32_t reachy_sim_get_capabilities(ReachySimCapabilities* capabilities);
REACHY_SIM_API int32_t reachy_sim_get_handle_capabilities(
    ReachySimHandle handle,
    ReachySimCapabilities* capabilities);

REACHY_SIM_API int32_t reachy_sim_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimHandle* out_handle,
    ReachySimErrorInfo* out_error);
REACHY_SIM_API int32_t reachy_sim_destroy(ReachySimHandle handle);
REACHY_SIM_API int32_t reachy_sim_reset(ReachySimHandle handle, uint32_t reset_id);
REACHY_SIM_API int32_t reachy_sim_step(ReachySimHandle handle, uint32_t step_count);
REACHY_SIM_API int32_t reachy_sim_submit_commands(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count);
REACHY_SIM_API int32_t reachy_sim_copy_state(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size);
REACHY_SIM_API int32_t reachy_sim_apply_wrench(
    ReachySimHandle handle,
    const ReachySimWrenchCommand* command);
REACHY_SIM_API int32_t reachy_sim_copy_snapshot(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size);
REACHY_SIM_API int32_t reachy_sim_restore_snapshot(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count);
REACHY_SIM_API int32_t reachy_sim_get_last_error(
    ReachySimHandle handle,
    ReachySimErrorInfo* out_error);

#ifdef __cplusplus
}
#endif

#endif

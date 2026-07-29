#ifndef REACHY_SIM_BACKEND_H
#define REACHY_SIM_BACKEND_H

#include "reachy_sim.h"

typedef struct ReachySimBackendOperations {
    void (*destroy)(void* context);
    ReachySimStatus (*reset)(
        void* context,
        uint32_t reset_id,
        char* error,
        size_t error_size);
    ReachySimStatus (*step)(
        void* context,
        uint32_t step_count,
        char* error,
        size_t error_size);
    ReachySimStatus (*submit_commands)(
        void* context,
        const void* bytes,
        size_t byte_count,
        char* error,
        size_t error_size);
    ReachySimStatus (*copy_state)(
        void* context,
        void* bytes,
        size_t byte_capacity,
        size_t* required_size,
        char* error,
        size_t error_size);
    ReachySimStatus (*apply_wrench)(
        void* context,
        const ReachySimWrenchCommand* command,
        char* error,
        size_t error_size);
    ReachySimStatus (*copy_snapshot)(
        void* context,
        void* bytes,
        size_t byte_capacity,
        size_t* required_size,
        char* error,
        size_t error_size);
    ReachySimStatus (*restore_snapshot)(
        void* context,
        const void* bytes,
        size_t byte_count,
        char* error,
        size_t error_size);
} ReachySimBackendOperations;

typedef struct ReachySimBackendInstance {
    void* context;
    const ReachySimBackendOperations* operations;
    uint64_t capability_flags;
    uint64_t model_hash;
} ReachySimBackendInstance;

ReachySimStatus reachy_sim_backend_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimBackendInstance* out_backend,
    char* error,
    size_t error_size);

#endif

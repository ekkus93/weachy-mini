#include "reachy_sim.h"

#include "reachy_sim_backend.h"

#include <stdatomic.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>

#define REACHY_SIM_MAX_HANDLES 64U
#define REACHY_SIM_MAX_MODEL_BYTES UINT64_C(67108864)
#define REACHY_SIM_MAX_COMMAND_BYTES UINT64_C(1048576)
#define REACHY_SIM_MAX_SNAPSHOT_BYTES UINT64_C(67108864)

typedef struct ReachySimSlot {
    uint32_t generation;
    bool in_use;
    atomic_bool destroying;
    atomic_uint active_calls;
    ReachySimBackendInstance backend;
    ReachySimErrorInfo last_error;
} ReachySimSlot;

typedef struct ReachySimSlotLease {
    ReachySimSlot* slot;
    ReachySimStatus status;
} ReachySimSlotLease;

static ReachySimSlot g_slots[REACHY_SIM_MAX_HANDLES];
static atomic_flag g_registry_lock = ATOMIC_FLAG_INIT;

static void registry_lock(void)
{
    while(atomic_flag_test_and_set_explicit(&g_registry_lock, memory_order_acquire))
    {
    }
}

static void registry_unlock(void)
{
    atomic_flag_clear_explicit(&g_registry_lock, memory_order_release);
}

static void clear_error_info(ReachySimErrorInfo* error)
{
    if(error == NULL)
    {
        return;
    }

    memset(error, 0, sizeof(*error));
    error->abi_version = REACHY_SIM_ABI_VERSION;
    error->struct_size = (uint32_t)sizeof(*error);
    error->status = REACHY_SIM_STATUS_OK;
    error->recoverability = REACHY_SIM_RECOVERABILITY_NONE;
}

static void write_error_info(
    ReachySimErrorInfo* error,
    ReachySimStatus status,
    const char* message)
{
    clear_error_info(error);
    if(error == NULL)
    {
        return;
    }

    error->status = (int32_t)status;
    error->recoverability = reachy_sim_status_recoverability((int32_t)status);
    if(message != NULL)
    {
        (void)snprintf(error->message, sizeof(error->message), "%s", message);
    }
}

static void set_slot_error(
    ReachySimSlot* slot,
    ReachySimStatus status,
    const char* message)
{
    if(slot != NULL)
    {
        write_error_info(&slot->last_error, status, message);
    }
}

static bool backend_operations_complete(
    const ReachySimBackendOperations* operations)
{
    return operations != NULL &&
        operations->destroy != NULL &&
        operations->reset != NULL &&
        operations->step != NULL &&
        operations->submit_commands != NULL &&
        operations->copy_state != NULL &&
        operations->apply_wrench != NULL &&
        operations->copy_snapshot != NULL &&
        operations->restore_snapshot != NULL;
}

static ReachySimStatus validate_config(
    const ReachySimConfig* config,
    char* message,
    size_t message_size)
{
    if(config == NULL)
    {
        (void)snprintf(message, message_size, "%s", "config is null");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(config->abi_version != REACHY_SIM_ABI_VERSION)
    {
        (void)snprintf(
            message,
            message_size,
            "config ABI mismatch: expected %u, received %u",
            (unsigned int)REACHY_SIM_ABI_VERSION,
            config->abi_version);
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(config->struct_size != (uint32_t)sizeof(*config))
    {
        (void)snprintf(
            message,
            message_size,
            "config size mismatch: expected %zu, received %u",
            sizeof(*config),
            config->struct_size);
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    if(config->timestep_seconds <= 0.0)
    {
        (void)snprintf(message, message_size, "%s", "timestep must be positive");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(config->max_command_count == 0U)
    {
        (void)snprintf(
            message,
            message_size,
            "%s",
            "max_command_count must be positive");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimHandle make_handle(uint32_t slot_index, uint32_t generation)
{
    return ((uint64_t)generation << 32U) |
        ((uint64_t)slot_index + UINT64_C(1));
}

static bool decode_handle(
    ReachySimHandle handle,
    uint32_t* slot_index,
    uint32_t* generation)
{
    const uint32_t encoded_slot =
        (uint32_t)(handle & UINT64_C(0xffffffff));
    if(handle == REACHY_SIM_INVALID_HANDLE || encoded_slot == 0U)
    {
        return false;
    }
    if(encoded_slot > REACHY_SIM_MAX_HANDLES)
    {
        return false;
    }

    *slot_index = encoded_slot - 1U;
    *generation = (uint32_t)(handle >> 32U);
    return *generation != 0U;
}

static ReachySimSlotLease acquire_slot(ReachySimHandle handle)
{
    ReachySimSlotLease lease = {
        NULL,
        REACHY_SIM_STATUS_INVALID_HANDLE};
    uint32_t slot_index = 0U;
    uint32_t generation = 0U;
    if(!decode_handle(handle, &slot_index, &generation))
    {
        return lease;
    }

    registry_lock();
    ReachySimSlot* const slot = &g_slots[slot_index];
    if(!slot->in_use || slot->generation != generation)
    {
        lease.status = REACHY_SIM_STATUS_STALE_HANDLE;
        registry_unlock();
        return lease;
    }
    if(atomic_load_explicit(&slot->destroying, memory_order_acquire))
    {
        lease.status = REACHY_SIM_STATUS_HANDLE_BUSY;
        registry_unlock();
        return lease;
    }

    (void)atomic_fetch_add_explicit(
        &slot->active_calls,
        1U,
        memory_order_acq_rel);
    lease.slot = slot;
    lease.status = REACHY_SIM_STATUS_OK;
    registry_unlock();
    return lease;
}

static void release_slot(ReachySimSlot* slot)
{
    (void)atomic_fetch_sub_explicit(
        &slot->active_calls,
        1U,
        memory_order_acq_rel);
}

static int32_t finish_backend_call(
    ReachySimSlot* slot,
    ReachySimStatus status,
    const char* message)
{
    if(status == REACHY_SIM_STATUS_OK)
    {
        set_slot_error(slot, REACHY_SIM_STATUS_OK, "");
    }
    else
    {
        set_slot_error(slot, status, message);
    }
    release_slot(slot);
    return (int32_t)status;
}

static int32_t lease_failure(ReachySimStatus status)
{
    return (int32_t)status;
}

uint32_t reachy_sim_abi_version(void)
{
    return REACHY_SIM_ABI_VERSION;
}

const char* reachy_sim_version_string(void)
{
    return "0.2.0-abi-contract";
}

const char* reachy_sim_status_string(int32_t status)
{
    switch((ReachySimStatus)status)
    {
        case REACHY_SIM_STATUS_OK:
            return "ok";
        case REACHY_SIM_STATUS_INVALID_ARGUMENT:
            return "invalid_argument";
        case REACHY_SIM_STATUS_ABI_MISMATCH:
            return "abi_mismatch";
        case REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH:
            return "struct_size_mismatch";
        case REACHY_SIM_STATUS_MODEL_EMPTY:
            return "model_empty";
        case REACHY_SIM_STATUS_MODEL_TOO_LARGE:
            return "model_too_large";
        case REACHY_SIM_STATUS_ALLOCATION_FAILED:
            return "allocation_failed";
        case REACHY_SIM_STATUS_RESOURCE_EXHAUSTED:
            return "resource_exhausted";
        case REACHY_SIM_STATUS_BACKEND_UNAVAILABLE:
            return "backend_unavailable";
        case REACHY_SIM_STATUS_BACKEND_ERROR:
            return "backend_error";
        case REACHY_SIM_STATUS_INVALID_HANDLE:
            return "invalid_handle";
        case REACHY_SIM_STATUS_STALE_HANDLE:
            return "stale_handle";
        case REACHY_SIM_STATUS_HANDLE_BUSY:
            return "handle_busy";
        case REACHY_SIM_STATUS_BUFFER_TOO_SMALL:
            return "buffer_too_small";
        case REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR:
            return "command_format_error";
        case REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE:
            return "snapshot_incompatible";
        case REACHY_SIM_STATUS_UNSUPPORTED:
            return "unsupported";
        case REACHY_SIM_STATUS_NUMERIC_FAILURE:
            return "numeric_failure";
        default:
            return "unknown_status";
    }
}

uint32_t reachy_sim_status_recoverability(int32_t status)
{
    switch((ReachySimStatus)status)
    {
        case REACHY_SIM_STATUS_OK:
            return REACHY_SIM_RECOVERABILITY_NONE;
        case REACHY_SIM_STATUS_BUFFER_TOO_SMALL:
        case REACHY_SIM_STATUS_HANDLE_BUSY:
            return REACHY_SIM_RECOVERABILITY_RETRY;
        case REACHY_SIM_STATUS_STALE_HANDLE:
        case REACHY_SIM_STATUS_NUMERIC_FAILURE:
            return REACHY_SIM_RECOVERABILITY_RECREATE_HANDLE;
        case REACHY_SIM_STATUS_BACKEND_ERROR:
        case REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE:
            return REACHY_SIM_RECOVERABILITY_RELOAD_MODEL;
        case REACHY_SIM_STATUS_INVALID_ARGUMENT:
        case REACHY_SIM_STATUS_ABI_MISMATCH:
        case REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH:
        case REACHY_SIM_STATUS_MODEL_EMPTY:
        case REACHY_SIM_STATUS_MODEL_TOO_LARGE:
        case REACHY_SIM_STATUS_ALLOCATION_FAILED:
        case REACHY_SIM_STATUS_RESOURCE_EXHAUSTED:
        case REACHY_SIM_STATUS_BACKEND_UNAVAILABLE:
        case REACHY_SIM_STATUS_INVALID_HANDLE:
        case REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR:
        case REACHY_SIM_STATUS_UNSUPPORTED:
        default:
            return REACHY_SIM_RECOVERABILITY_FATAL_CONFIGURATION;
    }
}

ReachySimConfig reachy_sim_default_config(void)
{
    const ReachySimConfig config = {
        REACHY_SIM_ABI_VERSION,
        (uint32_t)sizeof(ReachySimConfig),
        0.002,
        64U,
        0U};
    return config;
}

int32_t reachy_sim_get_capabilities(
    ReachySimCapabilities* capabilities)
{
    if(capabilities == NULL)
    {
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(capabilities->abi_version != REACHY_SIM_ABI_VERSION)
    {
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(capabilities->struct_size != (uint32_t)sizeof(*capabilities))
    {
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }

    capabilities->capability_flags = 0U;
    capabilities->max_model_bytes = REACHY_SIM_MAX_MODEL_BYTES;
    capabilities->max_command_bytes = REACHY_SIM_MAX_COMMAND_BYTES;
    capabilities->max_snapshot_bytes = REACHY_SIM_MAX_SNAPSHOT_BYTES;
    return REACHY_SIM_STATUS_OK;
}

int32_t reachy_sim_get_handle_capabilities(
    ReachySimHandle handle,
    ReachySimCapabilities* capabilities)
{
    if(capabilities == NULL)
    {
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(capabilities->abi_version != REACHY_SIM_ABI_VERSION)
    {
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(capabilities->struct_size != (uint32_t)sizeof(*capabilities))
    {
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }

    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    capabilities->capability_flags =
        lease.slot->backend.capability_flags;
    capabilities->max_model_bytes = REACHY_SIM_MAX_MODEL_BYTES;
    capabilities->max_command_bytes = REACHY_SIM_MAX_COMMAND_BYTES;
    capabilities->max_snapshot_bytes = REACHY_SIM_MAX_SNAPSHOT_BYTES;
    release_slot(lease.slot);
    return REACHY_SIM_STATUS_OK;
}

int32_t reachy_sim_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimHandle* out_handle,
    ReachySimErrorInfo* out_error)
{
    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    ReachySimBackendInstance backend = {0};
    clear_error_info(out_error);

    if(out_handle == NULL)
    {
        write_error_info(
            out_error,
            REACHY_SIM_STATUS_INVALID_ARGUMENT,
            "out_handle is null");
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    *out_handle = REACHY_SIM_INVALID_HANDLE;

    const ReachySimStatus config_status =
        validate_config(config, message, sizeof(message));
    if(config_status != REACHY_SIM_STATUS_OK)
    {
        write_error_info(out_error, config_status, message);
        return (int32_t)config_status;
    }
    if(model_bytes == NULL || model_size == 0U)
    {
        write_error_info(
            out_error,
            REACHY_SIM_STATUS_MODEL_EMPTY,
            "model bytes are empty");
        return REACHY_SIM_STATUS_MODEL_EMPTY;
    }
    if((uint64_t)model_size > REACHY_SIM_MAX_MODEL_BYTES)
    {
        write_error_info(
            out_error,
            REACHY_SIM_STATUS_MODEL_TOO_LARGE,
            "model exceeds configured limit");
        return REACHY_SIM_STATUS_MODEL_TOO_LARGE;
    }

    const ReachySimStatus backend_status = reachy_sim_backend_create(
        model_bytes,
        model_size,
        config,
        &backend,
        message,
        sizeof(message));
    if(backend_status != REACHY_SIM_STATUS_OK)
    {
        write_error_info(out_error, backend_status, message);
        return (int32_t)backend_status;
    }
    if(backend.context == NULL ||
       !backend_operations_complete(backend.operations))
    {
        if(backend.operations != NULL &&
           backend.operations->destroy != NULL)
        {
            backend.operations->destroy(backend.context);
        }
        write_error_info(
            out_error,
            REACHY_SIM_STATUS_BACKEND_ERROR,
            "backend returned an incomplete instance");
        return REACHY_SIM_STATUS_BACKEND_ERROR;
    }

    registry_lock();
    for(uint32_t index = 0U;
        index < REACHY_SIM_MAX_HANDLES;
        ++index)
    {
        ReachySimSlot* const slot = &g_slots[index];
        if(!slot->in_use)
        {
            uint32_t generation = slot->generation + 1U;
            if(generation == 0U)
            {
                generation = 1U;
            }
            slot->generation = generation;
            slot->in_use = true;
            atomic_init(&slot->destroying, false);
            atomic_init(&slot->active_calls, 0U);
            slot->backend = backend;
            clear_error_info(&slot->last_error);
            *out_handle = make_handle(index, generation);
            registry_unlock();
            return REACHY_SIM_STATUS_OK;
        }
    }
    registry_unlock();

    backend.operations->destroy(backend.context);
    write_error_info(
        out_error,
        REACHY_SIM_STATUS_RESOURCE_EXHAUSTED,
        "no simulator handle slots are available");
    return REACHY_SIM_STATUS_RESOURCE_EXHAUSTED;
}

int32_t reachy_sim_destroy(ReachySimHandle handle)
{
    uint32_t slot_index = 0U;
    uint32_t generation = 0U;
    if(!decode_handle(handle, &slot_index, &generation))
    {
        return REACHY_SIM_STATUS_INVALID_HANDLE;
    }

    registry_lock();
    ReachySimSlot* const slot = &g_slots[slot_index];
    if(!slot->in_use || slot->generation != generation)
    {
        registry_unlock();
        return REACHY_SIM_STATUS_STALE_HANDLE;
    }
    if(atomic_load_explicit(
           &slot->active_calls,
           memory_order_acquire) != 0U)
    {
        registry_unlock();
        return REACHY_SIM_STATUS_HANDLE_BUSY;
    }
    atomic_store_explicit(
        &slot->destroying,
        true,
        memory_order_release);
    ReachySimBackendInstance backend = slot->backend;
    slot->in_use = false;
    memset(&slot->backend, 0, sizeof(slot->backend));
    clear_error_info(&slot->last_error);
    registry_unlock();

    backend.operations->destroy(backend.context);
    return REACHY_SIM_STATUS_OK;
}

int32_t reachy_sim_reset(
    ReachySimHandle handle,
    uint32_t reset_id)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->reset(
            lease.slot->backend.context,
            reset_id,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_step(
    ReachySimHandle handle,
    uint32_t step_count)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    if(step_count == 0U)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_INVALID_ARGUMENT,
            "step_count must be positive");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->step(
            lease.slot->backend.context,
            step_count,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_submit_commands(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    if(bytes == NULL ||
       byte_count < sizeof(ReachySimCommandBatchHeader))
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR,
            "command batch is null or smaller than its header");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }
    if((uint64_t)byte_count > REACHY_SIM_MAX_COMMAND_BYTES)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR,
            "command batch exceeds configured limit");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_COMMAND_FORMAT_ERROR;
    }

    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->submit_commands(
            lease.slot->backend.context,
            bytes,
            byte_count,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_copy_state(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    if(required_size == NULL)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_INVALID_ARGUMENT,
            "required_size is null");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->copy_state(
            lease.slot->backend.context,
            bytes,
            byte_capacity,
            required_size,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_apply_wrench(
    ReachySimHandle handle,
    const ReachySimWrenchCommand* command)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    if(command == NULL)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_INVALID_ARGUMENT,
            "wrench command is null");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(command->abi_version != REACHY_SIM_ABI_VERSION)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_ABI_MISMATCH,
            "wrench ABI mismatch");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(command->struct_size != (uint32_t)sizeof(*command))
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH,
            "wrench structure size mismatch");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }

    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->apply_wrench(
            lease.slot->backend.context,
            command,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_copy_snapshot(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    if(required_size == NULL)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_INVALID_ARGUMENT,
            "required_size is null");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }

    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->copy_snapshot(
            lease.slot->backend.context,
            bytes,
            byte_capacity,
            required_size,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_restore_snapshot(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count)
{
    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    if(bytes == NULL ||
       byte_count < sizeof(ReachySimSnapshotHeader))
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE,
            "snapshot is null or smaller than its header");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }
    if((uint64_t)byte_count > REACHY_SIM_MAX_SNAPSHOT_BYTES)
    {
        set_slot_error(
            lease.slot,
            REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE,
            "snapshot exceeds configured limit");
        release_slot(lease.slot);
        return REACHY_SIM_STATUS_SNAPSHOT_INCOMPATIBLE;
    }

    char message[REACHY_SIM_ERROR_MESSAGE_CAPACITY] = {0};
    const ReachySimStatus status =
        lease.slot->backend.operations->restore_snapshot(
            lease.slot->backend.context,
            bytes,
            byte_count,
            message,
            sizeof(message));
    return finish_backend_call(lease.slot, status, message);
}

int32_t reachy_sim_get_last_error(
    ReachySimHandle handle,
    ReachySimErrorInfo* out_error)
{
    if(out_error == NULL)
    {
        return REACHY_SIM_STATUS_INVALID_ARGUMENT;
    }
    if(out_error->abi_version != REACHY_SIM_ABI_VERSION)
    {
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(out_error->struct_size != (uint32_t)sizeof(*out_error))
    {
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }

    ReachySimSlotLease lease = acquire_slot(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease_failure(lease.status);
    }
    *out_error = lease.slot->last_error;
    release_slot(lease.slot);
    return REACHY_SIM_STATUS_OK;
}

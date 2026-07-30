#define reachy_sim_create reachy_sim_create_unserialized
#define reachy_sim_destroy reachy_sim_destroy_unserialized
#define reachy_sim_reset reachy_sim_reset_unserialized
#define reachy_sim_step reachy_sim_step_unserialized
#define reachy_sim_submit_commands reachy_sim_submit_commands_unserialized
#define reachy_sim_copy_state reachy_sim_copy_state_unserialized
#define reachy_sim_apply_wrench reachy_sim_apply_wrench_unserialized
#define reachy_sim_copy_snapshot reachy_sim_copy_snapshot_unserialized
#define reachy_sim_restore_snapshot reachy_sim_restore_snapshot_unserialized
#define reachy_sim_get_last_error reachy_sim_get_last_error_unserialized
#define reachy_sim_get_handle_capabilities reachy_sim_get_handle_capabilities_unserialized

static int32_t reachy_sim_create_unserialized(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimHandle* out_handle,
    ReachySimErrorInfo* out_error);
static int32_t reachy_sim_destroy_unserialized(ReachySimHandle handle);
static int32_t reachy_sim_reset_unserialized(ReachySimHandle handle, uint32_t reset_id);
static int32_t reachy_sim_step_unserialized(ReachySimHandle handle, uint32_t step_count);
static int32_t reachy_sim_submit_commands_unserialized(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count);
static int32_t reachy_sim_copy_state_unserialized(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size);
static int32_t reachy_sim_apply_wrench_unserialized(
    ReachySimHandle handle,
    const ReachySimWrenchCommand* command);
static int32_t reachy_sim_copy_snapshot_unserialized(
    ReachySimHandle handle,
    void* bytes,
    size_t byte_capacity,
    size_t* required_size);
static int32_t reachy_sim_restore_snapshot_unserialized(
    ReachySimHandle handle,
    const void* bytes,
    size_t byte_count);
static int32_t reachy_sim_get_last_error_unserialized(
    ReachySimHandle handle,
    ReachySimErrorInfo* out_error);
static int32_t reachy_sim_get_handle_capabilities_unserialized(
    ReachySimHandle handle,
    ReachySimCapabilities* capabilities);

#include "reachy_sim_unserialized.inc"

#undef reachy_sim_create
#undef reachy_sim_destroy
#undef reachy_sim_reset
#undef reachy_sim_step
#undef reachy_sim_submit_commands
#undef reachy_sim_copy_state
#undef reachy_sim_apply_wrench
#undef reachy_sim_copy_snapshot
#undef reachy_sim_restore_snapshot
#undef reachy_sim_get_last_error
#undef reachy_sim_get_handle_capabilities

#define REACHY_SIM_PUBLIC_OPERATION_SLOTS 128U

typedef struct ReachySimPublicOperation {
    ReachySimHandle handle;
    bool active;
} ReachySimPublicOperation;

typedef struct ReachySimPublicLease {
    size_t index;
    ReachySimStatus status;
} ReachySimPublicLease;

static ReachySimPublicOperation
    g_public_operations[REACHY_SIM_PUBLIC_OPERATION_SLOTS];
static atomic_flag g_public_operation_lock = ATOMIC_FLAG_INIT;

static void public_operation_lock(void)
{
    while(atomic_flag_test_and_set_explicit(
        &g_public_operation_lock,
        memory_order_acquire))
    {
    }
}

static void public_operation_unlock(void)
{
    atomic_flag_clear_explicit(
        &g_public_operation_lock,
        memory_order_release);
}

static ReachySimStatus validate_live_handle(ReachySimHandle handle)
{
    uint32_t slot_index = 0U;
    uint32_t generation = 0U;
    if(!decode_handle(handle, &slot_index, &generation))
    {
        return REACHY_SIM_STATUS_INVALID_HANDLE;
    }

    registry_lock();
    const ReachySimSlot* const slot = &g_slots[slot_index];
    const ReachySimStatus status =
        slot->in_use && slot->generation == generation
            ? REACHY_SIM_STATUS_OK
            : REACHY_SIM_STATUS_STALE_HANDLE;
    registry_unlock();
    return status;
}

static ReachySimPublicLease acquire_public_operation(ReachySimHandle handle)
{
    ReachySimPublicLease lease = {
        SIZE_MAX,
        validate_live_handle(handle)};
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return lease;
    }

    lease.status = REACHY_SIM_STATUS_HANDLE_BUSY;
    public_operation_lock();
    size_t free_index = SIZE_MAX;
    for(size_t index = 0U;
        index < REACHY_SIM_PUBLIC_OPERATION_SLOTS;
        ++index)
    {
        const ReachySimPublicOperation* const operation =
            &g_public_operations[index];
        if(operation->active && operation->handle == handle)
        {
            public_operation_unlock();
            return lease;
        }
        if(!operation->active && free_index == SIZE_MAX)
        {
            free_index = index;
        }
    }
    if(free_index != SIZE_MAX)
    {
        g_public_operations[free_index].handle = handle;
        g_public_operations[free_index].active = true;
        lease.index = free_index;
        lease.status = REACHY_SIM_STATUS_OK;
    }
    public_operation_unlock();
    return lease;
}

static void release_public_operation(ReachySimPublicLease lease)
{
    if(lease.status != REACHY_SIM_STATUS_OK ||
       lease.index >= REACHY_SIM_PUBLIC_OPERATION_SLOTS)
    {
        return;
    }
    public_operation_lock();
    g_public_operations[lease.index].active = false;
    g_public_operations[lease.index].handle = REACHY_SIM_INVALID_HANDLE;
    public_operation_unlock();
}

static int32_t public_busy_or_call(
    ReachySimHandle handle,
    int32_t (*operation)(ReachySimHandle))
{
    ReachySimPublicLease lease = acquire_public_operation(handle);
    if(lease.status != REACHY_SIM_STATUS_OK)
    {
        return (int32_t)lease.status;
    }
    const int32_t status = operation(handle);
    release_public_operation(lease);
    return status;
}

static ReachySimStatus validate_create_error_output(
    const ReachySimErrorInfo* error)
{
    if(error == NULL)
    {
        return REACHY_SIM_STATUS_OK;
    }
    if(error->abi_version != REACHY_SIM_ABI_VERSION)
    {
        return REACHY_SIM_STATUS_ABI_MISMATCH;
    }
    if(error->struct_size != (uint32_t)sizeof(*error))
    {
        return REACHY_SIM_STATUS_STRUCT_SIZE_MISMATCH;
    }
    return REACHY_SIM_STATUS_OK;
}

static ReachySimStatus validate_copy_result(
    int32_t status,
    const void* bytes,
    size_t byte_capacity,
    size_t required_size)
{
    if(status == REACHY_SIM_STATUS_OK)
    {
        if(bytes == NULL || required_size == 0U ||
           byte_capacity < required_size)
        {
            return REACHY_SIM_STATUS_BACKEND_ERROR;
        }
    }
    else if(status == REACHY_SIM_STATUS_BUFFER_TOO_SMALL)
    {
        if(required_size == 0U ||
           (bytes != NULL && byte_capacity >= required_size))
        {
            return REACHY_SIM_STATUS_BACKEND_ERROR;
        }
    }
    return (ReachySimStatus)status;
}

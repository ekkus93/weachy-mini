#include "reachy_sim_backend.h"
#include "reachy_sim_blocking_backend.h"

#include <stdatomic.h>
#include <stdbool.h>

#define fake_step fake_step_uncontrolled
#define FAKE_OPERATIONS FAKE_OPERATIONS_UNCONTROLLED
#define reachy_sim_backend_create reachy_sim_backend_create_uncontrolled

ReachySimStatus reachy_sim_backend_create_uncontrolled(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimBackendInstance* out_backend,
    char* error,
    size_t error_size);

#include "reachy_sim_fake_backend_base.inc"

#undef fake_step
#undef FAKE_OPERATIONS
#undef reachy_sim_backend_create

static atomic_bool g_test_step_blocked;
static atomic_bool g_test_step_entered;

void reachy_sim_blocking_backend_reset_controls(void)
{
    atomic_store_explicit(
        &g_test_step_entered,
        false,
        memory_order_release);
    atomic_store_explicit(
        &g_test_step_blocked,
        false,
        memory_order_release);
}

void reachy_sim_blocking_backend_set_step_blocked(bool blocked)
{
    atomic_store_explicit(
        &g_test_step_blocked,
        blocked,
        memory_order_release);
}

bool reachy_sim_blocking_backend_step_entered(void)
{
    return atomic_load_explicit(
        &g_test_step_entered,
        memory_order_acquire);
}

static ReachySimStatus fake_step(
    void* context,
    uint32_t step_count,
    char* error,
    size_t error_size)
{
    atomic_store_explicit(
        &g_test_step_entered,
        true,
        memory_order_release);
    while(atomic_load_explicit(
        &g_test_step_blocked,
        memory_order_acquire))
    {
        atomic_signal_fence(memory_order_seq_cst);
    }
    return fake_step_uncontrolled(
        context,
        step_count,
        error,
        error_size);
}

static const ReachySimBackendOperations FAKE_OPERATIONS = {
    fake_destroy,
    fake_reset,
    fake_step,
    fake_submit_commands,
    fake_copy_state,
    fake_apply_wrench,
    fake_copy_snapshot,
    fake_restore_snapshot};

ReachySimStatus reachy_sim_backend_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimBackendInstance* out_backend,
    char* error,
    size_t error_size)
{
    const ReachySimStatus status = reachy_sim_backend_create_uncontrolled(
        model_bytes,
        model_size,
        config,
        out_backend,
        error,
        error_size);
    if(status == REACHY_SIM_STATUS_OK)
    {
        out_backend->operations = &FAKE_OPERATIONS;
    }
    return status;
}

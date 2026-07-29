#include "reachy_sim_backend.h"

#include <stdio.h>
#include <string.h>

ReachySimStatus reachy_sim_backend_create(
    const uint8_t* model_bytes,
    size_t model_size,
    const ReachySimConfig* config,
    ReachySimBackendInstance* out_backend,
    char* error,
    size_t error_size)
{
    (void)model_bytes;
    (void)model_size;
    (void)config;
    if(out_backend != NULL)
    {
        memset(out_backend, 0, sizeof(*out_backend));
    }
    if(error != NULL && error_size > 0U)
    {
        (void)snprintf(
            error,
            error_size,
            "%s",
            "MuJoCo backend is not linked; simulation startup is unavailable");
    }
    return REACHY_SIM_STATUS_BACKEND_UNAVAILABLE;
}

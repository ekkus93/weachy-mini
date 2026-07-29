#include "reachy_sim.h"

#include <stdint.h>
#include <stdio.h>
#include <string.h>

int main(void)
{
    static const uint8_t MODEL[] = "unavailable-backend-test";
    ReachySimConfig config = reachy_sim_default_config();
    ReachySimHandle handle = UINT64_C(99);
    ReachySimErrorInfo error = {0};
    error.abi_version = REACHY_SIM_ABI_VERSION;
    error.struct_size = (uint32_t)sizeof(error);

    const int32_t status = reachy_sim_create(
        MODEL,
        sizeof(MODEL),
        &config,
        &handle,
        &error);
    if(status != REACHY_SIM_STATUS_BACKEND_UNAVAILABLE)
    {
        (void)fprintf(
            stderr,
            "Expected backend_unavailable, received %s.\n",
            reachy_sim_status_string(status));
        return 1;
    }
    if(handle != REACHY_SIM_INVALID_HANDLE)
    {
        (void)fprintf(
            stderr,
            "%s\n",
            "Unavailable backend returned a live handle.");
        return 1;
    }
    if(error.status != REACHY_SIM_STATUS_BACKEND_UNAVAILABLE ||
       strstr(error.message, "not linked") == NULL)
    {
        (void)fprintf(
            stderr,
            "%s\n",
            "Unavailable backend did not provide explicit diagnostics.");
        return 1;
    }
    return 0;
}

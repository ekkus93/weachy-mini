#include "reachy_sim_version.h"

#include <stdint.h>
#include <stdio.h>
#include <string.h>

int main(void)
{
    const uint32_t actual_abi =
        reachy_sim_scaffold_abi_version();
    const char* const version =
        reachy_sim_scaffold_version_string();

    if(actual_abi != REACHY_SIM_SCAFFOLD_ABI_VERSION)
    {
        (void)fprintf(
            stderr,
            "ABI version mismatch: %u\n",
            actual_abi);
        return 1;
    }

    if(version == NULL ||
       strcmp(version, "0.3.0-deterministic-snapshot") != 0)
    {
        (void)fprintf(
            stderr,
            "%s\n",
            "Unexpected simulation version string.");
        return 1;
    }

    return 0;
}

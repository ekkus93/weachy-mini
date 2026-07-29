#include "reachy_sim_version.h"

uint32_t reachy_sim_scaffold_abi_version(void)
{
    return reachy_sim_abi_version();
}

const char* reachy_sim_scaffold_version_string(void)
{
    return reachy_sim_version_string();
}

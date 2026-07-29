#ifndef REACHY_SIM_VERSION_H
#define REACHY_SIM_VERSION_H

#include "reachy_sim.h"

#ifdef __cplusplus
extern "C" {
#endif

enum {
    REACHY_SIM_SCAFFOLD_ABI_VERSION = REACHY_SIM_ABI_VERSION
};

uint32_t reachy_sim_scaffold_abi_version(void);
const char* reachy_sim_scaffold_version_string(void);

#ifdef __cplusplus
}
#endif

#endif

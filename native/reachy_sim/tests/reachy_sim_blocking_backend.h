#ifndef REACHY_SIM_BLOCKING_BACKEND_H
#define REACHY_SIM_BLOCKING_BACKEND_H

#include <stdbool.h>

void reachy_sim_blocking_backend_reset_controls(void);
void reachy_sim_blocking_backend_set_step_blocked(bool blocked);
void reachy_sim_blocking_backend_fail_next_step_numeric(void);
bool reachy_sim_blocking_backend_step_entered(void);

#endif

#include "reachy_sim_backend_mujoco/types_and_validation.inc"
#include "reachy_sim_backend_mujoco/model_and_reset.inc"
#define mju_zero(values, count) reachy_mujoco_zero((values), (count))
#include "reachy_sim_backend_mujoco/step_commands_wrench.inc"
#undef mju_zero
#include "reachy_sim_backend_mujoco/snapshots.inc"
#include "reachy_sim_backend_mujoco/create.inc"

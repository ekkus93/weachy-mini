#include "reachy_sim_mujoco_backend_test/part1.inc"
#include "reachy_sim_mujoco_backend_test/part2.inc"
#include "reachy_sim_mujoco_backend_test/part_state_payload.inc"
#define main reachy_sim_mujoco_existing_main
#include "reachy_sim_mujoco_backend_test/part3.inc"
#undef main

int main(void)
{
    test_authoritative_state_payload();
    return reachy_sim_mujoco_existing_main();
}

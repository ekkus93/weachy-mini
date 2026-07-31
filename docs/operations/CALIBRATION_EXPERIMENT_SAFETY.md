# Physical Calibration Experiment Safety

RMA-072 experiment plans can describe motions that may damage a robot, strike
nearby objects, pinch fingers, overheat actuators, or destabilize the mechanism.
A compiled dry run is not permission to move hardware.

## Required operator controls

Before every physical run:

1. Place the robot on a stable surface with the full motion envelope clear.
2. Keep people, pets, cables, tools, and loose objects outside that envelope.
3. Verify the emergency-stop or immediate torque-disable path on the connected
   unit before enabling experiment motion.
4. Keep a trained operator within immediate reach for the entire run.
5. Confirm that the plan's robot ID, hardware revision, firmware constraint,
   actuator map, soft limits, and electrical limits match the connected unit.
6. Start with conservative amplitudes, rates, repetitions, and temperature
   ceilings. Increase only after reviewing the previous run.
7. Use external supports or fixtures only when their geometry, load direction,
   and collision clearance are documented.
8. Stop immediately for unexpected noise, binding, vibration, odor, heat,
   communication loss, voltage collapse, excessive current, contact, or motion.
9. Do not leave warm/cold or multi-actuator tests unattended.
10. Preserve aborted-run evidence. Never relabel an incomplete or
    unsynchronized run as successful.

## Free-decay precautions

Torque-disabled tests can allow the head, platform, body yaw, or antennas to
move under gravity or stored elastic energy. Support the mechanism without
placing hands in a pinch path. Confirm that disabling torque cannot cause a
drop into the fixture, table, cables, or operator.

## Gravity-loaded and multi-actuator precautions

Static poses and simultaneous commands can create larger combined current,
voltage sag, structural load, and collision risk than single-axis tests. Use
the plan-wide current and concurrency limits and monitor the common supply,
not only per-actuator telemetry.

## Warm/cold test precautions

Temperature limits are hard abort thresholds, not targets. Allow sufficient
cooldown, verify sensor placement and plausibility, and treat missing,
non-finite, stale, or implausible temperature data as a failure. Never bypass
thermal shutdown to complete a dataset.

## Execution authorization

The library requires the exact acknowledgement:

`RMA-072 PHYSICAL MOTION AUTHORIZED`

This acknowledgement is only one interlock. It does not replace inspection,
workspace clearance, emergency-stop verification, live telemetry, or operator
judgment.

## Evidence boundary

The committed RMA-072 fixture is synthetic and exercises orchestration only.
Physical execution and admissible calibration data remain RMA-074 work.

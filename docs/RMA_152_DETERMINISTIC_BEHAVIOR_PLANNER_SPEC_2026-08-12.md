# RMA-152 — Deterministic behavior planner specification — 2026-08-12

## Status

Implemented on `master`. Managed compilation/CI remains the closure gate.

## Purpose

RMA-152 converts an already validated RMA-151 `ReachyBehaviorIntent` into a deterministic,
bounded sequence of high-level position-controller targets. It is the safety boundary between
AI-authored behavior intent and the existing controller/servo/MuJoCo path.

The planner must never accept or synthesize raw LLM/VLM joint, torque, velocity, MuJoCo-state,
or native-command writes.

## Inputs

The planner consumes only:

1. a validated `ReachyBehaviorIntent`;
2. an immutable RMA-112 `WorldModelSnapshot` when gaze is requested;
3. an authoritative nine-actuator position/velocity snapshot;
4. explicit safety interlocks for controller availability, workspace clearance, active fault,
   contact/collision, hard stop, and load-limit state;
5. an explicit planning timestamp and cancellation token.

No system clock, random generator, hidden fallback provider, stale world-model reuse, or implicit
motion state is permitted.

## World-model gaze resolution

A tracked gaze target is executable only when all of the following hold:

- the world-model snapshot is no more than one second old and is not from the future;
- the entity ID exists exactly;
- the entity is `CurrentlyVisible` rather than merely recently seen;
- confidence is at least `0.65`;
- transformed-image valid coverage is at least `0.50`;
- coverage state is `Normal` or `Degraded`;
- `ShouldStopVisionDrivenTurning` is false.

Each failure has a distinct fail-visible planner status. No different entity is substituted.

## Motion envelope

The authoritative policy is `models/reachy-mini/behavior-planner-policy.json` with contract ID
`rma152_deterministic_behavior_planner_v2`.

Position limits are bound to the RMA-065 soft command envelopes:

- body yaw and all six Stewart actuators use the pinned joint ranges inset by `0.015` rad;
- both antennas use the RMA-065 `[-3.05, 3.05]` engineering soft range.

Planner velocity and acceleration limits are deliberately conservative engineering limits and are
not calibration claims. A requested `maximum_duration_ms` is never allowed to compress motion
below those limits. If a requested duration is too short, planning fails.

The planner does not merely delay a large position-target jump. Each motion segment is expanded
into a fixed 50 ms setpoint stream using cubic smoothstep interpolation. The segment duration is
chosen from the smoothstep peak-velocity factor (1.5) and peak-acceleration factor (6.0), rounded
up to the command cadence. The resulting scheduled target stream is bounded to 128 frames and
remains inside the same soft position envelope. This is an open-loop setpoint-slew contract; the
normal servo/MuJoCo path remains authoritative for actual physical state and RMA-154 later adds
closed-loop visual feedback.

## Deterministic pose mapping

RMA-152 provides a small deterministic baseline only:

- gaze horizontal error is split between bounded body yaw and a bounded Stewart yaw component;
- gaze vertical error maps to a bounded Stewart pitch component;
- expressions add small bounded Stewart/antenna offsets;
- `nod`, `small_head_tilt`, and `recoil` expand to fixed parameterized keyframe sequences;
- every plan starts from the supplied authoritative actuator state;
- each segment is sampled as a cubic smoothstep trajectory at a fixed 50 ms command cadence;
- segment duration is the conservative maximum of the minimum segment time, smoothstep peak
  velocity requirement, and smoothstep peak acceleration requirement;
- no segment is represented as one delayed final-target step.

The Stewart basis is explicitly labeled an engineering estimate. It is not a calibrated inverse
kinematics claim. RMA-154 later closes the gaze loop using transformed-image feedback.

## Safety interlocks

No motion plan is produced when any of these conditions is active:

- normal controller path unavailable;
- workspace not explicitly cleared;
- MuJoCo warning or unknown authoritative health flag;
- active contact/collision;
- contact overload/load-limit signal;
- hard-stop signal;
- authoritative position outside the RMA-152 soft envelope;
- authoritative velocity already above the planner envelope.

Speech-only intents remain valid when motion is interlocked because suppressing speech would be an
unrelated silent behavior fallback.

The managed authoritative-state adapter maps canonical actuator observations, health flags, and
contact count into the planner motion/safety snapshots. Unknown health bits fail closed.

## Cancellation and safe rest

A cancelled normal plan returns `Cancelled` with no fabricated replacement motion and the explicit
diagnostic `cancelled-safe-rest-replan-required`. `ReachyBehaviorTrajectoryExecutor` also observes
cancellation between trajectory frames. Once cancellation is observed, it submits no later frames.
A controller queue/unavailable/contract rejection stops execution immediately and is never retried.

Safe rest is a separate `PlanSafeRest` operation using a fresh authoritative motion/safety snapshot.
It creates a bounded position trajectory to the neutral nine-actuator target. This avoids assuming
that the robot remains at the position predicted by a now-cancelled plan.

## Production routing

`ReachyBehaviorTrajectoryExecutor` consumes the deterministic frame offsets asynchronously and
submits each accepted frame exactly once through an `IReachyBehaviorControllerTargetSink`. It has
no fallback, retry, or implicit safe-rest behavior.

`ReachyProductionBehaviorControllerTargetSink` is the only RMA-152 production submission adapter.
It converts a planner frame to a temporary nine-value target array and calls
`ReachyProductionAuthoritativeRuntime.SubmitPositionTargets`.

The sink does not reference `NativeReachySim`, `ReachySimSession`, `SubmitCommandsRaw`, direct
`ReachySimulationCommandBatch` construction, torque mode, or MuJoCo state. Therefore the existing
bounded command queue, controller timing, servo models, power/thermal model, collision/hard-stop
model, and authoritative MuJoCo worker remain in the command path.

## Explicit limitations

- RMA-152 does not claim calibrated head inverse kinematics.
- RMA-152 does not implement a second predictive collision engine; it uses conservative envelopes
  and authoritative RMA-065 interlocks.
- RMA-152 does not perform closed-loop visual servoing; that is RMA-154.
- RMA-152 does not expand the full baseline behavior library; that is RMA-153.
- RMA-152 does not silently wake a sleeping simulation. Wake/rest sequencing remains explicit.

## Closure requirements

RMA-152 may be marked complete only after:

1. the managed RMA-152 contract fixture compiles warnings-as-errors and passes;
2. static/source-set tests pass without lint suppression;
3. the production sink compiles in the Unity/Android assembly;
4. no new direct native/MuJoCo/raw-torque command path exists;
5. the implementation is present on `master`.

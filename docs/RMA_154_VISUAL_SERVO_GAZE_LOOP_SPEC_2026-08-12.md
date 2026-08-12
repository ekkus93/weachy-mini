# RMA-154 visual-servo gaze loop specification — 2026-08-12

## Scope

RMA-154 closes the gaze-acquisition loop that RMA-152 intentionally left open. It consumes only
current transformed-image tracking observations and commands pure gaze-only corrections through the
existing RMA-152 deterministic planner, trajectory executor, and production position-controller path.

The loop is feedback driven. A requested target, a submitted controller setpoint, or successful
trajectory submission is never evidence that the robot physically moved or that the camera view
changed.

## Authoritative feedback contract

Each `ReachyVisualServoFeedbackSample` binds five observations:

- a positive feedback timestamp;
- the latest authoritative MuJoCo state sequence;
- a bounded world-model snapshot;
- the RMA-152 motion snapshot derived from that authoritative state;
- the RMA-152 safety snapshot derived from that authoritative state.

A tracked entity's `ReachyVisionFrameIdentity` must not claim a timestamp or authoritative sequence
newer than the paired feedback sample. Production feedback is captured read-only from
`ReachyProductionAuthoritativeRuntime` and `BoundedWorldModel`.

The visual error is calculated only from transformed normalized image bounds:

- horizontal error = `Bounds.CenterX - 0.5`;
- vertical error = `Bounds.CenterY - 0.5`.

The loop does not infer success from planner targets, desired poses, queue acceptance, or command
completion.

## Closed-loop iteration

For each visible off-center target:

1. Evaluate current authoritative safety and transformed-image coverage.
2. Build a pure gaze-only RMA-151 intent for the exact entity with no expression, gesture, speech, or
   timing side effect.
3. Let the RMA-152 planner resolve the exact entity and create a mechanically bounded position-target
   trajectory from the fresh authoritative motion snapshot.
4. Execute that trajectory through `ReachyBehaviorTrajectoryExecutor`; in production its target
   sink remains `ReachyProductionBehaviorControllerTargetSink` and therefore
   `ReachyProductionAuthoritativeRuntime.SubmitPositionTargets`.
5. Record the pre-command transformed frame identity, authoritative state sequence, and physical
   actuator positions.
6. Poll authoritative feedback until an RMA-152-controlled body/head actuator has actually changed
   by at least the configured physical-motion threshold.
7. After physical motion has been observed, require a newer transformed frame from the same camera,
   source session, and continuity ID whose authoritative sequence is at least the sequence at which
   physical motion was observed.
8. Re-evaluate the transformed-image tracking error and either stop within tolerance or perform the
   next bounded iteration.

This two-proof gate prevents a newly submitted target, a repeated frame, or a frame generated before
observed physical motion from unlocking the next iteration.

## Safety and fail-closed stop conditions

The loop stops without issuing another adjustment when any of these conditions occurs:

- transformed-image horizontal and vertical errors are both inside tolerance (`Centered`);
- exact target is absent or no longer currently visible (`TargetLost`);
- transformed-image coverage is outside the allowed normal/degraded bounded region
  (`CoverageBlocked`);
- authoritative load-limit state is active (`LoadLimit`);
- another authoritative RMA-152 safety interlock prevents motion (`SafetyInterlock`);
- caller cancellation is requested (`Cancelled`);
- the loop duration or iteration budget is exhausted (`TimedOut`);
- RMA-152 planning rejects the request (`PlannerRejected` or the more specific mapped
  target/coverage/safety result);
- trajectory execution rejects a controller submission (`ExecutionRejected`);
- transformed camera continuity changes or authoritative/frame sequence/timestamp regresses
  (`FrameDiscontinuity`);
- production authoritative feedback cannot be captured (`FeedbackUnavailable`).

There is no automatic provider switch, retry through another controller, direct native submission,
raw joint command, torque command, or silent success fallback.

## Determinism

The controller contains no random or wall-clock-derived control choice. Given the same policy,
entity ID, world-model snapshots, authoritative motion/safety snapshots, and frame sequence, it
produces the same RMA-152 trajectory submissions and stop result. Wall-clock time is used
only by the explicit cancellation timeout and polling delay.

## Machine-readable policy

`models/reachy-mini/visual-servo-gaze-policy-v1.json` is the machine-readable RMA-154 policy. Its
contract ID is `rma154_visual_servo_gaze_v1` and its numeric values must match
`ReachyVisualServoPolicy.CreateMobileDefault()`.

The initial values are conservative engineering defaults rather than calibration claims:

- horizontal tolerance: 0.06 normalized image units;
- vertical tolerance: 0.06 normalized image units;
- minimum valid coverage fraction: 0.50;
- minimum observed physical actuator motion: 1e-5 rad;
- feedback poll delay: 20 ms;
- maximum adjustments: 8;
- maximum loop duration: 15 s.

## Production boundary

`ReachyProductionVisualServoFeedbackSource` is read-only. The only RMA-154 command route is the
already established RMA-152 high-level position target route. The production feedback adapter does
not reference `NativeReachySim`, `ReachySimSession`, `SubmitCommandsRaw`, torque mode, or direct
MuJoCo mutation.

`ReachyProductionAuthoritativeRuntime` exposes two new read-only helpers for allocating and copying
the worker's published authoritative state frame. These helpers do not enqueue commands.

## Acceptance behavior

The integrated acceptance scenario is:

1. a tracked face begins near a transformed-image edge;
2. RMA-154 submits a bounded gaze adjustment;
3. MuJoCo physically changes authoritative body/head state;
4. a subsequent transformed camera frame is generated from an authoritative sequence at or after
   that observed motion;
5. the tracker/world model reports the new transformed-image location;
6. RMA-154 re-plans from the fresh authoritative state;
7. the face converges into the configured center tolerance or the loop stops explicitly.

A managed deterministic contract fixture simulates this causal sequence and separately proves that
a fresh-looking frame with no physical actuator motion cannot unlock another adjustment. The final
Unity/MuJoCo behavior gate must still execute the real integrated path.

## Closure requirements

RMA-154 may be marked complete only when:

1. the managed RMA-154 contract fixture compiles warnings-as-errors and passes;
2. focused/static source and policy checks pass without suppression;
3. Unity/Android compilation accepts both the core loop and production feedback adapter;
4. the integrated Unity/MuJoCo acceptance demonstrates edge target -> actual simulated motion ->
   post-motion transformed frame -> reduced tracking error/centering;
5. no new direct native/raw-joint/raw-torque command path exists;
6. replay of the same scripted observation stream produces identical submitted trajectories;
7. the implementation and validation evidence are present on `master`.

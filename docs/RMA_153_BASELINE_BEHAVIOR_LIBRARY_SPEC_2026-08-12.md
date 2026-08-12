# RMA-153 — Baseline behavior library specification — 2026-08-12

## Status

Implemented candidate on `master`. Managed warnings-as-errors and Unity/Android compilation remain
the closure gates.

## Purpose

RMA-153 defines the deterministic first-release behavior catalog required by section 16.4 of the
Reachy Mini Android digital-twin specification. It is a recipe library over the RMA-152 planner,
not a second motion controller. No RMA-153 API emits raw joint, torque, native, or MuJoCo writes.

## Catalog

The library exposes explicit requests for:

- neutral idle micro-motion;
- listening posture;
- speaking motion driven by an external timing pulse or normalized audio energy;
- acknowledgment/nod;
- curiosity/head tilt;
- surprise/recoil;
- exact-entity gaze acquisition and centering;
- bounded gaze-loss search for the exact recently-seen entity;
- unavailable/error expression;
- safe sleep/rest sequencing;
- wake sequencing.

Every request is immutable and validated before planning. Gaze IDs use the same bounded canonical
`entity-[0-9]+` form as RMA-151. Audio energy is finite and constrained to `[0, 1]`.

## Planner ownership

`ReachyBaselineBehaviorLibrary` delegates all motion to
`ReachyDeterministicBehaviorPlanner.PlanBaseline`. Standard gestures reuse the RMA-152 expression,
gesture, and exact gaze primitives. Custom idle, speaking, search, and wake recipes still terminate
in the existing RMA-152 `BuildTrajectory` or `PlanSafeRest` path.

Therefore every expressive target remains subject to:

- current authoritative motion-state validation;
- controller/workspace/fault/contact/hard-stop/load interlocks;
- RMA-065-derived actuator soft envelopes;
- fixed 50 ms setpoint cadence;
- RMA-152 velocity and acceleration limits;
- trajectory frame and duration budgets;
- cancellation and controller submission failure;
- the existing normal position-controller/servo/MuJoCo execution path.

If a source pose is too close to a mechanical envelope for a requested expression, planning fails.
The behavior is not silently clipped or substituted.

## Idle and speaking

Idle is a deterministic inhale/source/exhale/source micro-cycle. It always returns to the supplied
authoritative source pose so repeated completed cycles do not accumulate drift.

Speaking supports two explicit drives:

1. `SpeakingFromTiming()` — one deterministic conservative speaking pulse. The caller is responsible
   for triggering pulses from speech timing events; RMA-153 does not fabricate phoneme/audio timing.
2. `SpeakingFromAudioEnergy(energy)` — the same bounded micro-cycle with amplitude scaled by a
   validated normalized energy value in `[0, 1]`.

Zero audio energy produces a successful no-motion pulse. Both speaking modes return to the supplied
source pose.

## Gaze acquisition

Gaze acquisition uses the existing RMA-152 exact entity resolver. The world-model snapshot must be
fresh; the exact entity must exist, be currently visible, meet the RMA-152 confidence threshold,
and have transformed-image coverage that permits turning. No alternate entity is selected.

## Gaze loss and bounded search

A search request contains:

- the exact canonical entity ID;
- a timestamp for the current coverage observation;
- the current transformed-image `WorldCoverageContext`.

Search is permitted only when:

- the world-model snapshot is fresh under the RMA-152 age bound;
- the current coverage timestamp is positive, not from the future, and fresh under the same bound;
- the exact entity still exists in the world model but is `RecentlySeen` rather than visible;
- its last-seen age is at most 2 seconds;
- confidence is at least 0.35;
- current coverage is `Normal` or `Degraded`, valid fraction meets the RMA-152 threshold, and
  `ShouldStopVisionDrivenTurning` is false.

The stored coverage on the last-seen entity does not authorize search. This prevents stale historical
coverage from becoming a fail-open turning permission.

Search centers on the last known direction within conservative body/head bounds, performs a bounded
left/right sweep, and returns to the original authoritative source pose. A currently visible entity
is rejected with an explicit instruction to use acquisition instead.

## Sleep/rest and wake

Sleep and wake use the existing simulator reset semantics rather than cosmetic approximations:

- `EnterSleepRest` maps to `ReachySimResetPose.SleepRest`;
- `WakeNeutral` maps to `ReachySimResetPose.NeutralAwake`.

Sleep first plans the normal RMA-152 safe-rest trajectory. `EnterSleepRest` is released only after a
completed trajectory execution result whose submitted-frame count matches the planned safe-rest
trajectory. Planning failure, cancellation, controller rejection, or a mismatched completed
execution never authorizes the sleep reset.

Wake declares `WakeNeutral` as a pre-planning lifecycle action. After that reset the caller must
obtain a fresh authoritative state. The expressive wake phase is rejected unless all nine positions
and velocities are within the configured neutral tolerances, preventing a stale pre-reset snapshot
from being treated as a successful wake.

## Deterministic policy

`models/reachy-mini/baseline-behavior-library-v1.json` is the machine-readable parameter contract.
`ReachyBaselineBehaviorPolicy.CreateMobileDefault()` carries the same values, and static tests bind
the two representations.

The parameters are engineering estimates, not calibration claims. RMA-152 remains authoritative for
mechanical constraints.

## Closure gates

RMA-153 may be marked complete only when:

1. the managed RMA-153 contract matrix compiles warnings-as-errors and passes;
2. static/source-set checks pass without suppression;
3. Unity/Android compilation accepts the new core behavior source set;
4. no direct native/raw-torque/MuJoCo command path exists;
5. the implementation and evidence are on `master`.

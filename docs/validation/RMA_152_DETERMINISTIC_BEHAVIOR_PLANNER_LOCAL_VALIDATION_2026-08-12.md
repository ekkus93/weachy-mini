# RMA-152 deterministic behavior planner — local validation — 2026-08-12

## Scope

Local pre-CI validation of the RMA-152 implementation candidate against the refactored `master`
source layout supplied on 2026-08-12. Current GitHub RMA-151 contracts were used as the planner
boundary.

## Implemented contracts

- provider-neutral RMA-151 intent consumption;
- current-world-model gaze target resolution;
- stale, missing, expired/not-visible, low-confidence, and coverage-blocked rejection;
- fixed nine-actuator canonical order;
- RMA-065-bound soft position limits;
- conservative velocity and acceleration envelope;
- cubic-smoothstep position-target slew at a fixed 50 ms cadence rather than delayed target steps;
- explicit 128-frame / 6.4-second representable trajectory budget;
- deterministic expression and gesture trajectories relative to the fresh authoritative pose;
- explicit body-yaw, Stewart, and antenna coordination;
- authoritative contact/hard-stop/load/warning safety mapping;
- explicit workspace/controller interlocks;
- planning and in-flight trajectory cancellation with no fabricated replacement motion;
- fail-visible controller submission rejection with no automatic retry;
- fresh-state safe-rest replanning;
- production routing only through `ReachyProductionAuthoritativeRuntime.SubmitPositionTargets`.

## Local results

Post-hardening focused static contract suite:

```text
python3 -m unittest -v scripts.tests.test_rma152_behavior_planner
Ran 13 tests
OK
```

The focused suite verifies source-set completeness, gaze fail-closed behavior, authoritative-pose
relative planning, safety interlocks, fixed-cadence smoothstep setpoint slew, frame-budget bounds,
velocity/acceleration timing policy, planning and execution cancellation, no-retry controller
failure behavior, RMA-065 position-range provenance, runtime/machine-policy drift, absence of
nondeterministic clocks/randomness, and the production controller path.

Before this setpoint-slew hardening, the same supplied refactored snapshot completed the broader
script discovery at 272/272. After the hardening, the focused RMA-152 suite and Python compilation
were rerun successfully. A repeat of the entire repository discovery exceeded this sandbox's
execution window, so this record does not relabel that prior 272/272 run as post-hardening evidence.

`python3 -m compileall -q scripts` completed without a Python syntax failure after the hardening.

The supplied ZIP predates the later `master` cleanup of an existing
`scripts/calibration_fitting_jsonio.py` `type: ignore`; that stale local copy was not changed or
republished by RMA-152.

## Managed validation limitation

This sandbox has no `dotnet`, `csc`, `mcs`, or `msbuild`. Therefore
`Rma152DeterministicBehaviorPlannerContractTests.cs` cannot be executed locally. The managed
fixture covers:

- successful high-confidence current gaze resolution;
- rejection of missing, recently-seen/expired, low-confidence, coverage-blocked, and stale targets;
- deterministic repeated trajectory generation;
- intermediate setpoint-slew frames instead of one delayed position step;
- scheduled position, velocity, acceleration, cadence, and endpoint-hold envelope checks;
- controller/workspace/fault/contact/hard-stop/load interlocks;
- speech-only operation while motion is interlocked;
- authoritative-state motion/safety mapping;
- rejection of timing requests that would require unsafe motion;
- cancellation requiring fresh safe-rest planning;
- in-flight cancellation stopping later frame submission;
- controller rejection stopping without retry;
- preservation of unrelated authoritative actuator state during expression/gaze planning;
- bounded neutral safe-rest targets, including full-soft-envelope endpoint recovery.

Managed warnings-as-errors/Unity compilation and CI remain required before the roadmap checkbox is
closed.

## RMA-154 physical-loop sign correction

RMA-154 integration review found that the original managed gaze fixture asserted a right-side image
target should produce **positive** `yaw_body`. That was internally deterministic but physically inverted
for the pinned model/camera convention: the RMA-101 optical basis has image-right along neutral world
`-Y`, while the pinned MuJoCo `yaw_body` hinge is `+Z`, so positive body yaw turns the robot forward
axis toward world `+Y`.

The planner now negates the horizontal transformed-image angle before splitting it between body yaw
and the Stewart yaw estimate. The lost-target search-center helper uses the same sign. The managed
fixture was corrected to require negative physical body yaw for a right-side target, and the permanent
RMA-152 static contract now guards this sign. No safety envelope, cadence, acceleration limit, or
production command route was relaxed.

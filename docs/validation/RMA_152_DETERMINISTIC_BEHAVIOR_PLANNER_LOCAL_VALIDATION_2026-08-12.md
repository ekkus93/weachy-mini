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
- deterministic expression and gesture trajectories relative to the fresh authoritative pose;
- explicit body-yaw, Stewart, and antenna coordination;
- authoritative contact/hard-stop/load/warning safety mapping;
- explicit workspace/controller interlocks;
- planning and in-flight trajectory cancellation with no fabricated replacement motion;
- fail-visible controller submission rejection with no automatic retry;
- fresh-state safe-rest replanning;
- production routing only through `ReachyProductionAuthoritativeRuntime.SubmitPositionTargets`.

## Local results

Focused static contract suite:

```text
python3 -m unittest -v scripts.tests.test_rma152_behavior_planner
Ran 11 tests
OK
```

The focused suite verifies source-set completeness, gaze fail-closed behavior, authoritative-pose
relative planning, safety interlocks, velocity/acceleration timing policy, planning and execution
cancellation, no-retry controller failure behavior, RMA-065 position-range provenance,
runtime/machine-policy drift, absence of nondeterministic clocks/randomness, and the production
controller path.

Broader script test discovery on the supplied refactored snapshot:

```text
python3 -m unittest discover -s scripts/tests -p 'test_*.py'
Ran 272 tests
OK
```

`python3 -m compileall -q scripts` also completed without a Python syntax failure.

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
- position, velocity, and acceleration envelope checks;
- controller/workspace/fault/contact/hard-stop/load interlocks;
- speech-only operation while motion is interlocked;
- authoritative-state motion/safety mapping;
- rejection of timing requests that would require unsafe motion;
- cancellation requiring fresh safe-rest planning;
- in-flight cancellation stopping later frame submission;
- controller rejection stopping without retry;
- preservation of unrelated authoritative actuator state during expression/gaze planning;
- bounded neutral safe-rest targets.

Managed warnings-as-errors/Unity compilation and CI remain required before the roadmap checkbox is
closed.

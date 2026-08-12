# RMA-153 baseline behavior library — local validation — 2026-08-12

## Scope

Local pre-CI validation of the RMA-153 baseline behavior library against the current RMA-152/RMA-151
contracts. The sandbox cannot execute managed compilation because `dotnet`, `csc`, `mcs`, and
`msbuild` are unavailable.

## Implemented contracts

- explicit immutable requests for all section-16.4 baseline behaviors;
- deterministic idle and speaking cycles with no random/system-clock input;
- normalized audio-energy speaking drive plus caller-triggered timing pulses;
- planner-bounded nod, curiosity/head-tilt, surprise/recoil, listening, and error expressions;
- exact current gaze acquisition;
- exact recently-seen target search with independent fresh coverage gating;
- RMA-152 safety, soft-envelope, cadence, velocity, acceleration, and frame-budget enforcement;
- safe-rest-before-sleep and neutral-reset-before-wake lifecycle ordering;
- post-execution sleep reset released only after completed execution matching the planned frame count;
- no direct native/raw-torque/MuJoCo motion path.

## Local results

Final focused static contract suite after lifecycle hardening:

```text
python3 -m unittest -v scripts.tests.test_rma153_baseline_behavior_library
Ran 7 tests
OK
```

Python syntax validation:

```text
python3 -m compileall -q scripts
```

completed without an error for the candidate static-test source set.

The modified Python test has a maximum line length of 99 characters, no lines over 100 characters,
and no lint-suppression directives.

## Managed fixture coverage

The managed `Rma153BaselineBehaviorLibraryContractTests` matrix covers:

- explicit request catalog and request bounds;
- deterministic idle/listening/speaking planning;
- zero/low/high audio-energy behavior;
- return-to-source anti-drift for idle, speaking, and lost-target search;
- RMA-152 scheduled position/velocity/acceleration envelopes;
- acknowledgment, curiosity, and surprise through planner-bounded gestures;
- exact visible gaze acquisition;
- deterministic lost-target search;
- current-coverage freshness and coverage-stop rejection;
- rejection of searching a target that is visible again;
- bounded unavailable/error expression;
- sleep safe-rest and reset mapping;
- completed-execution gating for the post-sleep reset;
- rejection of a completed execution result whose submitted frame count does not match the plan;
- wake reset mapping and fresh-neutral-source enforcement;
- safety-interlock and cancellation behavior.

## Managed validation limitation

Managed warnings-as-errors and Unity/Android compilation remain external closure gates. This record
must not be interpreted as evidence that those compilers have run in this sandbox.

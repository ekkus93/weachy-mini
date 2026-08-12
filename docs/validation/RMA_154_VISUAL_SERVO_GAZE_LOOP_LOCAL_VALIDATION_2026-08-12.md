# RMA-154 visual-servo gaze loop — local validation — 2026-08-12

## Scope

Local pre-CI validation of the RMA-154 implementation candidate against the RMA-152 deterministic
planner, RMA-153 baseline behavior library, bounded world model, and production authoritative
runtime in the supplied `master` snapshot.

The sandbox cannot execute managed compilation because `dotnet`, `csc`, `mcs`, and `msbuild` are
unavailable. Network access is unavailable, so the missing SDK cannot be installed here.

## Implemented contracts

- transformed normalized tracking bounds are the only gaze-error feedback signal;
- each correction uses a pure gaze-only intent and exact entity resolution through the RMA-152 planner;
- repeated servo iterations cannot accumulate the RMA-153 attentive-expression antenna offsets;
- all commands remain bounded scheduled position targets through the existing trajectory executor;
- successful target submission does not count as physical motion evidence;
- a new transformed frame alone does not count as physical motion evidence;
- physical motion is proven from a changed authoritative MuJoCo-derived body/head actuator state;
- the next feedback iteration additionally requires a newer transformed frame generated at or after
  the authoritative sequence where physical motion was observed;
- camera/source/continuity changes and authoritative/frame sequence or timestamp regression fail closed;
- tolerance, target loss, invalid coverage, load limit, other safety interlocks, timeout,
  cancellation, planner rejection, execution rejection, and unavailable feedback stop explicitly;
- production feedback capture is read-only and does not add a command shortcut;
- replay of an identical feedback stream yields identical submitted RMA-152 trajectories.

## Focused static results

```text
python3 -m unittest scripts.tests.test_rma154_visual_servo_gaze -v
Ran 6 tests
OK
```

The six checks cover source-set completeness, machine policy/runtime-default drift, the dual
physical-motion/post-motion-frame gate, explicit fail-closed stops, read-only production feedback,
and deterministic replay coverage.

The broader RMA-150 through RMA-154 focused source-contract matrix also passed:

```text
python3 -m compileall -q scripts
python3 -m unittest discover -s scripts/tests -p 'test_rma15*.py' -v
Ran 38 tests
OK
```

The repository-wide `scripts/ci.sh --static-only` run made continuous passing progress for ten
minutes but exceeded this sandbox command window before completion. No failure was observed before
the timeout; this is not recorded as a full-suite pass.

## Native regression results

A normal/default CMake build and CTest run completed successfully after the RMA-154 changes:

```text
cmake -S . -B <build-dir>
cmake --build <build-dir> -j2
ctest --test-dir <build-dir> --output-on-failure
100% tests passed, 0 tests failed out of 11
```

A separate `-DCMAKE_BUILD_TYPE=Release` probe exposed a pre-existing native test configuration
problem unrelated to RMA-154: standard `assert(...)` calls compile out under `NDEBUG`, after which
`-Werror` reports the assertion-only variables as unused in
`native/reachy_sim/tests/reachy_electrical_servo_model_test.cpp`. RMA-154 does not modify that file.
The repository's normal/default native configuration remains green.

## Managed fixture coverage

`Rma154VisualServoGazeLoopContractTests` covers:

- an edge target requiring multiple bounded adjustments before centering;
- explicit waiting for authoritative physical actuator motion;
- explicit waiting for a post-motion transformed frame tied to that motion sequence;
- rejection of requested/submitted targets as motion evidence;
- target loss, invalid coverage, load limit, fault/safety interlock, and cancellation with no
  fail-open command;
- deterministic replay of the same feedback samples with trajectory-by-trajectory equality.

## Remaining external closure gates

This local record does **not** claim RMA-154 complete. The following evidence still requires the
normal compiler/integration environment:

1. managed warnings-as-errors build and execution of the RMA-154 fixture;
2. Unity/Android compilation of the production rendering adapter;
3. integrated Unity/MuJoCo behavior acceptance proving a real edge target physically moves the
   simulated robot and is recentered through a transformed post-motion tracking frame.

The roadmap checkboxes should remain open until those closure gates pass.

# RMA-051 state-to-render mapping validation

**Date:** 2026-07-30  
**Validated implementation commit:** `09d5f6d3cf48a5b167f09629de520112ae60d5a6`  
**Hosted quality run:** `30593459422`  
**Self-hosted Unity/Android run:** `30593459413`

## Scope

This record closes RMA-051 only. It verifies that Unity consumes the latest two authoritative MuJoCo states, maps every canonical body pose into the Unity basis, interpolates by simulation time, snaps over reset/reload discontinuities, avoids steady-state managed allocation, exposes optional diagnostics, and preserves MuJoCo as the sole motion authority. RMA-052 formal invariant closure remains a separate open task.

## Production publication path

`ReachySimAuthoritativePoseSource` implements the reusable production-source contract. It owns three preallocated authoritative-state frames: previous, latest, and capture scratch. A newly published worker state rotates those frames instead of constructing a body-pose array or immutable snapshot.

`ReachyAuthoritativeRenderer` creates two caller-owned `ReachyReusableAuthoritativePoseFrame` instances when the source is bound. Its production `LateUpdate` path copies the latest ordered pair into those buffers, validates canonical body count/index/name and sequence/time ordering, converts MuJoCo world poses into Unity coordinates, and writes presentation transforms. The immutable `ReachyAuthoritativePoseSnapshot` API remains available for diagnostics and compatibility, but it is not used by the production frame loop.

No Unity transform, interpolation result, or diagnostic overlay value is written back into MuJoCo or the simulation worker.

## Timestamp and discontinuity behavior

Focused tests prove that interpolation is evaluated from authoritative simulation timestamps rather than render-frame count. Rendering a target timestamp through different call cadences produces identical body transforms, representing the required 30 FPS and 60 FPS equivalence.

Within a continuity epoch, sequence and simulation time must increase strictly. Duplicate or out-of-order publication faults visibly. When reset or model reload advances the discontinuity identifier, the renderer selects the newer state and does not interpolate through an impossible cross-epoch pose.

## Allocation evidence

Two independent regressions measure the steady-state managed allocation boundary after warmup:

- 128 repeated `TryCopyLatestPair` operations through the real production pose source allocate zero bytes on the current thread;
- 128 repeated generated-prefab render iterations allocate zero bytes on the current thread.

Bind/configuration time owns all state arrays, pose buffers, body bindings, and expected-transform storage. Successful frame publication, conversion, interpolation, and transform application allocate no managed collection or array.

## Diagnostics and competing writers

The generated prefab contains exactly one optional `ReachyPresentationDebugOverlay`. It starts disabled, maps all 18 canonical body axes, and retains all 16 imported joint labels. Drawing diagnostics never writes an authoritative body transform and never becomes a fallback pose source.

Tests intentionally mutate an authoritative transform and require the renderer to fault rather than silently overwrite the competing writer. Rigidbody, Rigidbody2D, Joint, Joint2D, ArticulationBody, Animator, legacy Animation, and PlayableDirector/Timeline components are rejected on mapped bodies and visual descendants.

## Physical Android acceptance

Self-hosted run `30593459413` executed on `kawa` against the exact validated commit. It prepared the generated presentation, staged production ARM64 MuJoCo, passed Unity tests, built and verified the API-26 ARM64 IL2CPP APK, installed it on the LG G6, passed the lifecycle scenario, passed physical authoritative rendering, uploaded structured evidence, and uploaded the APK.

Physical rendering verifies all 18 canonical body bindings, nonzero production model identity, ordered sequence/time, body yaw, head movement, both antenna bodies, all six Stewart links, reset continuity advancement, discontinuity snapping, renderer health, and absence of a hidden kinematic fallback.

Installed lifecycle acceptance exercises real HOME/resume cycles and proves that suspended wall time is not converted into a catch-up motion burst. The pinned official model exposes no named sleep/rest keyframe, so the `SleepRest` request remains typed `UNSUPPORTED`. This is intentional fail-closed behavior: the app verifies lifecycle sleep/wake and neutral/reset mapping without inventing an unsupported physical pose.

## Hosted validation

Hosted run `30593459422` passed every job on the same exact commit:

- managed warnings-as-errors build and native-backed lifecycle/state tests;
- native warnings-as-errors and sanitizer suites;
- pinned Reachy source/topology validation, visual conversion, MuJoCo compile and step, and desktop reference generation;
- actionlint, Ruff, formatting, ShellCheck, and static repository policy;
- Android lint, Java warnings, and tests.

## Result

RMA-051 is complete. The rendered robot is driven from the latest two ordered authoritative MuJoCo states, interpolated by simulation time, snapped across discontinuities, allocation-free in steady state, diagnosable, fail-closed against competing writers, and verified in the production Android artifact on physical hardware. RMA-052 remains open for its separately scoped formal invariant-closure checklist.

# RMA-040 official-model import validation

**Date:** 2026-07-30  
**Validated implementation commit:** `3cf8fd0862eb0352873b3366eeb802d50e382509`  
**Checklist closure commit:** `86c9b965b9217ad901b07000c80c5bbbc63896fc`

## Scope

This record covers RMA-040 only: pinned official model import, immutable source
provenance, topology preservation, desktop-camera isolation, machine-readable
mapping, compiled-model identity, Android loading, Unity-required transform
coverage, and the production publication/startup path needed to render that exact
model. RMA-041 and later tasks remain unchanged.

## Contract matrix

| Requirement | Verified behavior |
|---|---|
| Pinned source | Clean Pollen Robotics checkout at `a739a6e461eb6d722901f1cfc225265ffc85c28d`. |
| Immutable import | MJCF, referenced visual/collision meshes, and license are copied without byte modification and receive SHA-256 provenance. |
| Core topology | Body yaw, six Stewart hinges, seven passive ball joints, five loop closures, head, camera frame, and both antennas are required. |
| Complete body identity | All 17 named bodies and the ordered 18-body path hierarchy are locked; the anonymous body is pinned at index 15. |
| Actuator identity | Nine position actuators must map to the expected joints. |
| Source cameras | `studio_close` and `eye_camera` remain MuJoCo metadata but are explicitly excluded from the Unity presentation. |
| Source parameters | Solver MJCF, collision geometry, joint ranges, inertias, actuator definitions, and equality constraints are not transformed. |
| Machine-readable map | `MODEL_MAP.json` contains bodies, joints, actuators, equalities, sites, cameras, attributes, indices, hierarchy paths, and source hash. |
| Compiled baseline | MuJoCo 3.9.0 requires 19 bodies including world, 16 joints, 9 actuators, 5 equalities, 13 sites, `nq=37`, and `nv=30`. |
| Unity transforms | The generated presentation requires all 18 body transforms, unique canonical names, and stable `__body_15` identity. |
| Publication ownership | The 500 Hz simulation worker owns full authoritative-state capture and publishes copied frames to Unity; the render thread never competes for the serialized native handle. |
| Startup ordering | `ReachyPresentationRoot` configures and disables the renderer before the production runtime binds its pose source and re-enables rendering. |

## Regression coverage

The import test suite verifies deterministic repeated output, complete provenance,
dirty-checkout and revision rejection, topology-count drift, duplicate names, and
body reparenting with unchanged counts and names. The latter must fail with an
ordered body-path mismatch before generated state indices or Unity identities can
change.

Managed and Unity tests additionally prove that worker-owned authoritative frames
advance independently of render cadence, can be copied into a consumer-owned
frame, produce ordered pose pairs, transfer reader ownership deterministically,
and retain a negative presentation-root execution order before runtime binding.

## Production integration defects found and fixed

The first final-device pass exposed a real ownership defect: physics stepped and
published lightweight worker snapshots, while Unity independently polled the same
serialized native handle for full state. The renderer could remain pair-starved
without a native or managed fault. The worker now owns both state captures and
publishes the full authoritative frame through
`IReachyPublishedAuthoritativeStateSource`; Unity consumes only that publication.

A second instrumented pass showed healthy worker progress (`worker_steps=28941`,
`worker_state_sequence=28941`) while the renderer remained disabled. The cause was
unspecified Unity `Start` ordering: `ReachyPresentationRoot.Start()` could disable
the renderer after `ReachyProductionAuthoritativeRuntime.Start()` bound and
enabled it. `DefaultExecutionOrder(-1000)` now makes presentation configuration
run first, and an editor regression test locks the ordering contract. No polling
retry, mock backend, kinematic substitute, or cosmetic fallback was added.

## Validation gates

The authoritative evidence consists of:

- hosted native warnings-as-errors and sanitizer tests;
- hosted Python lint/format/static checks and import regressions;
- official pinned-source checkout, model-map validation, Unity render conversion,
  MuJoCo compile/step, and reference-trace generation;
- hosted managed and Android tests;
- self-hosted production ARM64 MuJoCo staging;
- Unity EditMode/PlayMode tests;
- ARM64/API-26 IL2CPP APK build and verification;
- installed physical-device lifecycle and authoritative-rendering acceptance.

## Automated evidence

Hosted Quality Gates run `30579208078` passed on
`3cf8fd0862eb0352873b3366eeb802d50e382509`, including the complete pinned-source
model gate, Unity conversion, MuJoCo compile/step, reference trace, actionlint,
Ruff, ShellCheck, repository policy, native warnings/sanitizers, managed
warnings-as-errors and native lifecycle tests, and Android lint/tests.

Self-hosted Unity/Android run `30579208126` passed on the same exact commit,
including production ARM64 MuJoCo staging, Unity EditMode/PlayMode tests, ARM64
API-26 IL2CPP build/verification, installed LG G6 lifecycle acceptance, physical
authoritative rendering, evidence uploads, and APK upload.

## Result

All eight RMA-040 checklist and acceptance items are supported by permanent
implementation, regression tests, hosted validation, and exact-head physical
Unity/Android validation. The official model is imported without solver mutation,
its complete topology remains identity-stable through native and Unity layers,
and the production renderer consumes worker-owned authoritative state with
deterministic startup ordering. RMA-041 and later tasks remain unchanged.

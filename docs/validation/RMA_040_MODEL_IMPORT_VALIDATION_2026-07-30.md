# RMA-040 official-model import validation

**Date:** 2026-07-30  
**Validated implementation commit:** `e1b1b64fedfb630b153a9f5e69df27796822590f`

## Scope

This record covers RMA-040 only: pinned official model import, immutable source
provenance, topology preservation, desktop-camera isolation, machine-readable
mapping, compiled-model identity, Android loading, and Unity-required transform
coverage. RMA-041 and later tasks remain unchanged.

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

## Regression coverage

The import test suite verifies deterministic repeated output, complete provenance,
dirty-checkout and revision rejection, topology-count drift, duplicate names, and
body reparenting with unchanged counts and names. The latter must fail with an
ordered body-path mismatch before generated state indices or Unity identities can
change.

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

Exact run IDs and checklist-closure commit are added only after those gates finish
on the closure head.

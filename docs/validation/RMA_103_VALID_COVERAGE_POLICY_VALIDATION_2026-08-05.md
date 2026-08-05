# RMA-103 Valid-Coverage Policy Validation

**Milestone:** RMA-103

**Accepted implementation SHA:** `9bcacfec7d4395e3e83e5f599402066f0d184718`

**Date:** 2026-08-05

**Status:** Complete

## Implemented contract

- exact valid-pixel counts from the RMA-102 shader predicates;
- no production GPU image readback or full-image CPU scan;
- immutable camera, calibration, timestamp, model, simulation, and continuity identity;
- fail-closed stale, regression, and conflicting-identity rejection;
- explicit `Normal`, `Degraded`, `Unusable`, and `Unavailable` states;
- hysteresis at 25/35 and 65/75 percent boundaries;
- preemptive vision-driven-turning stop at 35 percent; and
- shared coverage metadata for future tracking, VLM, world-model, behavior, and diagnostics consumers.

## Permanent RMA-103 workflow

Workflow run `31014441081` passed on the accepted implementation SHA.
It executed the complete managed camera contract project with warnings as errors and proved:

- identity coverage is exactly 100 percent;
- bounded row-interval counts equal an exhaustive shader-predicate reference;
- output dimensions define the exact denominator;
- every hysteresis and turning-stop boundary behaves as specified;
- stale, regressed, and identity-conflicting publications fail closed;
- a new simulation continuity permits sequence restart;
- unavailable coverage blocks new visual observations and stops vision-driven turning;
- runtime files contain no `ReadPixels`, `GetPixels`, `AsyncGPUReadback`, or fail-open `|| true`; and
- generated managed output is not tracked.

## Hosted CI

Hosted CI run `31014441299` passed on the same exact SHA:

- static policy, Actionlint, Ruff, ShellCheck, and repository checks;
- managed warnings-as-errors tests;
- native strict-warning and sanitizer tests;
- Android lint, Java warnings, and tests; and
- pinned Reachy model generation and topology validation.

## Real-graphics Unity and Android validation

Local Unity Android Validation run `31014441080`, attempt 2, completed successfully on `kawa` against the accepted implementation SHA.

Unity evidence artifact `8934362565` has digest `sha256:4afaf83e693a65e46580e257d1242de96afe20cb31c45d8b25e5156b9f0b7f98` and records:

- OpenGL Core with Mesa llvmpipe, not `NullGfxDevice`;
- EditMode `112/112` passed;
- PlayMode `1/1` passed;
- exact coverage/shader-predicate tests passed;
- GPU frame/coverage identity mismatch was rejected; and
- the active-render-texture release warning was absent.

The same attempt passed ARM64 API-26 APK build and verification, RMA-090 discovery, RMA-091 acquisition, RMA-092 physical GPU texture acceptance, RMA-022 lifecycle acceptance, authoritative rendering acceptance, all evidence uploads, APK upload, and final status publication.

## Device-attempt analysis

Attempt 1 failed in existing RMA-092 physical acceptance when CameraX reported `camera_fatal_error` during the second rear-camera start after rotation. Before the fault, the artifact recorded Vulkan output, exact timestamp correspondence, valid color/mirror/output contracts, and zero stale frames. The unchanged-SHA attempt 2 passed that same rotation/restart sequence and every downstream gate. No production fallback, retry path, threshold change, or source repair was introduced for the transient device error.

## Accepted artifacts from attempt 2

- Unity tests: `8934362565`, `sha256:4afaf83e693a65e46580e257d1242de96afe20cb31c45d8b25e5156b9f0b7f98`;
- RMA-090: `8934452451`, `sha256:9807c21c06d2df007adedf7f520f836cb3b0596cbb639215be4c5c2c311cf7c8`;
- RMA-091: `8934499193`, `sha256:6a06e3a69aee4c34c7d6cba8bd3aff63862d657509b62aec50788793a315d6e1`;
- RMA-092: `8934536500`, `sha256:e5ffc57bd75a07016519200ba72b1f6470a826d7339f56d4d4d456fada51d6d2`;
- lifecycle: `8934573688`, `sha256:ccfa32b7cafe7a023ea15640e1d868d8f0ee0c88df2f6aa4913dc589555ae26f`;
- authoritative rendering: `8934594852`, `sha256:5c380693eeae58e8b043d37a8966b2811f0dab55caa4589c1da684c2ae41d328`; and
- APK: `8934664829`, `sha256:107f621f594bf18a3104b1188639988a8b21f37dec3ed4b47de035f51894d30f`.

RMA-103 is accepted as the coverage-policy baseline for RMA-104 and later vision-provider interfaces.

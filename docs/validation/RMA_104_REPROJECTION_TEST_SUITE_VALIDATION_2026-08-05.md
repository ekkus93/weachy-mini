# RMA-104 Reprojection Test Suite Validation

**Milestone:** RMA-104

**Accepted implementation SHA:** `90a9a5390ce8c893899779c89d035eb3262965e6`

**Date:** 2026-08-05

**Status:** Complete; closeout exact-SHA validation in progress

## Implemented contract

- deterministic asymmetric synthetic source images;
- identity, synthetic yaw/pitch/roll, intrinsic scaling, front mirror, and
  90/270-degree orientation cases;
- GPU color and validity output compared with a test-only CPU reference that
  consumes the exact float shader matrix payload and performs projection in
  double precision;
- explicit invalid-mask boundary and stale-target poisoning tests;
- actual authoritative MuJoCo orientation distinguished from a requested target;
- `rotation_only` translation exclusion;
- transformed Reachy-eye frame routing for tracker, VLM, world-model, behavior,
  and diagnostics consumers, with raw phone access limited to
  `ExplicitRawDebug`;
- exact shader-payload coverage counting and machine-noise identity
  canonicalization; and
- an observed CameraX `CLOSED` teardown barrier before restart or switching.

## Defects discovered and repaired

RMA-104 exposed three contract defects rather than weakening tests:

1. The CPU oracle originally evaluated the unquantized `double` homography while
   Unity uploaded `float` coefficients. The oracle now reproduces the exact
   shader payload before doing double-precision projection.
2. The RMA-103 valid-coverage calculator also evaluated the pre-upload matrix.
   It now counts the final float payload, so coverage metadata and the validity
   texture agree at boundaries. Identity homography noise within `1e-12` of
   `0`, `1`, or `-1` is canonicalized, and the identity test requires all
   `187/187` pixels valid.
3. CameraX stop published `Stopped` immediately after unbind and removed the
   observer before hardware closure. The repaired Java/Unity contract remains
   `Stopping` until CameraX reports `CLOSED`, retains ownership until then,
   fails close errors visibly, and queues switches instead of racing them.

The first two physical attempts on intermediate SHA
`2d8ddf2f6cd773128cea7c8004cf690a37cec532` reproduced the same
`camera_fatal_error` during the second rear-camera start. This was therefore not
dismissed as a transient. No sleep, blind retry, or silent recovery path was
added. The accepted implementation passed the identical sequence after the
observed close barrier was installed.

## Permanent and hosted validation

Hosted CI run `31035832714` passed on the accepted implementation SHA:

- static policy, Actionlint, Ruff, ShellCheck, and repository checks;
- managed warnings-as-errors contracts;
- native strict-warning and sanitizer tests;
- Android lint, Java warnings, and tests; and
- pinned Reachy model generation and topology validation.

The permanent RMA-104 workflow watches all homography, coverage,
consumer-routing, close-barrier, managed-test, Unity-test, physical-acceptance,
and closeout-evidence files. The RMA-091 workflow also checks the Java
`Stopping`/`CLOSED` barrier and both Unity restart regressions, and watches this
validation report so the final evidence commit cannot bypass that contract.

## Real-graphics Unity and physical Android validation

Local Unity Android Validation run `31035832853`, job `92407563209`, completed
successfully on `kawa` against the accepted implementation SHA.

Unity artifact `8942610209` has digest
`sha256:998b4f5ca19fe7ad7ab81b181ff781ca549fe17e43157558b27498b77b4c3dd9`
and records:

- OpenGL Core with Mesa llvmpipe, not `NullGfxDevice`;
- EditMode `125/125` passed;
- PlayMode `1/1` passed;
- all identity, axis-rotation, intrinsic-scaling, mirror, orientation,
  invalid-mask, stale-pixel, authoritative-orientation, routing, shader-payload
  coverage, and CameraX close-barrier tests passed; and
- no active-render-texture release warning.

The same exact-SHA run passed ARM64 API-26 APK build and verification, RMA-090
camera discovery, RMA-091 CameraX acquisition, RMA-092 physical GPU texture
acceptance, RMA-022 lifecycle acceptance, authoritative rendering acceptance,
all evidence uploads, APK upload, and final commit-status publication.

The RMA-092 artifact proves:

- the first rear session produced valid Vulkan output at 90 degrees;
- the stop report contained `CameraX camera device reached CLOSED; Preview and
  ImageAnalysis are fully released.`;
- the second rear session started after closure and produced valid Vulkan output
  at 0 degrees;
- the second stop also reached `CLOSED` before the front-camera session;
- rear and front metadata remained monotonic;
- accepted and uploaded texture stale-frame counts remained zero;
- color, mirror, dimensions, timestamp correspondence, and opacity contracts
  passed; and
- no `camera_fatal_error` occurred.

## Accepted artifacts

- Unity tests: `8942610209`,
  `sha256:998b4f5ca19fe7ad7ab81b181ff781ca549fe17e43157558b27498b77b4c3dd9`;
- RMA-090: `8942686556`,
  `sha256:bc741bb6c5404c4c0a2465b8a08c3164c0a55c3e65423265f9edb05102dac4ee`;
- RMA-091: `8942726346`,
  `sha256:70eb9605dc03fb4e1a2e46142c3b804decd98b7f34514e5b2645c9c554fa2acd`;
- RMA-092: `8942757981`,
  `sha256:0bfb53eee70756c3e2db92822813906d654af6d7f71e9350b0d98067853ef9e9`;
- lifecycle: `8942791146`,
  `sha256:0d186c4a9ec833a7dddc23b6039f048889ca2c18941c185c3606472059146d72`;
- authoritative rendering: `8942809858`,
  `sha256:5ec8761f0d8bc6e2d93d321235f136975cc79f142ad6d30d5ba9446f027d90a1`;
  and
- APK: `8942857182`,
  `sha256:e580ce36842613d42ac5e6e7bf43d5ecbe6d4cb2162708f57683138d2ef0d714`.

## Closeout validation

The exact commit that adds this section contains the completed authoritative
TODO, the accepted implementation and evidence, the hardened permanent RMA-104
workflow, the hardened RMA-091 CameraX close-barrier workflow, and no temporary
applicator files. Its permanent, hosted, and self-hosted run IDs will be appended
only after every required gate passes on that exact SHA.

RMA-104 is accepted as the final reprojection baseline for RMA-110 vision
provider contracts. Final closeout workflow run IDs will be appended after the
closeout SHA passes.

# RMA-104 Reprojection Test Suite Validation

**Milestone:** RMA-104

**Accepted implementation SHA:** `90a9a5390ce8c893899779c89d035eb3262965e6`

**Closeout baseline SHA:** `c06626cd2b75f676adcca8614acf98dde1a4f7a4`

**Date:** 2026-08-05

**Status:** Complete

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

The corrected permanent RMA-104 workflow passed on exact SHA
`5728294b0918a3dbd7538d875d4529c30f8ce09c`:

- run `31038705840`;
- job `92417364946`; and
- managed camera contracts, exact reprojection/coverage/routing source checks,
  CameraX close-barrier source checks, repository cleanliness, and final status
  publication all succeeded.

The hardened RMA-091 workflow passed the validation-report change on exact SHA
`e27826bfdd58a7b39f8d9c3308e06e02d94fe52a`:

- run `31038500602`;
- job `92416682473`; and
- managed camera contracts plus Java/Unity ownership, metadata, backpressure,
  and observed-close-barrier source checks all succeeded.

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

## Accepted implementation artifacts

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

## Closeout baseline validation

The clean closeout baseline SHA
`c06626cd2b75f676adcca8614acf98dde1a4f7a4` contains:

- the completed authoritative TODO;
- the accepted implementation and evidence;
- the corrected permanent RMA-104 workflow;
- the hardened permanent RMA-091 workflow; and
- no temporary patcher, exporter, or stale-run cancellation workflow.

Hosted CI run `31038940293` passed on that exact SHA:

- native job `92418158405`;
- managed job `92418158443`;
- Android job `92418158507`;
- pinned Reachy-model job `92418158536`; and
- static job `92418158551`.

Local Unity Android Validation run `31038939639`, job `92418238840`, also
passed on the exact closeout baseline SHA. It completed generated presentation,
MuJoCo staging, the full real-graphics Unity suite, ARM64 API-26 APK build and
verification, RMA-090, RMA-091, RMA-092, RMA-022 lifecycle, authoritative
rendering, all evidence uploads, APK upload, and final status publication.

Closeout artifacts are:

- Unity tests: `8943899469`,
  `sha256:19e97d4e5934890f0d399c6eff2235099467c5d531fc6758135a0a497cdad1cd`;
- RMA-090: `8944017820`,
  `sha256:de08afeb0bc7654cf70d299653b6d2b109ce84b8bd118bbb157a05b0db1dbdf4`;
- RMA-091: `8944087203`,
  `sha256:58f36f6b0cc0c7406ee27bf6320d68adee04f0310b163aab18f70893031edacf`;
- RMA-092: `8944133129`,
  `sha256:ccce179f275370ab090bf4d6394a6e82020e985a72811a01335d74ca820e5df1`;
- lifecycle: `8944182821`,
  `sha256:7e3a27c317115729c10d61a8eba06ef5efbcfce332d57aaf62158590d8f72b1d`;
- authoritative rendering: `8944211588`,
  `sha256:5952d7867d10c94275068644cbe1360bef73f40c4383a46618ccf554a08543df`;
  and
- APK: `8944229252`,
  `sha256:c13e5c7cc6d88b9754086d4418e945a3eb73b2230d9f1623ede79f63380a2e44`.

A superseded closeout attempt was cancelled by the workflow concurrency policy
before substantive validation. A temporary cancellation workflow later received
HTTP 409 because the target was already completed; it changed no product or test
behavior and was deleted before the clean baseline SHA above.

RMA-104 is accepted as the final reprojection baseline for RMA-110 vision
provider contracts. The evidence-only commit containing this completed report
must pass hosted CI, the permanent RMA-104 and RMA-091 gates, and the complete
self-hosted Unity/Android chain before final sign-off.

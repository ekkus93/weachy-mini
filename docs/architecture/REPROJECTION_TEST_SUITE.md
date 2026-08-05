# RMA-104 Reprojection Test Suite

## Scope

RMA-104 is the final camera reprojection gate before vision-provider work. It
tests the RMA-100 calibration, RMA-101 authoritative rotation, RMA-102 GPU
homography renderer, and RMA-103 validity/coverage policy as one deterministic
contract. Production reprojection remains `rotation_only`; translation is
intentionally excluded from Level 1.

## Deterministic synthetic image

The Unity suite creates an asymmetric coordinate image whose red, green, and
blue channels are deterministic functions of the top-left pixel coordinate.
The pattern has no horizontal, vertical, or rotational symmetry, so yaw, pitch,
roll, mirroring, right-angle orientation, stale pixels, and transposed axes
cannot accidentally produce the same result.

Source textures use point filtering. The test-only CPU reference implements the
shader's source lookup exactly:

1. quantize the inverse homography to the same `float` payload uploaded through
   Unity's `Matrix4x4`;
2. perform projection and division in `double`;
3. reject depth at or below the shader epsilon;
4. reject source coordinates outside the closed pixel bounds;
5. round to the point-sampled texel selected by `(sourcePixel + 0.5) / size`;
6. emit opaque black and validity zero for every rejected pixel.

GPU color is read back only inside the Unity test assembly and compared with an
ARGB32 tolerance of three byte values. Validity is compared independently.
Production runtime files continue to prohibit `ReadPixels`, `GetPixels`, and
`AsyncGPUReadback`.

## Shader precision and exact coverage

RMA-104 exposed two boundary defects that simpler identity tests had missed:

- algebraically identical `K * inverse(K)` matrices retained machine-noise
  coefficients that could invalidate an entire edge column after float upload;
- RMA-103 coverage counted the pre-upload `double` homography while the shader
  consumed a `float` matrix, allowing coverage metadata and the emitted validity
  mask to disagree at boundaries.

`ReachyCameraHomographyCalculator` now canonicalizes only coefficients within
`1e-12` of `0`, `1`, or `-1`. This removes numerical identity noise without
weakening meaningful rotations or translations. The valid-coverage calculator
counts the final float shader payload. Independent managed and real-graphics
Unity tests require an identity transform to remain exact and report all
`187/187` output pixels valid.

## Test matrix

The suite covers:

- an identity-transform golden image;
- synthetic positive X, Y, and Z axis rotations representing pitch, yaw, and
  roll;
- nonuniform phone and Reachy intrinsic scaling;
- the explicit front-camera normalization mirror;
- 90-degree and 270-degree portrait/landscape normalization;
- GPU color and validity output against the double-precision CPU reference;
- invalid-mask boundaries with reused render targets poisoned by a prior
  magenta frame;
- actual MuJoCo camera-body orientation versus a different requested target;
- the `rotation_only` contract; and
- the default transformed-frame route for tracking, VLM, world-model,
  behavior, and diagnostics consumers.

## Stale-pixel rejection

The stale-pixel test first fills the reusable output targets with magenta under
an identity transform. It then renders a green source through a yaw that creates
both valid and invalid output regions. Every invalid output must be opaque black
with validity zero. A previous-frame color is never accepted as a hole-filling
fallback.

## Authoritative orientation

The authoritative-orientation test uses actual MuJoCo camera-body poses and
creates two cases: the solved actual pose and a deliberately different
requested target. The homography is built from the actual
`ReachyCameraRelativeRotationCalculator` sample. Its GPU output must match the
CPU reference for the actual pose and differ from the reference for the
requested target. Position is not an input and the calibration profile remains
labeled `rotation_only`.

## CameraX stop barrier

Repeated physical RMA-092 failures revealed that the old stop path published
`Stopped` immediately after `unbind()` and removed its CameraX observer before
the camera device reached `CLOSED`. A following session could therefore start
against hardware that was still closing.

The repaired contract is an observed teardown barrier, not a sleep or retry:

- Java publishes `Stopping`, unbinds Preview and ImageAnalysis, and retains the
  camera-state observer;
- owned preview, analyzer, executor, and camera references are released only
  after CameraX reports `CLOSED`;
- a critical close error becomes the visible `camera_close_failed` fault;
- Unity preserves `Stopping` and never fabricates `Stopped`;
- a requested camera switch is queued and begins only after the `CLOSED`
  snapshot; and
- physical RMA-092 acceptance requires the diagnostic
  `CameraX camera device reached CLOSED; Preview and ImageAnalysis are fully released.`
  before starting the rotated rear-camera session or the front-camera session.

Permanent Unity tests cover explicit stop and queued switching. Physical device
evidence proves the rear rotation restart and front switch complete with zero
stale frames and no `camera_fatal_error`.

## Consumer routing

`ReachyVisionFrameRoutingPolicy` establishes the boundary needed by RMA-110:

- tracking, VLM, world-model, behavior, and normal diagnostics require a
  transformed `ReachyCameraHomographyGpuFrame`;
- the transformed frame must have eligible coverage and an explicit validity
  mask;
- unavailable or unusable transformed frames fail closed; and
- raw phone access is represented only by the distinct `ExplicitRawDebug`
  purpose.

The route does not expose a raw texture to ordinary consumers. RMA-110 may
build provider interfaces on this boundary without weakening it.

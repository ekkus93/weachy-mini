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

Source textures use point filtering. This lets the CPU reference implement the
shader's exact source lookup:

1. transform each output pixel center with `ReachyToPhonePixels`;
2. reject depth at or below the shader epsilon;
3. reject source coordinates outside the closed pixel bounds;
4. round to the point-sampled texel selected by `(sourcePixel + 0.5) / size`;
5. emit opaque black and validity zero for every rejected pixel.

The reference performs projection and division in `double`. GPU color is read
back only inside the Unity test assembly and compared with an ARGB32 tolerance
of three byte values. Validity is compared independently. Production runtime
files continue to prohibit `ReadPixels`, `GetPixels`, and `AsyncGPUReadback`.

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

The authoritative-orientation test creates two MuJoCo camera-body poses: the
solved actual pose and a deliberately different requested target. The
homography is built from the actual
`ReachyCameraRelativeRotationCalculator` sample. Its GPU output must match the
CPU reference for the actual pose and differ from the reference for the
requested target. Position is not an input and the calibration profile remains
labeled `rotation_only`.

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

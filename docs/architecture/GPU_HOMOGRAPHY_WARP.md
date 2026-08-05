# RMA-102 GPU Homography Warp

## Status

This document defines the implementation contract for RMA-102. It consumes the
RMA-092 normalized phone RGB texture, the selected RMA-100 calibration profile,
and the timestamp-corresponding RMA-101 authoritative relative rotation. It
produces GPU-resident Reachy-view color and validity textures.

RMA-102 remains rotation-only. Translation, parallax, newly revealed surfaces,
occlusion changes, and final physical image-quality acceptance remain outside
this milestone.

## 1. Homography direction

All image coordinates use the normative upper-left-origin pixel-center
convention from
`docs/architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md`.

The forward phone-to-Reachy mapping is:

```text
H_phone_to_reachy =
    K_reachy
    * R_currentReachy_from_currentPhone
    * inverse(K_phone)
```

Forward splatting is not used. The shader receives the exact inverse mapping:

```text
H_reachy_to_phone =
    K_phone
    * transpose(R_currentReachy_from_currentPhone)
    * inverse(K_reachy)
```

For every Reachy output pixel center, the shader computes one source phone
pixel. This inverse gather avoids holes caused by forward splatting.

## 2. Pixel-center and texture-coordinate conversion

Calibration pixel coordinates identify pixel centers. Unity render-texture UVs
identify normalized texture coordinates. The shader therefore converts:

```text
p_reachy = top_left_uv * output_size - 0.5
p_phone_h = H_reachy_to_phone * homogeneous(p_reachy)
p_phone = p_phone_h.xy / p_phone_h.z
source_uv_top_left = (p_phone + 0.5) / source_size
source_uv_unity = (source_uv_top_left.x, 1 - source_uv_top_left.y)
```

The `-0.5` and `+0.5` terms preserve the integer pixel-center convention.
RMA-092 has already applied crop, display rotation, and the explicit front
preview mirror. RMA-102 does not repeat or reinterpret those transforms.

## 3. Fail-closed plan construction

`ReachyCameraHomographyCalculator` builds an immutable
`ReachyCameraHomographyPlan`. Construction rejects:

- zero source session, sequence, or timestamp metadata;
- camera identifier or facing mismatch;
- normalized source dimensions that do not match `K_phone`;
- calibration/model compatibility mismatch;
- a camera texture timestamp that does not match the RMA-101 phone-orientation
  timestamp;
- a non-proper RMA-101 rotation; and
- forward/inverse matrices that do not compose to identity within tolerance.

The successful plan retains:

- calibration profile, camera, facing, and model identifiers;
- source session, sequence, timestamp, and dimensions;
- authoritative model hash, sequence, simulation time, continuity, phone
  timestamp, and camera body ID;
- independent output dimensions from `K_reachy`;
- the forward phone-to-Reachy homography; and
- the inverse Reachy-to-phone shader mapping.

`TryMapReachyPixelToPhonePixel` is a CPU reference contract for tests and
diagnostics. It is not the production image path.

## 4. GPU execution

`ReachyCameraHomographyWarpRenderer` owns one hidden material and two reusable
render textures:

- an `ARGB32` transformed color texture;
- an `R8` validity texture, with `RFloat` as the capability fallback.

The shader has two GPU passes over the same inverse mapping:

1. color pass: sample the source only when the projected ray is in front of the
   phone camera and the projected pixel center is inside the source image;
2. validity pass: emit one for the same valid region and zero otherwise.

Invalid color pixels are opaque black. The validity pass performs no source
texture sample.

`ReachyCameraHomographyWarpPipeline` is the production composition seam. It
reads the currently sampleable RMA-092 texture and immutable descriptor, builds
the exact plan, executes the two GPU passes, and clears prior outputs whenever
the source or plan becomes invalid.

## 5. GPU-resident tracker handoff

`ReachyCameraHomographyGpuFrame.Color` and `.Validity` are `RenderTexture`
objects. Local trackers and later rendering stages can consume either texture
directly without `ReadPixels`, `GetPixels`, `AsyncGPUReadback`, or a
`Texture2D` copy.

CPU readback appears only in editor tests that verify shader output. It is not
present in the runtime homography implementation.

## 6. Validity rules

An output pixel is valid only when:

```text
p_phone_h.z > epsilon
0 <= p_phone.x <= source_width - 1
0 <= p_phone.y <= source_height - 1
```

The depth test rejects rays at or behind the phone camera. The bounds test is
performed before the color sample, so the runtime never samples an invalid
source coordinate.

Coverage thresholds, erosion, confidence policy, and physical reprojection
quality remain RMA-103 and RMA-104 concerns. RMA-102 only emits the exact
per-pixel validity mask.

## 7. Maintained files

- `Assets/ReachyMini/Runtime/Core/Application/ReachyCameraHomographyWarp.cs`
- `Assets/ReachyMini/Runtime/Rendering/ReachyCameraHomographyWarpRenderer.cs`
- `Assets/ReachyMini/Runtime/Rendering/ReachyCameraHomographyWarpPipeline.cs`
- `Assets/ReachyMini/Runtime/Resources/ReachyCameraHomographyWarp.shader`
- `managed/ReachyMini.Camera.Tests/Rma102GpuHomographyContracts.cs`
- `Assets/ReachyMini/Tests/Editor/ReachyCameraHomographyWarpTests.cs`
- `.github/workflows/rma102-gpu-homography-warp.yml`

## 8. Validation boundary

RMA-102 validation must prove:

- the exact forward formula and inverse composition;
- identity and nontrivial-rotation mappings;
- independent source/output dimensions;
- rejection of behind-camera and out-of-bounds pixels;
- color and validity render-target allocation;
- shader execution under Unity;
- no runtime CPU readback;
- existing hosted CI; and
- the existing self-hosted Unity/Android regression chain.

Physical alignment quality and acceptable valid-pixel coverage are not claimed
until the later roadmap milestones.

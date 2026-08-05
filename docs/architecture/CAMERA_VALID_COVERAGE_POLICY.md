# RMA-103 Valid-Coverage Policy

## Status

This document defines the RMA-103 production contract layered on the RMA-102
rotation-only GPU homography warp. RMA-103 does not change color reprojection,
calibration, or the authoritative MuJoCo rotation source. It adds exact
per-frame valid-coverage metadata, fail-closed publication ordering, coverage
classification, and the safety signal required to stop vision-driven turning
before coverage becomes unusable.

The numeric thresholds in this milestone are explicit engineering policy defaults.
They are configurable and are not physical-camera calibration claims.
Coverage is propagated to tracking, VLM, world-model, behavior, and diagnostics consumers.

## 1. Coverage calculation

The RMA-102 shader maps each integer Reachy output pixel center through
`H_reachy_to_phone`. A pixel is valid exactly when:

```text
q = H_reachy_to_phone * [x, y, 1]^T

q.z > 1e-6
q.x / q.z in [0, source_width - 1]
q.y / q.z in [0, source_height - 1]
```

Because `q.z` is positive for every accepted pixel, the division bounds are
equivalent to five affine half-plane constraints:

```text
q.z - 1e-6 > 0
q.x >= 0
(source_width - 1) * q.z - q.x >= 0
q.y >= 0
(source_height - 1) * q.z - q.y >= 0
```

`ReachyCameraValidCoverageCalculator` intersects these constraints along each
integer output row. Monotonic binary searches find the first and last valid
integer `x` without inspecting the GPU texture. The result is the exact number
of pixel centers that the shader marks valid, not a sampled estimate and not a
continuous polygon-area approximation.

This algorithm is `O(output_height * log(output_width))`, performs no
`ReadPixels`, `GetPixels`, `AsyncGPUReadback`, texture copy, or full-image CPU
scan, and uses the same `1e-6` positive-depth threshold as the shader.

## 2. Immutable identity

Every `ReachyCameraCoverageMeasurement` is constructed from one accepted
`ReachyCameraHomographyPlan` and retains:

- calibration profile, camera ID, facing, and model compatibility;
- camera session, source sequence, and source timestamp;
- output dimensions;
- model hash, authoritative sequence, simulation time, and continuity ID;
- phone-orientation timestamp and camera body ID;
- valid and total pixel counts;
- coverage fraction and percentage.

The GPU frame contains the same immutable coverage snapshot as the pipeline
result. Frame construction rejects coverage whose camera, simulation, output,
or timestamp identity does not match the homography plan.

## 3. Publication ordering and continuity

`ReachyCameraCoverageStateMachine` publishes coverage only in monotonic order
within one camera-session and simulation-continuity epoch.

Within one epoch it rejects:

- camera source-sequence regression;
- authoritative-sequence or simulation-time regression;
- a newer source sequence whose timestamp does not advance;
- one source sequence associated with different camera/orientation timestamps;
- model, camera, calibration, output-size, or camera-body drift without a new
  session or continuity; and
- an identical camera/simulation identity that produces a different valid-pixel
  count.

An exact duplicate is idempotent. A new camera session or simulation continuity
starts a new classification epoch and permits sequence restart. Rejected
publication causes the production pipeline to release both RMA-102 render targets and publish `Unavailable`; it never keeps the previous frame as a fallback.

## 4. Coverage classes and hysteresis

The default `EngineeringBaseline` policy is:

| Transition or action | Coverage fraction |
| --- | ---: |
| enter `Unusable` | `<= 0.25` |
| leave `Unusable` | `>= 0.35` |
| leave `Normal` | `< 0.65` |
| enter `Normal` | `>= 0.75` |
| stop vision-driven turning | `<= 0.35` |

Values between the state-transition thresholds remain in `Degraded`. Separate
entry and exit thresholds prevent classification chatter around a boundary.

The turning-stop threshold is deliberately higher than the unusable-entry
threshold. A behavior planner consuming this contract must stop additional
vision-driven turning while coverage is still degraded, rather than waiting
until the image is already unusable. `Unavailable` and `Unusable` always stop
vision-driven turning.

## 5. Consumer contract

A successful `ReachyCameraHomographyGpuFrame` contains:

- the transformed color `RenderTexture`;
- the per-pixel validity `RenderTexture`;
- the exact coverage snapshot.

The snapshot explicitly states:

- whether coverage and a matching validity mask are available;
- `Normal`, `Degraded`, `Unusable`, or `Unavailable`;
- whether new visual observations may be created;
- whether degraded/unavailable coverage must be disclosed; and
- whether vision-driven turning must stop.

Tracking, VLM, world-model, behavior, and diagnostics layers must carry this
same snapshot identity. Coverage does not authorize a consumer to ignore the
validity mask: detections and semantic observations must still reject invalid
pixels. `Unusable` or `Unavailable` coverage cannot create new visual
observations. Diagnostics remain responsible for showing the state and reason.

RMA-110 will define the full provider interfaces. RMA-103 establishes the
metadata object those interfaces must carry; it does not add a parallel vision
frame source.

## 6. Clearing and failure behavior

`ReachyCameraHomographyWarpPipeline` clears both GPU outputs and coverage when:

- the RMA-092 source texture is absent or not sampleable;
- camera/calibration/timestamp/model validation rejects the plan;
- coverage calculation or publication fails;
- GPU execution fails;
- an explicit lifecycle reset occurs; or
- the pipeline is disposed.

The public `Reset(reason)` seam is for stop, switch, pause, permission
revocation, and fault transitions. The reason is mandatory. A clear operation
never installs a synthetic default, retains an old percentage, or reuses old
pixels.

## 7. Maintained files

- `Assets/ReachyMini/Runtime/Core/Application/ReachyCameraValidCoverage.cs`
- `Assets/ReachyMini/Runtime/Rendering/ReachyCameraHomographyWarpRenderer.cs`
- `Assets/ReachyMini/Runtime/Rendering/ReachyCameraHomographyWarpPipeline.cs`
- `managed/ReachyMini.Camera.Tests/Rma103ValidCoverageContracts.cs`
- `Assets/ReachyMini/Tests/Editor/ReachyCameraHomographyWarpTests.cs`
- `.github/workflows/rma103-valid-coverage-policy.yml`
- `docs/architecture/CAMERA_VALID_COVERAGE_POLICY.md`
- `docs/validation/RMA_103_VALID_COVERAGE_POLICY_VALIDATION_2026-08-05.md`

## 8. Validation boundary

RMA-103 validation must prove:

- identity coverage is exactly 100 percent;
- row-interval counting equals an exhaustive shader-predicate reference;
- output dimensions determine the exact denominator;
- hysteresis transitions at every boundary;
- the turning-stop signal activates before unusable entry;
- stale, regressed, and identity-conflicting samples fail closed;
- continuity permits an explicit sequence restart;
- GPU frames reject mismatched coverage metadata;
- unavailable/error/reset paths release color, validity, and coverage together;
- runtime coverage contains no image readback;
- managed warnings-as-errors and Unity real-graphics tests pass; and
- the existing ARM64 API-26 and physical-device regression chain passes.

RMA-104 remains responsible for the broader golden-image, synthetic
yaw/pitch/roll, mirroring, orientation, CPU/GPU image-comparison, stale-pixel,
and actual-versus-target camera gate.

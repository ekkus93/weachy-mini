# RMA-101 Authoritative Camera Rotation Validation

**Task:** RMA-101 — Compute relative rotation from MuJoCo state  
**Status:** Complete  
**Implementation evidence SHA:** `ffeb02af405cac3131a4d69fe816fdf3e6908db7`  
**Validation date:** 2026-08-04

## 1. Scope

RMA-101 produces the dynamic rotation consumed by the Level 1 Reachy-eye
reprojection pipeline. It extracts the actual solved Reachy camera orientation
from the immutable authoritative MuJoCo state, converts the pinned camera-body
frame into the Reachy optical frame, combines that rotation with the selected
RMA-100 calibration and explicit phone orientation, and publishes exact
correspondence metadata for RMA-102.

RMA-101 is rotation-only. It does not consume body translation, implement the
GPU homography warp, define valid-coverage policy, or claim physical
reprojection-image quality. Those remain RMA-102 through RMA-104.

## 2. Implemented contract

### 2.1 Pinned MuJoCo optical binding

`models/reachy-mini/camera-reprojection-binding.json` and
`ReachyCameraMujocoOpticalBinding.PinnedReachyMini` bind the implementation to:

- upstream repository `pollen-robotics/reachy_mini`;
- source commit `a739a6e461eb6d722901f1cfc225265ffc85c28d`;
- MJCF SHA-256
  `efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46`;
- named optical site `camera_optical`;
- named camera `eye_camera`;
- canonical anonymous camera body `__body_15` / MuJoCo body ID `15`; and
- the expected 18 non-world authoritative body poses.

The fixed camera-body-to-optical rotation and neutral optical frame are proper
rotations derived from the pinned MJCF hierarchy. They are not inferred from a
Unity presentation transform.

### 2.2 Solved-state rotation

`Assets/ReachyMini/Runtime/Core/Application/ReachyCameraAuthoritativeRotation.cs`
implements:

```text
R_world_from_currentOptical =
    R_world_from_cameraBody(actual mjData.xquat)
    * R_cameraBody_from_optical

R_currentReachy_from_neutralReachy =
    inverse(R_world_from_currentOptical)
    * R_world_from_neutralOptical

R_currentReachy_from_currentPhone =
    R_currentReachy_from_neutralReachy
    * R_neutralReachy_from_neutralPhone
    * R_neutralPhone_from_currentPhone
```

The first operand comes from the solved `mjData.xquat` body pose published by
the authoritative simulation worker. Requested head targets, interpolated Unity
transforms, and visual animation are not accepted inputs.

### 2.3 Rotation-only boundary

The calculator accepts only a body quaternion. No body position or translation
argument exists in the public calculation contract. Translation is therefore
excluded structurally rather than read and discarded later.

Every successful sample retains:

- authoritative model hash;
- authoritative sequence;
- simulation time;
- continuity ID;
- phone-orientation timestamp;
- authoritative camera body ID;
- current MuJoCo-world-from-Reachy-optical rotation;
- current-Reachy-from-neutral-Reachy rotation; and
- current-Reachy-from-current-phone rotation.

This is the correspondence contract RMA-102 must preserve through GPU warp and
evidence publication.

### 2.4 Authoritative capture and fail-closed policy

`Assets/ReachyMini/Runtime/Simulation/ReachyAuthoritativeCameraRotationSource.cs`
reads the immutable state layout and latest published frame. Capture fails
closed for:

- calibration/model compatibility mismatch;
- zero model hash or unexpected authoritative body count;
- unavailable authoritative state;
- missing or duplicate body ID 15;
- invalid/non-normalizable authoritative quaternion;
- a sequence that does not advance within one continuity; and
- any improper derived rotation.

A continuity-ID change explicitly permits sequence reset. A stale sample does
not replace the last accepted correspondence.

## 3. Sign and direction validation

The optical axes remain `+X` image-right, `+Y` image-down, and `+Z` forward.
Managed and Unity contracts prove the inverse-camera mapping signs:

- positive yaw about optical `+Y` maps neutral forward toward current `-X`;
- positive pitch about optical `+X` maps neutral forward toward current `+Y`;
- positive roll about optical `+Z` maps neutral right toward current `-Y`.

The tests also distinguish actual solved orientation from a requested target and
prove that changing position while retaining the same solved quaternion cannot
change the Level 1 result.

## 4. Implementation files

The maintained RMA-101 implementation and evidence paths are:

- `models/reachy-mini/camera-reprojection-binding.json`;
- `Assets/ReachyMini/Runtime/Core/Application/ReachyCameraAuthoritativeRotation.cs`;
- `Assets/ReachyMini/Runtime/Simulation/ReachyAuthoritativeCameraRotationSource.cs`;
- `Assets/ReachyMini/Tests/Editor/ReachyAuthoritativeCameraRotationTests.cs`;
- `managed/ReachyMini.Camera.Tests/Rma101AuthoritativeRotationContracts.cs`;
- `docs/architecture/AUTHORITATIVE_CAMERA_RELATIVE_ROTATION.md`; and
- `.github/workflows/rma101-authoritative-camera-rotation.yml`.

## 5. Permanent RMA-101 gate

Permanent workflow:

- Workflow: `RMA-101 Authoritative Camera Rotation`
- Run: `30964753440`
- Job: `92176195712`
- Exact SHA: `ffeb02af405cac3131a4d69fe816fdf3e6908db7`
- Result: passed

URLs:

- `https://github.com/ekkus93/weachy-mini/actions/runs/30964753440`
- `https://github.com/ekkus93/weachy-mini/actions/runs/30964753440/job/92176195712`

The job passed:

- managed RMA-090, RMA-091, RMA-100, and RMA-101 camera contracts under the
  repository warnings-as-errors policy;
- pinned upstream MJCF file and SHA verification;
- the actual parent/child hierarchy of `camera_optical`, `__body_15`, and
  `eye_camera`;
- the exact fixed body-to-optical quaternion relationship;
- authoritative-state-only integration policy;
- requested-target and Unity-presentation exclusion;
- translation-free Level 1 API policy;
- stale-sequence and continuity-reset policy;
- yaw, pitch, and roll sign contracts; and
- repository cleanliness and absence of temporary RMA-101 staging files.

## 6. Hosted repository validation

Hosted workflow:

- Workflow: `CI`
- Run: `30964753430`
- Exact SHA: `ffeb02af405cac3131a4d69fe816fdf3e6908db7`
- Result: passed

URLs:

- `https://github.com/ekkus93/weachy-mini/actions/runs/30964753430`

All five jobs passed:

| Job | Job ID |
| --- | ---: |
| Reachy model | `92176195469` |
| Native | `92176195547` |
| Static | `92176195555` |
| Android | `92176195572` |
| Managed | `92176195575` |

This preserved the full repository model, native, static-policy, Android, and
managed contract boundary on the exact RMA-101 implementation SHA.

## 7. Unity and physical-device validation

Self-hosted workflow:

- Workflow: `Local Unity Android Validation`
- Run: `30964753429`
- Successful rerun job: `92182165671`
- Exact SHA: `ffeb02af405cac3131a4d69fe816fdf3e6908db7`
- Runner: `kawa`
- Result: passed

URLs:

- `https://github.com/ekkus93/weachy-mini/actions/runs/30964753429`
- `https://github.com/ekkus93/weachy-mini/actions/runs/30964753429/job/92182165671`

The successful exact-SHA job passed:

- deterministic generated Reachy presentation preparation;
- production MuJoCo runtime staging;
- Unity edit-mode and play-mode tests;
- ARM64/API-26 IL2CPP APK build and verification;
- RMA-090 physical camera discovery;
- RMA-091 physical CameraX acquisition;
- RMA-092 physical GPU texture acceptance;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- all evidence uploads;
- APK upload; and
- final commit-status publication.

The Unity XML records 106/106 edit-mode tests and 1/1 play-mode test passing.
The four RMA-101 Unity tests cover generated body binding, actual solved
rotation/sign behavior and translation independence, stale-sequence rejection
with same-source continuity reset, and fail-closed missing/duplicate body cases.

## 8. RMA-091 transient failure and unchanged-SHA rerun

The first self-hosted attempt, job `92176195708`, passed Unity tests, APK build
and verification, and RMA-090, then encountered a CameraX
`camera_fatal_error` during the third rear-camera start after display rotation.
The application remained alive and published explicit fault evidence; downstream
device steps were correctly skipped.

Comparison with the prior successful device evidence showed the same rear-camera
selection sequence (`0 -> 2 -> 0`). The failure was therefore classified as a
one-off CameraX critical error rather than a reproducible RMA-101 regression.
The failed job was rerun without changing source, APK inputs, or commit SHA.

The unchanged-SHA rerun passed RMA-091 and recorded:

- four CameraX sessions;
- 51 frame observations;
- rear and front frame delivery;
- rotation metadata changing from 90 degrees to 0 degrees;
- monotonic metadata and zero fault transitions; and
- final fail-closed `PermissionRevoked` state.

No CameraX recovery patch or RMA-101 source change was justified or made.

## 9. Successful rerun artifacts

| Artifact | ID | Digest |
| --- | ---: | --- |
| Unity test results | `8915060063` | `sha256:f8ba229175a95bc76dd53184d7cd74b61748e40d27b270d854ba5c77d9267e27` |
| RMA-090 camera discovery | `8915111305` | `sha256:f59e81fda9154237fc9f0ed9f168f3504280600ac678700d888476d96f95d476` |
| RMA-091 camera acquisition | `8915138562` | `sha256:cb2964c0b350815e64458678fae77c719065cabf4640c6bc2e40203f3d806322` |
| RMA-092 camera texture | `8915156150` | `sha256:e91ef7dd925a8465f31727e25b8fac4b82ac6eb2d90ae3e16c4a7839aede61d7` |
| RMA-022 lifecycle | `8915177452` | `sha256:2752ebcc4f27c8fd5524c3f947097d5f93d83ddac653ae3cb8133ae9b05c1e1b` |
| Authoritative rendering | `8915187961` | `sha256:12292e7d99187e1c1238b08423cac0a2ca382448551b40bd2ba030435921a9f0` |
| ARM64/API-26 APK | `8915214849` | `sha256:fcce02bf2383ef129d29540a3da48378129c620e7ababad72dc92a73d5a92099` |

## 10. Truthfulness boundaries

RMA-101 proves rotation extraction and composition, not a measured production
phone calibration. It uses whichever explicitly selected RMA-100 calibration
profile is supplied and does not invent intrinsics or mounting transforms.

The physical-device run proves that the new solved-state rotation contracts do
not regress the existing camera, texture, lifecycle, APK, or authoritative
rendering paths. It is not yet a GPU homography image-quality or valid-coverage
acceptance test.

Translation, parallax, newly revealed surfaces, occlusion changes, invalid-pixel
coverage, and output-image warp correctness remain outside RMA-101.

## 11. Conclusion

RMA-101 is complete. The repository has one pinned, fail-closed path from actual
solved MuJoCo camera-body orientation to a timestamped
`CurrentReachyFromCurrentPhone` rotation suitable for RMA-102's GPU homography
warp. Requested targets, presentation transforms, and translation cannot
silently substitute for authoritative solved orientation.

## 12. Formal roadmap closeout

The authoritative roadmap was changed to `Complete (2026-08-04)`, all five
RMA-101 tasks were checked, the exact implementation and validation evidence
was added, and the temporary self-removing finalizer was deleted in closeout
commit `da61a7252e9be508ec3fd4530eaf0a40b961b1d3`.

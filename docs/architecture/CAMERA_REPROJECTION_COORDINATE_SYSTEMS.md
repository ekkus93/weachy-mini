# Camera Reprojection Coordinate Systems and Calibration

## Status

This document defines the RMA-100 coordinate and calibration contract for the
Level 1 Reachy-eye camera path. It is normative for RMA-101 through RMA-104.
Level 1 is intentionally **rotation-only**. It does not model translation,
parallax, newly revealed surfaces, or occlusion changes.

## 1. Mathematical convention

All vectors are column vectors. Transform composition is right to left:

```text
p_output = A * B * p_input
```

Pixel coordinates refer to pixel centers. A pixel is represented in homogeneous
form as `(u, v, 1)`. A camera ray is represented in the right-handed optical
frame described below.

The rotation-only image mapping is:

```text
H = K_reachy * R_reachy_phone * inverse(K_phone)
```

The production warp uses the inverse mapping from each destination pixel to the
source texture. RMA-100 defines the operands; RMA-101 will derive the dynamic
rotation from authoritative MuJoCo state, and RMA-102 will execute the GPU warp.

## 2. Coordinate-system contract

| Space | Origin and axes | Handedness | RMA-100 treatment |
| --- | --- | --- | --- |
| Android source pixels | Upper-left origin, `+u` right, `+v` down | 2D pixel space | Raw CameraX/YUV buffer before crop, display rotation, or preview mirror |
| RMA-092 normalized RGB pixels | Upper-left origin, `+u` right, `+v` down | 2D pixel space | Crop, then clockwise display rotation, then optional horizontal front preview mirror |
| Phone optical camera | Optical center, `+X` image-right, `+Y` image-down, `+Z` forward | Right-handed | Input ray space for reprojection |
| Reachy optical camera | Virtual optical center, `+X` image-right, `+Y` image-down, `+Z` forward | Right-handed | Destination ray space for reprojection |
| Unity world/camera local | `+X` right, `+Y` up, `+Z` forward | Unity convention | Presentation and generated robot transforms |
| MuJoCo world/body | Project contract: `+X` right, `+Y` forward, `+Z` up | Right-handed | Authoritative simulation transforms |

The existing authoritative-rendering conversion is retained:

```text
Unity position (x, y, z) = MuJoCo position (x, z, y)
```

`ReachyCameraCoordinateContract.UnityWorldFromMujocoWorld` encodes that basis
change. `ReachyCameraCoordinateContract.OpticalFromUnityCamera` changes Unity
camera-local `+Y up` to optical `+Y down`. Their product maps a MuJoCo camera
vector `(x, y, z)` to optical `(x, -z, y)` and is a proper rotation.

A basis conversion of a rotation uses conjugation:

```text
R_target = B_target_source * R_source * inverse(B_target_source)
```

The code rejects a matrix that is not a proper finite rotation before or after
conversion.

## 3. Mirroring is not physical orientation

The RMA-092 texture bridge establishes one normalized RGB source. Its pixel
normalization order is fixed:

```text
source buffer
  -> crop
  -> clockwise display rotation
  -> horizontal mirror for front preview only
  -> normalized RGB texture
```

The corresponding homogeneous pixel matrix is:

```text
A_normalized_source = A_mirror * A_rotation * A_crop
```

The persisted `ReachyCameraImageNormalization` stores both this matrix and its
inverse, plus the source/crop/output dimensions.

A front-camera horizontal mirror is a **pixel-space reflection**. It is never
inserted into the physical phone orientation or the neutral phone-to-Reachy
rotation. Therefore:

- front profiles must record `mirrorHorizontally=true`;
- rear and external profiles must record `mirrorHorizontally=false`;
- a front camera may still have an identity physical neutral rotation;
- RMA-101 must not infer a physical yaw reversal from preview mirroring.

## 4. Intrinsic matrices

`ReachyCameraIntrinsicMatrix` stores a full finite, invertible affine 3x3
projection matrix and the image dimensions it describes.

A conventional source pinhole matrix is:

```text
K_source = [ fx  skew  cx ]
           [  0    fy  cy ]
           [  0     0   1 ]
```

After RMA-092 normalization:

```text
K_phone = A_normalized_source * K_source
```

Crop, 90-degree rotation, or mirroring can produce a matrix that is no longer in
upper-triangular pinhole form. Storing the complete 3x3 matrix avoids discarding
that information or incorrectly folding a reflection into a quaternion.

`K_phone` always describes the exact normalized RGB texture dimensions exposed
by RMA-092. `K_reachy` describes the independently selected virtual Reachy-eye
output dimensions. RMA-102 may therefore render at a resolution different from
the phone source.

Intrinsic provenance remains explicit:

- `AndroidPlatformMetadata` for usable Camera2 calibration metadata;
- `MeasuredCheckerboard` for a retained measured calibration dataset;
- `UserSupplied` for an explicit imported profile;
- `UncalibratedEstimate` for a labeled temporary pinhole estimate.

An estimate is usable only as an explicitly selected uncalibrated profile. It is
never reported as calibrated.

## 5. Neutral phone-to-Reachy relationship

Every profile stores a normalized quaternion named
`NeutralReachyFromPhoneRotation`. It maps a physical phone optical ray into the
virtual Reachy optical frame when the simulated Reachy camera is in its neutral
pose.

Identity means that the normalized phone camera view and neutral virtual Reachy
view are aligned. The quaternion must produce an orthonormal matrix with
determinant `+1`; reflection and mirroring are prohibited.

RMA-101 will combine this neutral relationship with the **actual** authoritative
MuJoCo camera-site/body orientation. It must not use a requested head target in
place of the simulated pose. The exact dynamic composition belongs to RMA-101
and will be tested there.

## 6. Versioned calibration profile

A `ReachyCameraCalibrationProfile` contains:

- profile schema version and stable profile ID;
- stable Android camera ID and lens facing;
- provenance category, human-readable detail, and source reference/hash;
- compatible Reachy model/asset key;
- UTC creation timestamp;
- source crop/rotation/mirror normalization;
- normalized phone 3x3 projection and dimensions;
- virtual Reachy 3x3 projection and dimensions;
- neutral Reachy-from-phone optical rotation;
- fixed `rotation_only` reprojection mode.

The profile constructor fails closed for:

- unknown provenance;
- unsupported schema;
- invalid camera facing;
- non-UTC timestamps;
- singular/non-finite matrices;
- source/crop/output dimension disagreement;
- front/rear mirror disagreement;
- non-rotation neutral transforms.

## 7. Exact profile selection

Calibration lookup is exact on:

1. camera ID and lens facing;
2. normalized phone image dimensions;
3. virtual Reachy output dimensions;
4. Reachy model compatibility key.

The result distinguishes:

- no profiles installed;
- camera mismatch;
- image-size mismatch;
- model mismatch;
- exact uncalibrated estimate;
- exact calibrated profile.

No mismatch loads a default and calls it calibrated. When more than one exact
profile exists, a calibrated profile is preferred over an estimate, then the
newest UTC profile is selected deterministically.

## 8. Persistence and recovery

Camera calibration is stored independently from ordinary preferences in:

```text
reachy-camera-calibration-v1.json
```

The envelope and each profile are versioned. Writes use a temporary file,
backup, replacement, and cleanup sequence. An invalid or unsupported file is
moved to a timestamped `.corrupt-*` path. The active state becomes empty and
degraded; no silent default calibration is installed.

`ReachySettingsPersistenceApplicationService` owns this calibration store so the
application has one persistence boundary while retaining separate schemas and
failure diagnostics. A settings-file problem and a calibration-file problem are
reported independently through combined service health.

Camera frames, images, and checkerboard source media are not persisted by this
store. A measured profile records only provenance and a source reference/hash;
retaining or exporting the source dataset is a separate explicit workflow.

## 9. RMA-101 handoff

RMA-101 must provide a timestamp-corresponding actual camera rotation derived
from the authoritative MuJoCo body/site transform. Its output must use the
right-handed optical convention in this document and combine with the selected
neutral rotation without adding translation.

The following remain intentionally outside RMA-100:

- selecting the authoritative MuJoCo camera site/body;
- dynamic head rotation extraction;
- GPU homography execution;
- validity-mask generation and coverage policy;
- physical reprojection image acceptance.

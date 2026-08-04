# RMA-100 Camera Coordinate and Calibration Validation

**Task:** RMA-100 — Define coordinate systems and calibration  
**Status:** Complete  
**Implementation evidence SHA:** `e809e98522585dd207de1f0bef831a1fcdd7c462`  
**Validation date:** 2026-08-04

## 1. Scope

RMA-100 establishes the mathematical and persistence contract required by the
Level 1 rotation-only Reachy-eye reprojection pipeline. It defines the spaces,
basis conversions, image normalization, intrinsic matrices, neutral
phone-to-Reachy optical relationship, profile selection rules, calibration
provenance, persistence format, and failure behavior consumed by RMA-101 through
RMA-104.

RMA-100 does not implement dynamic MuJoCo camera rotation extraction, the GPU
homography shader, valid-coverage masks, or physical reprojection image
acceptance. Those remain RMA-101 through RMA-104.

## 2. Implemented contract

### 2.1 Coordinate systems

`docs/architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md` is the normative
coordinate-system document. It defines:

- column-vector, right-to-left transform composition;
- Android source pixels with upper-left origin, `+u` right, and `+v` down;
- the RMA-092 normalized RGB pixel space after crop, display rotation, and
  optional front preview reflection;
- right-handed phone and Reachy optical frames with `+X` image-right, `+Y`
  image-down, and `+Z` forward;
- Unity camera/world and MuJoCo world/body axes;
- the production MuJoCo-to-Unity basis relationship;
- the proper optical basis conversion used for physical rotations; and
- the Level 1 rotation-only homography convention:

```text
H = K_reachy * R_reachy_phone * inverse(K_phone)
```

### 2.2 Shared calibration model

`Assets/ReachyMini/Runtime/Core/Application/ReachyCameraReprojectionCalibration.cs`
provides Unity-independent contracts for:

- finite vectors, quaternions, and 3x3 matrices;
- normalized quaternions and proper rotation validation;
- finite and invertible complete 3x3 projection matrices;
- crop, clockwise display rotation, and optional horizontal reflection;
- source and normalized image dimensions;
- phone and virtual Reachy projection matrices;
- stable camera identity and lens facing;
- calibration provenance and source references;
- Reachy model compatibility keys;
- UTC creation timestamps and profile schema versions;
- exact profile selection with explicit mismatch statuses; and
- neutral rotation-only homography construction.

A complete 3x3 phone projection matrix is retained after image normalization.
This avoids incorrectly forcing a crop, quarter-turn, or reflection into a
conventional upper-triangular pinhole matrix.

### 2.3 Mirroring and physical orientation

Front-camera preview mirroring is represented only as a pixel-space reflection.
It is not inserted into `NeutralReachyFromPhoneRotation` and is never treated as
a physical yaw reversal. Front profiles require the reflection flag; rear and
external profiles reject it.

The neutral phone-to-Reachy relationship is a normalized proper quaternion.
Reflections, singular transforms, non-finite values, and determinant-negative
matrices fail closed.

### 2.4 Intrinsic provenance and profile selection

Calibration provenance distinguishes Android platform metadata, retained
checkerboard measurement, explicit user-supplied data, and labeled
uncalibrated estimates. An estimate may be selected explicitly but is returned
as `ExactUncalibratedEstimate`, never `ExactCalibrated`.

Selection is exact on:

1. Android camera ID and lens facing;
2. normalized phone image dimensions;
3. virtual Reachy output dimensions; and
4. Reachy model compatibility key.

The result distinguishes no profiles, camera mismatch, image-size mismatch,
model mismatch, exact uncalibrated estimate, and exact calibrated profile. No
mismatch silently substitutes a default profile.

### 2.5 Versioned persistence

`Assets/ReachyMini/Runtime/Application/ReachyCameraCalibrationPersistence.cs`
stores calibration independently from ordinary preferences in:

```text
reachy-camera-calibration-v1.json
```

The envelope and profiles are versioned. Writes use temporary-file, backup,
replacement, and cleanup stages. Invalid or unsupported data is moved to a
`.corrupt-*` quarantine path; active calibration state becomes empty and
service health becomes degraded. No default calibration is installed during
recovery.

`ReachySettingsPersistenceApplicationService` owns the calibration store and
publishes combined settings/calibration health while retaining separate files,
schemas, and diagnostics.

## 3. Hosted validation

Permanent workflow:

- Workflow: `RMA-100 Camera Calibration Contract`
- Run: `30959984801`
- Job: `92161575744`
- Exact SHA: `e809e98522585dd207de1f0bef831a1fcdd7c462`
- Result: passed

URLs:

- `https://github.com/ekkus93/weachy-mini/actions/runs/30959984801`
- `https://github.com/ekkus93/weachy-mini/actions/runs/30959984801/job/92161575744`

The hosted job compiled the shared core under the repository's
warnings-as-errors policy and explicitly executed the RMA-100 managed contract
suite. It passed tests for:

- explicit coordinate bases;
- RMA-092 crop/rotation/reflection order;
- separation of preview reflection from physical orientation;
- expected positive optical-yaw image direction;
- exact calibrated/uncalibrated and mismatch selection;
- invalid and singular calibration rejection; and
- the existing RMA-090 and RMA-091 camera contracts.

The same job passed its static integration and truthfulness gate, including:

- required core, persistence, settings, Unity-adapter, test, and documentation
  symbols;
- an explicit managed test entry point;
- absence of implicit module-initializer execution;
- absence of temporary RMA-100 staging workflows and payloads; and
- explicit rotation-only and no-silent-calibration policy language.

## 4. Unity and self-hosted validation

Permanent workflow:

- Workflow: `Local Unity Android Validation`
- Run: `30959984789`
- Job: `92161627492`
- Exact SHA: `e809e98522585dd207de1f0bef831a1fcdd7c462`
- Runner: `kawa`
- Result: passed

URLs:

- `https://github.com/ekkus93/weachy-mini/actions/runs/30959984789`
- `https://github.com/ekkus93/weachy-mini/actions/runs/30959984789/job/92161627492`

The job passed:

- generated Reachy Unity presentation preparation;
- production MuJoCo runtime staging;
- Unity tests and result upload;
- ARM64/API-26 IL2CPP APK build and verification;
- RMA-090 physical camera discovery and evidence upload;
- RMA-091 physical frame acquisition and evidence upload;
- RMA-092 physical GPU texture acceptance and evidence upload;
- RMA-022 lifecycle acceptance and evidence upload;
- authoritative rendering acceptance and evidence upload;
- APK artifact upload; and
- final commit-status publication.

The uploaded Unity result XML records 102/102 edit-mode tests passing. The four
RMA-100 Unity-facing tests prove:

1. core matrix and quaternion directions are preserved by the Unity adapter;
2. a versioned calibrated profile survives persistence round-trip;
3. an unsupported schema is quarantined without installing a calibration; and
4. the settings service owns and exposes the calibration persistence boundary.

## 5. Evidence artifacts

| Artifact | ID | Digest |
| --- | ---: | --- |
| Unity test results | `8912623164` | `sha256:7ec9253df8f7a09c109f58349e5cbbce826932e7e28703116f05bce2fa598a6e` |
| RMA-090 camera discovery | `8912711734` | `sha256:051400fc0a2bc6503ebeee9214b7cefad42be55a7047f4b154a40bd68ee78b38` |
| RMA-091 camera acquisition | `8912747228` | `sha256:ca51749f3db64909456b697dde21d3fc5ff1003dff78346b2632cab4f2f0aa1a` |
| RMA-092 camera texture | `8912779906` | `sha256:4c3727eb1a8dd15b259bb04f682ce514abb11ed1d4d748c694f24df60655ff86` |
| RMA-022 lifecycle | `8912807841` | `sha256:1fceae255f6bbe1523ec3c9b8733c5e3ee22218eb46d1ee3ad80e71f310a2cdd` |
| Authoritative rendering | `8912823649` | `sha256:c004093d841a6374f816b790f818121853f34ca7bb21669ae1793026ddb847b4` |
| ARM64/API-26 APK | `8912845797` | `sha256:56eec1dd62538d07af96f35d3158154bd0769037dd35b3815b39d228b8a9028a` |

## 6. Failure-path coverage

The maintained tests and contracts reject or expose:

- non-finite and singular matrices;
- non-normalizable or reflection-producing physical rotations;
- unsupported profile/envelope schemas;
- non-UTC profile timestamps;
- camera-facing/reflection disagreement;
- source, crop, normalized, and intrinsic dimension disagreement;
- camera, image-size, model, and output-size profile mismatches;
- invalid persistence JSON and unsupported persistence data; and
- attempts to treat an estimate as calibrated.

Persistence failure does not retain a stale active calibration, hide the error,
or replace the file with an invented profile.

## 7. Truthfulness boundaries

RMA-100 defines and validates the calibration mechanism; it does not claim that
a measured production calibration for the LG-H872 or another phone was created.
No checkerboard dataset, calibrated numerical phone intrinsics, or measured
phone-to-Reachy mounting rotation was invented during this task.

The physical-device run is regression evidence that the new contracts and
persistence integration did not break the existing camera, lifecycle, or
rendering paths. It is not a physical reprojection-quality acceptance test.

Level 1 remains rotation-only. Translation, parallax, newly revealed surfaces,
occlusion changes, validity-mask policy, and GPU warp image quality remain
outside RMA-100.

## 8. Conclusion

RMA-100 is complete. The repository now has one explicit, versioned, fail-closed
coordinate and calibration contract suitable for RMA-101's extraction of the
actual authoritative MuJoCo Reachy-eye rotation.

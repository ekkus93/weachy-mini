# RMA-090 Android Camera Discovery Validation — 2026-08-03

## Result

RMA-090 is complete. Android camera permission and capability discovery are
implemented without starting frame acquisition or weakening the fixed Unity
presentation-camera boundary.

## Validated implementation

- Exact implementation commit:
  `8ce02564bed817ed215478180ee1c4468def8baa`
- Hosted workflow: `RMA-090 Camera Discovery`
  - run `30874543837`
  - job `91883241667`
  - conclusion: success
- Self-hosted workflow: `Local Unity Android Validation`
  - run `30874543829`
  - job `91883242040`
  - runner: `kawa`
  - conclusion: success

## Contract validation

The hosted gate passed managed warnings-as-errors tests and permanent static
integration checks covering:

- permission starts at `NotRequested` and is requested only by an explicit
  camera action;
- granted, denied, permanently denied, revoked, unsupported, and faulted
  transitions;
- front/rear inventory, Camera2 characteristics, availability, orientations,
  and analysis resolutions;
- platform-intrinsics provenance and explicit calibration fallback;
- settings/application-shell truthfulness and fixed-camera invariants;
- no claim that device-camera frames flow before RMA-091.

## Physical-device validation

The installed ARM64/API-26 APK was exercised on:

- serial: `LGH87250967ab9`
- manufacturer: LGE
- model: LG-H872
- Android API: 26
- ABI: `arm64-v8a`

The acceptance sequence proved:

1. After clearing app data and revoking CAMERA, startup remained
   `NotRequested`, exposed no inventory, and displayed no permission dialog.
2. After externally granting CAMERA and relaunching, discovery reached
   `Granted` and reported three available cameras: two rear and one front.
3. Camera IDs `0` and `2` reported rear facing, 90-degree sensor orientation,
   level-3 hardware, and 28 YUV analysis sizes up to `4160x3120`.
4. Camera ID `1` reported front facing, 270-degree sensor orientation,
   limited hardware, and 20 YUV analysis sizes up to `2560x1920`.
5. The LG device did not expose usable lens-intrinsic calibration. Every
   camera therefore reported `CalibrationFallbackRequired` with the
   documented, explicitly uncalibrated checkerboard/pinhole fallback.
6. After revoking CAMERA and relaunching, persisted grant history produced
   `Revoked`, removed the inventory, and disabled camera selection.

The same run also passed the existing RMA-022 lifecycle acceptance and
authoritative-rendering acceptance, then uploaded the verified APK.

## Evidence artifacts

- Camera device report: artifact `8879158536`
  - digest `sha256:4c7de802d90cd84214e535247ec8a8bfedb0d9d924c8277eb0490a59cb9eec5c`
- Unity test results: artifact `8879118727`
  - digest `sha256:7c076595348f49827a14df5eee53b0f40bbaa4cb11e5450fb1b5f6aa431ed584`
- Lifecycle device report: artifact `8879175025`
  - digest `sha256:daad857c2dbdd61d185efb4409ebc0f04a5eb8e86566501006ce5b6af8b294b0`
- Authoritative-rendering report: artifact `8879187403`
  - digest `sha256:8d8c4bb3108647fbfc5648a47e888e694dd8ceeed162d3c103ecfe62d580705b`
- Verified device APK: artifact `8879219465`
  - digest `sha256:82676328c8cd538825758b311fd47c637c447a2ba0819093b65d70a2ab21ba67`

## Repository cleanup

Cleanup commit `fbacef76f83a6cc9c73542c28bd289f9e1039801` marked the
roadmap item complete, added this permanent validation record, and removed all
RMA-090 integration-only machinery:

- `.github/workflows/rma090-apply-integration.yml`;
- `scripts/apply_rma090_integration.py`;
- `scripts/apply_rma090_integration_v2.py`.

Only the permanent hosted RMA-090 gate and the permanent self-hosted Unity/
Android validation workflow remain responsible for ongoing regression coverage.

## Boundary retained for RMA-091

RMA-090 discovers capabilities only. `ReachyDiscoveredCameraApplicationService`
remains degraded and truthful because no CameraX preview/ImageAnalysis stream
has been bound and no frame is delivered to Unity. RMA-091 is the next camera
milestone.

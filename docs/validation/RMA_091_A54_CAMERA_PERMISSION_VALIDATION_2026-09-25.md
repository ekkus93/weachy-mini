# RMA-091 A54 Camera Permission Physical Validation — 2026-09-25

## Scope

This record captures a clean physical Android validation run for the existing RMA-091 CameraX acquisition and permission workflow on the connected Samsung A54.

The run is relevant supporting evidence for the RMA-190 `Permission denial/revocation` scenario and RMA-194 release acceptance diagnostics, but it does **not** close either checklist item by itself. RMA-190 still requires scenario-level reconciliation on a mergeable branch, and the authoritative TODO must only be updated after the required evidence is merged or otherwise safely reconciled.

## Evidence

- Repository: `ekkus93/weachy-mini`
- Application source under test: current `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Temporary validation branch: `ralph/a54-rma091-camera-permission-20260925`
- Temporary validation head: `42f9fc75602d272284f0e6879b1a862933de25bd`
- Workflow: `A54 RMA-091 Camera Permission Acceptance`
- Run ID: `36137125500`
- Job: `RMA-091 camera permission on A54`
- Result: `success`

All workflow steps completed successfully:

- checkout and Python setup;
- Unity/Android installation resolution;
- generated Reachy Unity presentation preparation;
- production MuJoCo runtime staging for Unity Android;
- ARM64 API-26 APK build;
- APK verification;
- physical A54 pin;
- `Run RMA-091 camera permission acceptance`.

## Interpretation

The successful run proves that the current `master` application source can be built and installed on the connected A54 and that the existing RMA-091 physical CameraX permission acceptance passed on that device.

This evidence is intentionally recorded on a clean documentation branch. The temporary workflow branch should not be merged; it existed only to run the already-existing physical validation path without depending on artifact upload, which is currently unavailable because GitHub Actions artifact storage is exhausted.

## Remaining requirements

This record is supporting physical evidence, not direct RMA-190 checklist closure. The RMA-190 `Permission denial/revocation` checkbox should remain open until the canonical RMA-190 scenario branch and TODO reconciliation include the required exact-head evidence without truncating or overwriting the authoritative TODO.

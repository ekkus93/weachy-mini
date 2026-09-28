# RMA-190 Core End-to-End Physical Validation — 2026-09-22

## Scope

This record is the clean replacement evidence lane for the first three RMA-190 scenarios:

- Offline launch with no network.
- MuJoCo full model load and neutral reset.
- Deterministic gesture replay.

It replaces the unsafe reconciliation attempted by PR #39, whose branch copy of
`docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` was truncated to 76 lines. This file
is intentionally additive and does not edit the authoritative TODO. The TODO checkboxes
must be reconciled only by a later safe full-file edit or other merge-safe mechanism that
preserves the complete current TODO.

## Evidence source — original canonical run

Ralph Bridge CI report `35768977213` records:

- repository: `ekkus93/weachy-mini`
- exact SHA: `b188ce3b18635d24da79cb762e245c52e361ab5e`
- workflow: `RMA-190 Core End-to-End Android Acceptance`
- result: `success`
- job: `RMA-190 core E2E on Android`
- artifact: `rma190-core-end-to-end-report-b188ce3b18635d24da79cb762e245c52e361ab5e`
- artifact status at audit time: non-expired

## Evidence source — A54 clean rerun

A later temporary branch-only workflow reran the same core acceptance path from exact current `master` source while the A54 was connected:

- temporary branch: `ralph/a54-rma190-core-20260925`
- temporary branch head: `b69ec73b3b8ad0196447a00bf791dd214dbaf7a9`
- base app source under test: `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e`
- workflow: `A54 RMA-190 Core E2E`
- run id: `36104854865`
- job: `RMA-190 core E2E on A54`
- result: `success`

The temporary workflow changed only a branch-scoped workflow file. It did not modify application source. The workflow deliberately printed the core report into the job log instead of uploading artifacts because GitHub Actions artifact storage quota was already exhausted for this repository/account at the time of the rerun.

The following steps all passed in run `36104854865`:

- source-contract validation;
- Unity/Android resolution;
- generated Reachy Unity presentation preparation;
- production MuJoCo Android runtime staging;
- ARM64 API-26 APK build;
- APK verification;
- physical A54 pin;
- `scripts/run_rma190_core_end_to_end_android.sh` acceptance.

## Closure interpretation

The two passing runs above are accepted as physical Android evidence candidates for the first three RMA-190 core scenarios. The original run produced the dedicated artifact, and the later A54 rerun provides a clean standalone core-only pass after the earlier combined A54 core+camera workflow produced a confusing final verdict because of camera `continue-on-error` handling.

The following RMA-190 scenarios remain explicitly outside this evidence record and must
stay open until their own scenario evidence passes:

- Front camera -> head rotation -> transformed Reachy-eye frame.
- On-device ASR -> local LLM -> behavior -> offline TTS.
- Rear-camera visual question with optional VLM.
- Independent cloud provider combinations.
- Permission denial/revocation.
- Network loss, rate limit, malformed response, and cancellation.
- Model corruption/OOM.
- Solver fault and controlled reset.

## Merge policy

This branch may be used as the canonical safe replacement for PR #39's evidence record.
It does not by itself close TODO checkboxes. A separate safe TODO reconciliation must
preserve the full authoritative TODO and apply only the intended RMA-190 core checkbox
and evidence-note delta.

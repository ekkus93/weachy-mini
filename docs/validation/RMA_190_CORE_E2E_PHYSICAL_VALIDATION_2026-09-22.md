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

## Evidence source

Ralph Bridge CI report `35768977213` records:

- repository: `ekkus93/weachy-mini`
- exact SHA: `b188ce3b18635d24da79cb762e245c52e361ab5e`
- workflow: `RMA-190 Core End-to-End Android Acceptance`
- result: `success`
- job: `RMA-190 core E2E on Android`
- artifact: `rma190-core-end-to-end-report-b188ce3b18635d24da79cb762e245c52e361ab5e`
- artifact status at audit time: non-expired

## Closure interpretation

The run is accepted as the physical Android evidence candidate for the first three RMA-190
core scenarios because it passed on the exact current master SHA and produced the dedicated
RMA-190 core end-to-end report artifact.

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

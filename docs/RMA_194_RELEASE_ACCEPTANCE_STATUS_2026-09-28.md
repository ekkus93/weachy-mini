# RMA-194 Release Acceptance Status — 2026-09-28

This document records the current release-gate status for the Reachy Mini Android digital twin. It is intentionally conservative: implementation and static contracts may exist before exact-head CI, Local Unity Android Validation, physical-device, provider, performance, privacy, license, and release-acceptance evidence is complete.

It is a planning/status artifact only. It does **not** close any checkbox in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`. TODO checkbox closure must wait for exact current-master evidence and authoritative TODO reconciliation.

## Release status

**Release status: not accepted.** No final release tag should be cut from the current tree, and no TODO checkbox in RMA-194 should be closed merely because this status document exists.

The project currently has evidence for three RMA-194 subitems that were already checked in the authoritative TODO before this document:

- MuJoCo is authoritative and stable, with supporting implementation status and RMA-032/RMA-060 through RMA-065 evidence.
- The full closed-loop Reachy model is rendered from native state, with RMA-051/RMA-052 evidence and no hidden kinematic fallback.
- Level 1 camera reprojection passes reference tests, with RMA-104 validation evidence and the reprojection workflow retained.

All other RMA-194 checklist entries remain open until their own acceptance evidence passes at the exact candidate head.

## Current merge and qualification boundary

Several hosted workflows continue to fail with the repository-wide zero-step/no-log or `JOB_NOT_FOUND` signature. Those failures are classified as CI infrastructure failures until a workflow exposes failed steps, logs, or another actionable source diagnostic.

Long-running self-hosted Android validation lanes may remain queued, pending, or in progress while the runner is busy. Queued status is not a pass and is not a source failure.

Artifact upload failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload. A physical Android run blocked by secure keyguard, device disconnection, missing installed ASR/TTS services, or unavailable runner hardware is a hardware/service availability condition, not a source-code failure. Lack of physical Reachy Mini hardware is not a release blocker for the virtual Reachy Mini product unless a later optional physical-calibration milestone explicitly requires it.

## RMA-194 item map

| RMA-194 item | Current evidence/status | Blocker before checkbox closure |
| --- | --- | --- |
| App launches and runs offline on supported hardware. | Open. Historical offline and application-composition evidence exists, but current `master` still needs exact-head release-candidate physical evidence. | Exact-head physical Android run on supported hardware, then TODO reconciliation. |
| MuJoCo is authoritative and stable. | Already checked in the TODO with RMA-032/RMA-060 through RMA-065 evidence. | Keep checked unless future source changes invalidate the evidence. |
| Full closed-loop Reachy model is rendered from native state. | Already checked in the TODO with RMA-051/RMA-052 evidence and `hidden_kinematic_fallback=false`. | Keep checked unless future source changes invalidate the evidence. |
| Level 1 camera reprojection passes reference tests. | Already checked in the TODO with RMA-104 validation evidence and the reprojection workflow retained. | Keep checked unless future source changes invalidate the evidence. |
| Android on-device ASR and offline TTS work where installed. | Open. RMA-121/RMA-123/RMA-125 source and provider contracts exist; full spoken-turn proof remains open. | Non-fabricated ASR -> local LLM -> behavior -> offline TTS evidence, preferably with networking disabled where required. |
| Selected benchmark-backed local LLM works without blocking physics. | Open. RMA-133 selected Qwen3-0.6B; RMA-134/RMA-135 local runtime/governor evidence exists historically. | Exact-head validation and representative-device profile reconciliation for the release candidate. |
| OpenAI and compatible providers are independently configurable. | Open. Provider configuration, BYOK, and cloud LLM/VLM wiring exist; real cloud-provider success is not claimed by this document. | Exact-head provider validation and intentionally supplied endpoint/credential evidence if a real cloud success claim is made. |
| Behavior planner validates all AI output. | Open. RMA-151/RMA-195 design routes validated behavior intents through production behavior capability. | Exact-head evidence that the full current path remains intact under malformed-provider responses and regeneration limits. |
| No silent provider, privacy, calibration, or kinematic fallbacks remain. | Open. RMA-173/RMA-191 gates now enforce key fail-closed boundaries; TODO closure still requires accepted exact-head evidence. | Exact-head CI/security/privacy/failure-mode validation and no redacted source gaps for the closure claim. |
| Diagnostics identify active fidelity, providers, and failures. | Open. RMA-170/RMA-171/RMA-172 diagnostics and bundle export exist; release closure needs current-head verification. | Exact-head CI plus audit that diagnostics remain wired to active fidelity/provider/failure state. |
| Representative-device performance report exists. | Open. RMA-184 defines the matrix; measured default profiles remain open; detailed rows are not reconstructed from redacted output. | Safely readable detailed measured profiles and supported-device target reconciliation. |
| Licenses and attribution are complete. | Open. RMA-192 review gate is now in-tree; final closure still needs exact-head validation; partial reconstruction from redacted inventory output is not acceptable. | Full exact-head license-review validation and TODO reconciliation. |

## Current open release blockers

The current release gate remains blocked by the following evidence gaps:

- RMA-125 / RMA-190: physical offline speech and spoken end-to-end scenarios still require device proof with suitable installed Android on-device ASR and exact offline TTS services.
- RMA-184: representative-device default profiles require measured low, mid, and high class reports; mid/high physical measurements are still pending.
- RMA-190: production end-to-end scenarios remain unchecked and must exercise shipping runtime paths, not test-only substitutes represented as production evidence.
- RMA-191: the security and privacy review has systematic contracts in-tree, but exact-head CI/device evidence must pass before TODO closure.
- RMA-192: the license and attribution review has systematic contracts in-tree, but exact-head CI evidence must pass before TODO closure.
- RMA-193: the release operator documentation hub exists in-tree, but exact-head CI evidence must pass before TODO closure.
- RMA-194: the remaining release checklist and acceptance criteria must be reconciled only after the evidence above is complete.


## Release-gate blockers

1. Generic CI and several status-publisher workflows continue to show zero-step/`JOB_NOT_FOUND` infrastructure failures.
2. Full RMA-125/RMA-190 spoken-turn evidence remains missing: on-device ASR utterance -> local LLM -> behavior -> offline TTS with the required offline/network-disabled conditions, or an explicitly accepted non-fabricated ASR acceptance method.
3. RMA-002 still requires second independently provisioned Unity/Android build evidence.
4. RMA-184 still needs detailed measured default profiles and representative-device target reconciliation.
5. RMA-192 still needs full readable inventory/notices/source changes plus exact-head license-review validation before release closure.

## Non-blocker classifications

- Artifact upload quota failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload.
- Zero-step CI failures with unavailable logs are infrastructure failures, not source failures.
- A physical Android run blocked by secure keyguard or device disconnection is a hardware availability condition, not a source failure.
- Lack of physical Reachy Mini hardware is not a release blocker for the virtual Reachy Mini product unless a later optional physical-calibration milestone explicitly requires it.

## Deferred-feature boundary

The initial release must not half-enable deferred future milestones. Level 2 depth reprojection, Level 3 scene reconstruction, AR placement, movable observer camera, and Reachy-specific VLA or learned social-motion policy remain future milestones and must not be labeled as active release features.

The correct behavior for missing deferred capability is an explicit unavailable, unsupported, or deferred state. The app must not provide a hidden fallback path that treats Level 1 reprojection as depth-aware, substitutes stale camera content, labels nominal dynamics as calibrated physical behavior, or silently promotes cloud/provider paths across a privacy boundary.

## Release labeling rule

No calibrated twin label is allowed without a named physical calibration profile and held-out physical evidence. No final release label is assigned by this document. A future release candidate must choose one of the documented labels — geometric baseline, dynamic baseline, servo-fidelity, or calibrated twin — strictly according to the evidence actually accepted at that candidate head.

## Evidence handling

Infrastructure failures such as zero-step hosted CI jobs, artifact-storage/upload failures, missing GitHub Actions job logs, pending self-hosted jobs, or transient bridge/API failures are not release evidence and are not source-code failures. They should keep the affected checklist items open while independent source, documentation, security, privacy, license, and release-gate work continues.

When the remaining evidence is available, update this file together with the authoritative TODO and include exact commit SHA, workflow run IDs, physical-device reports, artifact digests, tested device matrix, fidelity label, and known limitations.

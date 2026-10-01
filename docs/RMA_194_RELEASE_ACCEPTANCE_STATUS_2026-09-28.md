# RMA-194 Release Acceptance Status — 2026-09-28

This document records the current release-gate status for the Reachy Mini Android digital twin. It is intentionally conservative: implementation and static contracts may exist before exact-head CI, Local Unity Android Validation, physical-device, provider, performance, privacy, license, and release-acceptance evidence is complete.

It is a planning/status artifact only. It does **not** close any checkbox in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`. TODO checkbox closure must wait for exact current-master evidence, accepted evidence, and authoritative TODO reconciliation.

## Release status

**Release status: not accepted.** No final release tag should be cut from the current tree, and no TODO checkbox in RMA-194 should be closed merely because this status document exists. No final release label is assigned.

The project currently has evidence for three RMA-194 subitems that were already checked in the authoritative TODO before this document:

- MuJoCo is authoritative and stable, with supporting implementation status and RMA-032/RMA-060 through RMA-065 evidence.
- The full closed-loop Reachy model is rendered from native state, with RMA-051/RMA-052 evidence and no hidden kinematic fallback.
- Level 1 camera reprojection passes reference tests, with RMA-104 validation evidence and the reprojection workflow retained.

RMA-190 has accepted physical A54 evidence for its first three scenarios — offline launch, production MuJoCo full-model load plus neutral reset, and deterministic gesture replay — in `docs/validation/RMA_190_CORE_END_TO_END_VALIDATION_2026-09-29.md`. That evidence was refreshed on current master by exact-head no-artifacts workflow run `36823974738`, job `110245472761`, at commit `f03b83d8b29d2b099fa825b7b86721bafd5a0855`; the same run also passed RMA-090, RMA-091, RMA-092, RMA-111, RMA-154, RMA-022, RMA-195, and authoritative-rendering acceptance. This refresh does not close the other RMA-190 scenarios.

Exact-head RMA-173 silent-failure regression run `36823974671` passed at commit `f03b83d8b29d2b099fa825b7b86721bafd5a0855`, preserving the on-device-ASR/cloud-ASR, offline-TTS/network-voice, local-LLM/OpenAI, stale-camera-content, solver/Unity-animation, calibration-placeholder, dropped-buffer, and malformed-provider-response fail-closed cases. The authoritative TODO is still conservative for RMA-173 until its section is separately reconciled.

Exact-head release-review run `36823974604` on commit `f03b83d8b29d2b099fa825b7b86721bafd5a0855` passed the RMA-191 security/privacy, RMA-192 license/attribution, RMA-193 documentation, and RMA-194 status-map contracts. The authoritative TODO has since been reconciled for RMA-191, RMA-192, and RMA-193. RMA-194 rows remain open until release-candidate-specific evidence exists for each row.

Exact-head hosted CI run `36823974718` and Local Unity Android Validation run `36823974640` also passed at commit `f03b83d8b29d2b099fa825b7b86721bafd5a0855`; the latter completed Unity tests, APK build and verification, physical camera/tracking/visual-servo/lifecycle/application-composition/authoritative-rendering acceptance, artifact uploads, and final status publication.

All remaining RMA-194 checklist entries stay open until their own acceptance evidence passes on the release-candidate head. Exact current-master evidence is required before any additional release checklist item is closed.

## Current merge and qualification boundary

Several hosted workflows have shown the repository-wide zero-step/no-log or `JOB_NOT_FOUND` signature. Those failures are classified as CI infrastructure failures until a workflow exposes failed steps, logs, or another actionable source diagnostic.

A dedicated `RMA Release Review Contracts` workflow runs the RMA-012, RMA-161, RMA-162, RMA-170, RMA-172, RMA-195, RMA-191, RMA-192, RMA-193, and RMA-194 static/reconciliation contracts on a generic self-hosted Linux runner without requiring an Android phone. Run `36823974604` passed this gate at the exact executable-source head listed above.

Artifact upload quota failures are infrastructure failures; artifact-publication failures are also infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload. A physical Android run blocked by secure keyguard, device disconnection, missing installed ASR/TTS services, or unavailable runner hardware is a hardware/service availability condition, not a source-code failure. Lack of physical Reachy Mini hardware is not a release blocker for the virtual Reachy Mini product unless a later optional physical-calibration milestone explicitly requires it.

## RMA-194 item map

| RMA-194 item | Current evidence/status | Blocker before checkbox closure |
| --- | --- | --- |
| App launches and runs offline on supported hardware. | Open. RMA-190 core has accepted physical A54 offline-launch evidence on the current executable source line, but RMA-194 closure still belongs to release-candidate evidence. | Reconcile only after final release-candidate app launch evidence is selected for this row. |
| MuJoCo is authoritative and stable. | Already checked in the TODO with RMA-032/RMA-060 through RMA-065 evidence. | Keep checked unless future source changes invalidate the evidence. |
| Full closed-loop Reachy model is rendered from native state. | Already checked in the TODO with RMA-051/RMA-052 evidence and `hidden_kinematic_fallback=false`. | Keep checked unless future source changes invalidate the evidence. |
| Level 1 camera reprojection passes reference tests. | Already checked in the TODO with RMA-104 validation evidence and the reprojection workflow retained. | Keep checked unless future source changes invalidate the evidence. |
| Android on-device ASR and offline TTS work where installed. | Open. RMA-121/RMA-123/RMA-125 source and provider contracts exist; full spoken-turn proof remains open. | Non-fabricated ASR -> local LLM -> behavior -> offline TTS evidence, preferably with networking disabled where required. |
| Selected benchmark-backed local LLM works without blocking physics. | Open. RMA-133 selected Qwen3-0.6B; RMA-134/RMA-135 local runtime/governor evidence exists historically. | Exact-head validation and representative-device profile reconciliation for the release candidate. |
| OpenAI and compatible providers are independently configurable. | Open. Provider configuration, BYOK, and cloud LLM/VLM wiring exist; real cloud-provider success is not claimed by this document. | Exact-head provider validation and intentionally supplied endpoint/credential evidence if a real cloud success claim is made. |
| Behavior planner validates all AI output. | Open. RMA-151/RMA-195 route validated behavior intents through production behavior capability; malformed-provider safety is also covered by RMA-173 exact-head evidence. | Release-candidate evidence that the full current path remains intact under malformed-provider responses and regeneration limits. |
| No silent provider, privacy, calibration, or kinematic fallbacks remain. | Open. RMA-173 and RMA-191 exact-head gates prove key fail-closed and security/privacy boundaries. | Final release-candidate review after remaining RMA-190/RMA-125/RMA-184 evidence gaps are resolved or explicitly deferred. |
| Diagnostics identify active fidelity, providers, and failures. | Open. RMA-170/RMA-171/RMA-172 diagnostics and bundle export exist; RMA-191/RMA-193 release-review contracts passed on the current executable source line. | Final release-candidate audit that diagnostics remain wired to active fidelity/provider/failure state. |
| Representative-device performance report exists. | Open. RMA-184 defines low/mid/high classes and has partial low/A54 measurements; measured mid/high default-profile closure remains pending. | Complete the remaining physical representative-device measurements and target reconciliation. |
| Licenses and attribution are complete. | Open. RMA-012/RMA-192 exact-head review now mechanically binds repository inventory, app notices, Reachy attribution/share-alike obligations, no-endorsement disclosure, and selected-model presentation. | RMA-194 license row still needs final release-candidate packaging/release audit even though RMA-192 is complete. |

## Current open release blockers

The current release gate remains blocked by the following evidence gaps:

- RMA-002: second independently provisioned Unity/Android build environment evidence remains missing.
- RMA-125 / RMA-190: physical offline speech and spoken end-to-end scenarios still require device proof with suitable installed Android on-device ASR and exact offline TTS services.
- RMA-184 still needs detailed measured default profiles and representative-device target reconciliation.
- RMA-190: the first three core scenarios have physical evidence, while camera/VLM, speech/local-LLM, cloud-provider, failure-injection, model-corruption/OOM, and solver-fault scenarios remain open.
- RMA-194 license row still needs final release-candidate packaging/release audit even though RMA-192 is complete.
- RMA-194: the remaining release checklist and acceptance criteria must be reconciled only after the evidence above is complete or explicitly deferred into a named later milestone with rationale.

## Release-gate blockers

1. Generic hosted CI and several status-publisher workflows continue to show zero-step/`JOB_NOT_FOUND` infrastructure failures.
2. Full RMA-125/RMA-190 spoken-turn evidence remains missing: on-device ASR utterance -> local LLM -> behavior -> offline TTS with the required offline/network-disabled conditions, or an explicitly accepted non-fabricated ASR acceptance method.
3. RMA-002 still requires second independently provisioned Unity/Android build evidence.
4. RMA-184 still needs detailed measured default profiles and representative-device target reconciliation.
5. RMA-190 still needs exact-head evidence for the remaining end-to-end scenarios not covered by the current core A54 pass.
6. The final packaging/license row must use readable exact-head evidence; detailed rows are not reconstructed from redacted output, and partial reconstruction from redacted inventory output is not acceptable.

## Completed release-review evidence requiring release-gate care

- RMA-173: exact-head silent-failure regression run `36823974671` passed all eight fail-closed cases, but the TODO section remains conservative until separately reconciled.
- RMA-191: exact-head release-review run `36823974604` passed the security/privacy contracts, and the authoritative TODO section is reconciled closed.
- RMA-192: exact-head release-review run `36823974604` passed the license/attribution contracts, and the authoritative TODO section is reconciled closed.
- RMA-193: exact-head release-review run `36823974604` passed the release-documentation contracts, and the authoritative TODO section is reconciled closed.

## Non-blocker classifications

- Artifact upload quota failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload.
- Artifact-publication failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload.
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

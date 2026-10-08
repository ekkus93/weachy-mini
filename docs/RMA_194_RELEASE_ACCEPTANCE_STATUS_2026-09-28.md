# RMA-194 Release Acceptance Status — updated 2026-10-07

This document records the current release-gate status for the Reachy Mini Android digital twin. It is intentionally conservative: implementation and static contracts may exist before exact-head CI, Local Unity Android Validation, physical-device, provider, performance, privacy, license, and release-acceptance evidence is complete.

It is a planning/status artifact only. It does **not** close any checkbox in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`. TODO checkbox closure must wait for exact current-master evidence, accepted evidence, and authoritative TODO reconciliation.

## Release status

**Release status: not accepted.** No final release tag should be cut from the current tree, and no TODO checkbox in RMA-194 should be closed merely because this status document exists. No final release label is assigned.

The authoritative TODO has been reconciled beyond the original 2026-09-28 status snapshot. Current checked RMA-194 rows have evidence elsewhere in the TODO and associated validation docs; remaining unchecked rows are still genuine evidence gaps, not formatting omissions. The release-review boundary still explicitly includes RMA-191 security/privacy, RMA-192 license/attribution, RMA-193 documentation, and RMA-194 release-acceptance status contracts.

## Current checked RMA-194 rows

| RMA-194 item | Current evidence/status | Care boundary |
| --- | --- | --- |
| App launches and runs offline on supported hardware. | Checked in the authoritative TODO after RMA-190 tranche 1 physically accepted offline launch on a pinned Android device. | Keep checked only while release-candidate evidence remains compatible with that path. |
| MuJoCo is authoritative and stable. | Checked with RMA-032/RMA-060 through RMA-065 evidence. | Keep checked unless future source changes invalidate the evidence. |
| Full closed-loop Reachy model is rendered from native state. | Checked with RMA-051/RMA-052 native-state rendering evidence and no hidden kinematic fallback. | Keep checked unless future source changes invalidate the evidence. |
| Level 1 camera reprojection passes reference tests. | Checked with RMA-104 validation evidence and the reprojection workflow retained. | Keep checked unless future source changes invalidate the evidence. |
| OpenAI and compatible providers are independently configurable. | Checked after RMA-140 through RMA-143 plus RMA-195 provider-configuration wiring established independent OpenAI-compatible configuration. | Do not claim a successful external provider call; real cloud-provider success is not claimed without intentionally supplied endpoint/credential evidence. |
| Behavior planner validates all AI output. | Checked by the RMA-150/RMA-151 behavior-intent contracts and the shipping RMA-195 composition path. | Malformed-provider and regeneration-limit safety remain covered by RMA-173 release-review contracts. |
| No silent provider, privacy, calibration, or kinematic fallbacks remain. | Checked after exact-head RMA-173 and release-review gates proved fail-closed provider/camera/solver/calibration behavior and security/privacy boundaries. | New optional provider, camera, calibration, or animation paths must preserve explicit unavailable/degraded/faulted outcomes. |
| Diagnostics identify active fidelity, providers, and failures. | Checked with RMA-170/RMA-171/RMA-172 diagnostics and release-review contracts. | Release candidates still need an audit that active runtime state is what diagnostics report. |
| Licenses and attribution are complete. | Checked after RMA-012/RMA-192 reconciled the repository inventory, in-app notices, Reachy attribution/share-alike obligations, no-endorsement disclosure, and selected-model presentation. | Final packaging must still include the reconciled notice set; detailed rows are not reconstructed from redacted output, and partial reconstruction from redacted inventory output is not acceptable. |

## Current unchecked RMA-194 rows

| RMA-194 item | Current evidence/status | Blocker before checkbox closure |
| --- | --- | --- |
| Android on-device ASR and offline TTS work where installed. | Open. RMA-121/RMA-123/RMA-125 source and provider contracts exist; the full spoken-turn proof remains open. | Non-fabricated physical ASR -> local LLM -> behavior -> offline TTS evidence with suitable installed Android on-device ASR and exact offline TTS services. |
| Selected benchmark-backed local LLM works without blocking physics. | Open. RMA-133 selected Qwen3-0.6B; RMA-134/RMA-135 local runtime/governor evidence exists historically, and current exact-head RMA-134/RMA-135 validation is queued. | Exact-head physical validation and representative-device profile reconciliation for the release candidate. |
| Representative-device performance report exists. | Open. RMA-184 defines low/mid/high classes and captures the required metadata, but measured default-profile publication remains pending. | Complete the remaining physical representative-device measurements and target reconciliation. |

## RMA-190 status snapshot

RMA-190 remains **in progress**. The former RMA-195 composition blocker is closed.
The authoritative TODO records these accepted scenarios:

- Offline launch with no network.
- MuJoCo full model load and neutral reset.
- Deterministic gesture replay.
- Front camera -> head rotation -> transformed Reachy-eye frame.
- Independent cloud provider combinations.
- Permission denial/revocation.
- Network loss, rate limit, malformed response, and cancellation.

The following scenarios remain open until their own end-to-end evidence passes:

- On-device ASR -> local LLM -> behavior -> offline TTS.
- Rear-camera visual question with optional VLM.
- Model corruption/OOM.
- Solver fault and controlled reset.

Physical core/camera/permission evidence and the deterministic provider-integration
evidence are cited in the authoritative RMA-190 section. Provider run `37757168563`
passed on `b9f4d7397630f6f6392f2dee9bf586ab1bb68f7b`, using explicitly labeled
fixture endpoints rather than live cloud credentials. Model-failure fixture
integration run `37759478803` passed on
`6f3019c4c9e8a8c963161267defaff71a966a658` with artifact `11541646432`;
that scenario remains unchecked pending authoritative acceptance reconciliation.

## Current exact-head evidence on `master`

Current `master` before this status-only reconciliation was `e618c6adb7109d37e5f736df3a7796fc46d2f1c6` (`RMA-190: format model failure guard`). At that head:

- `CI` run `37676348629` passed.
- `Local Unity Android Validation` run `37676348632` passed.
- `RMA Release Review Contracts` run `37676348616` passed, including the RMA-191, RMA-192, RMA-193, and RMA-194 contract set.
- `RMA-173 Silent Failure Regressions` run `37676348614` passed.
- `Publish Hosted CI Status` run `37676548144` passed.
- `Publish Local Unity Validation Status` run `37679004837` passed.

The conservative CI outcome for that head remains pending while physical/self-hosted validations continue or wait in queue. Pending or queued physical validations are not source-code failures and are not evidence for closing additional physical/performance rows.

## Current open release blockers

The current release gate remains blocked by these evidence gaps:

1. RMA-002 still requires second independently provisioned Unity/Android build evidence.
2. Full RMA-125/RMA-190 spoken-turn evidence remains missing: physical offline speech and spoken end-to-end scenarios still require device proof with suitable installed Android on-device ASR and exact offline TTS services.
3. RMA-134 / RMA-135 / RMA-194: local-LLM physical timing/governor evidence must be current for the release-candidate head.
4. RMA-184 still needs detailed measured default profiles and representative-device target reconciliation.
5. RMA-190: rear-camera/VLM, model-corruption/OOM, and solver-fault/reset scenarios remain open; provider-combination and network/rate-limit/malformed/cancellation scenarios have accepted deterministic integration evidence.
6. RMA-194 license row still needs final release-candidate packaging/release audit, and the remaining release checklist and acceptance criteria must be reconciled only after the evidence above is complete or explicitly deferred into a named later milestone with rationale.

Several hosted workflows have previously shown repository-wide zero-step/`JOB_NOT_FOUND` infrastructure failures. Those failures are classified as CI infrastructure failures until a workflow exposes failed steps, logs, or another actionable source diagnostic.

## Release-gate blockers versus non-blockers

Artifact upload quota failures are infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload. Artifact-publication failures are also infrastructure failures, not source failures, when the relevant acceptance step completed before artifact upload. Zero-step CI failures with unavailable logs are infrastructure failures, not source failures.

Infrastructure failures such as zero-step hosted CI jobs, missing job logs, artifact-storage/upload failures, pending self-hosted jobs, runner outages, or transient bridge/API failures are not release evidence and are not source-code failures. They should keep affected checklist items open while independent source, documentation, security, privacy, license, and release-gate work continues.

A physical Android run blocked by secure keyguard, device disconnection, missing installed ASR/TTS services, or unavailable runner hardware is a hardware availability condition, not a source failure. Lack of physical Reachy Mini hardware is not a release blocker for the virtual Reachy Mini product unless a later optional physical-calibration milestone explicitly requires it.

A compiler error, test failure, lint/formatting failure, merge conflict, or implementation defect remains normal repository work and must be diagnosed and fixed before treating that head as qualified.

## Deferred-feature boundary

A permanent shipping-source regression tripwire is documented in
`docs/validation/RMA_194_DEFERRED_FEATURE_SHIPPING_SCOPE_CONTRACT_2026-10-08.md`.
It checks Unity package dependencies, the fixed non-navigable presentation
camera bootstrap, and known AR/XR/depth/scene runtime activation patterns.
This is source-level prevention, not a completed release-candidate audit;
the corresponding authoritative TODO checkbox remains open.

The initial release must not half-enable deferred future milestones. Level 2 depth reprojection, Level 3 scene reconstruction, AR placement, movable observer camera, and Reachy-specific VLA or learned social-motion policy remain future milestones and must not be labeled as active release features.

The correct behavior for missing deferred capability is an explicit unavailable, unsupported, or deferred state. The app must not provide a hidden fallback path that treats Level 1 reprojection as depth-aware, substitutes stale camera content, labels nominal dynamics as calibrated physical behavior, or silently promotes cloud/provider paths across a privacy boundary.

## Release labeling rule

No calibrated twin label is allowed without a named physical calibration profile and held-out physical evidence. No final release label is assigned by this document. A future release candidate must choose one of the documented labels — geometric baseline, dynamic baseline, servo-fidelity, or calibrated twin — strictly according to the evidence actually accepted at that candidate head.

When the remaining evidence is available, update this file together with the authoritative TODO and include exact commit SHA, workflow run IDs, physical-device reports, artifact digests, tested device matrix, fidelity label, known limitations, and confirmation that every final handoff reference exists at the stated repository path.

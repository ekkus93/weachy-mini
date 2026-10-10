# RMA-194 Release Acceptance Status — updated 2026-10-08

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
| Selected benchmark-backed local LLM works without blocking physics. | Checked from exact-head physical RMA-134 run `37731000395` on `33332e81498dc174181cc00fce916f68bb14f9a3`: the shipping harness verified the selected Qwen3 0.6B artifact, offline generation, and finite physics p95 at or below the 2 ms budget. | This narrow row does not claim sustained thermal or 1,800-second representative-device performance; those remain RMA-184 gates. |

## Current unchecked RMA-194 rows

| RMA-194 item | Current evidence/status | Blocker before checkbox closure |
| --- | --- | --- |
| Android on-device ASR and offline TTS work where installed. | Open. RMA-121/RMA-123/RMA-125 source and provider contracts exist; the full spoken-turn proof remains open. | Non-fabricated physical ASR -> local LLM -> behavior -> offline TTS evidence with suitable installed Android on-device ASR and exact offline TTS services. |
| Representative-device performance report exists. | Open. RMA-184 defines low/mid/high classes and publishes measured 300-second A54 30/60 FPS default profiles from physical run `36187273962` (`docs/validation/RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md`). Camera, local LLM, audio, and network workloads were not exercised in those profile captures. | Qualify 1,800-second integrated long-run stability, thermal degradation order, and representative supported-device simulation/interaction targets; the A54 default-profile capture alone does not meet these gates. |

## Final acceptance criteria status

The three final acceptance criteria below are distinct from the individual
RMA-194 release-checklist rows. This table is reconciled against the
authoritative TODO by `scripts/tests/test_rma194_release_acceptance_status.py`.
A static regression guard does not constitute an APK/release-candidate audit,
and no unqualified criterion is promoted by this table.

| RMA-194 acceptance criterion | State | Required closure evidence |
| --- | --- | --- |
| All incomplete items are either resolved or moved to an explicitly named later milestone with rationale. | Open. | Resolve the remaining RMA-002/125/184/190 evidence gaps or explicitly defer each under a named, approved milestone with rationale; reconcile the TODO. |
| No deferred Level 2, Level 3, AR, movable observer camera, or VLA code is half-enabled in production. | Open. | Inspect the actual release-candidate source, shipping Bootstrap scene, Unity dependencies and built Android package at the candidate SHA; retain exact-head hosted static contracts and packaging evidence. |
| The release is labeled accurately as geometric baseline, dynamic baseline, servo-fidelity, or calibrated twin according to completed evidence. | Open. | Select a final label only after release-candidate acceptance and match it to the model-fidelity audit, physical evidence and supported-device matrix; no calibrated claim without a named validated physical profile. |

## RMA-190 status snapshot

RMA-190 remains **in progress**. The former RMA-195 composition blocker is closed.
The authoritative TODO records these accepted scenarios:

- Offline launch with no network.
- MuJoCo full model load and neutral reset.
- Deterministic gesture replay.
- Front camera -> head rotation -> transformed Reachy-eye frame.
- Rear-camera visual question with optional VLM.
- Independent cloud provider combinations.
- Permission denial/revocation.
- Network loss, rate limit, malformed response, and cancellation.
- Model corruption/OOM.
- Solver fault and controlled reset.

The following scenarios remain open until their own end-to-end evidence passes:

- On-device ASR -> local LLM -> behavior -> offline TTS.

Physical core/camera/permission evidence and the deterministic provider-integration
evidence are cited in the authoritative RMA-190 section. Provider run `37757168563`
passed on `b9f4d7397630f6f6392f2dee9bf586ab1bb68f7b`, using explicitly labeled
fixture endpoints rather than live cloud credentials. Model-failure fixture
integration run `37759478803` passed on
`6f3019c4c9e8a8c963161267defaff71a966a658` with artifact `11541646432`.
The authoritative RMA-190 section closes that scenario using explicitly labeled
deterministic package/OOM fixtures; this is not physical memory or performance
evidence and does not close RMA-135 or RMA-184. Solver-fault/controlled-reset
integration is also accepted from exact-head CI run `37760354266` on
`405711d5dd0d03253959742dcf66a957012ad0a8`; that managed/native fixture proves
fail-closed worker fault retention, explicit handle recreation, neutral reset, and
resumed finite authoritative progress without claiming physical-device evidence.

Rear-camera/VLM physical end-to-end acceptance passed on exact implementation
SHA `3895bb93f5faef7fef1169ffeb747a4347f88c21`: run `37994546094`,
job `114037107333`, uploaded artifact `11648195872` (zip SHA-256
`0631940a8066e69c118b2c723efb5a8098714940413c6318cbf611a6be4d55a6`).
The Samsung SM-A546E exercised the real rear camera, authoritative transformed
eye frame, shipping VLM image encoder, secure credential path, and production
HTTP transport against an explicitly identified loopback fixture. This is not
live-cloud, measured calibration, or performance evidence. The one remaining
RMA-190 scenario is the offline spoken turn.

## Current exact-head evidence on `master`

Historical verified source baseline (not current `master`): `e618c6adb7109d37e5f736df3a7796fc46d2f1c6` (`RMA-190: format model failure guard`). At that earlier head:

- `CI` run `37676348629` passed.
- `Local Unity Android Validation` run `37676348632` passed.
- `RMA Release Review Contracts` run `37676348616` passed, including the RMA-191, RMA-192, RMA-193, and RMA-194 contract set.
- `RMA-173 Silent Failure Regressions` run `37676348614` passed.
- `Publish Hosted CI Status` run `37676548144` passed.
- `Publish Local Unity Validation Status` run `37679004837` passed.

Later source head `97ae8c976d5f183652acbb796bee9cd291dbdf7c` passed exact-head hosted CI `37842094416`, release-review contracts `37842094313`, and silent-failure regression `37842094334`. Physical RMA-190 rear-camera/VLM acceptance `37842094321` was still queued at the time of this reconciliation; none of these hosted passes establish new physical acceptance. The historical baseline above is not a claim of current-master SHA after this documentation change. Pending or queued physical validations are not source-code failures and are not evidence for closing additional physical/performance rows.

## Current open release blockers

The current release gate remains blocked by these evidence gaps:

1. RMA-002 still requires second independently provisioned Unity/Android build evidence.
2. Full RMA-125/RMA-190 spoken-turn evidence remains missing: physical offline speech and spoken end-to-end scenarios still require device proof with suitable installed Android on-device ASR and exact offline TTS services.
3. RMA-184 has published measured A54 30/60 FPS default profiles (physical run `36187273962`); the 1,800-second integrated long-run, thermal-order, and representative-device supported-target acceptance remain open. The measured profiles do not cover camera/LLM/audio/network integration.
4. RMA-190: rear-camera/VLM physical shipping-path acceptance is closed by run `37994546094`; the remaining open scenario is the real offline ASR -> local LLM -> behavior -> TTS spoken turn (also required by RMA-125).
5. RMA-194 license row still needs final release-candidate packaging/release audit, and the remaining release checklist and acceptance criteria must be reconciled only after the evidence above is complete or explicitly deferred into a named later milestone with rationale.

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

## Built-APK audit integration (2026-10-08)

The shared production Android device-feasibility build script now removes its
previous output APK before invoking Unity, then runs
`scripts/verify_rma194_release_apk.py` against the newly built
`Builds/Android/weachy-mini-device-arm64-api26.apk`. Both existing physical
Android workflows invoke this shared script, so an absent, stale, malformed,
or deferred AR/XR-containing APK fails before installation or artifact upload.
The audit requires the ARM64 MuJoCo, Reachy simulation, and local-LLM runtime
libraries and Unity data; synthetic positive/negative fixtures and build-script
ordering contracts run in hosted CI.

Implementation landed in commits `64bfe05371703f9e092954ca5f3ba4a628fb45c1`,
`431e847b9fd7cd6cdbb6c7def5c49a7d8263798d`, and
`bc1f384b30b8b9e0883f0e47e4a80353b6a80cd1`. On the latter exact head,
hosted `CI` run `37882472371`, `RMA Release Review Contracts` run
`37882472332`, and `RMA-173 Silent Failure Regressions` run `37882472297`
passed. These prove source and fixture checks, **not** inspection of an actual
release-candidate APK. Physical builds remain pending; earlier device workflows
`37877892219` and `37877892200` could not pin the disconnected Android device
(the `adb devices` inventory was empty). No RMA-194 acceptance checkbox is
closed by this integration. The production-built APK must be inspected and
its SHA-256 report retained once an Android build can run.

## Permanent fidelity-evidence regression contract

The RMA-194 release-acceptance test now reads the model-parameter audit and
shipping settings/metadata defaults. It rejects a model audit promoted above
the uncalibrated upstream geometric baseline, an optimistic calibrated
fidelity default, or a release-status claim of physical calibration without
the separately required held-out evidence. This is a static drift guard,
not a device measurement or final release label. The release-acceptance
checkboxes remain open pending the release-candidate audit and physical gates.

## Release labeling rule

No calibrated twin label is allowed without a named physical calibration profile and held-out physical evidence. No final release label is assigned by this document. A future release candidate must choose one of the documented labels — geometric baseline, dynamic baseline, servo-fidelity, or calibrated twin — strictly according to the evidence actually accepted at that candidate head.

When the remaining evidence is available, update this file together with the authoritative TODO and include exact commit SHA, workflow run IDs, physical-device reports, artifact digests, tested device matrix, fidelity label, known limitations, and confirmation that every final handoff reference exists at the stated repository path.

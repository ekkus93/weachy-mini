# Codex handoff — Reachy Mini Android digital twin (2026-10-12)

**Repository:** `ekkus93/weachy-mini`  
**Authoritative branch:** `master` — user explicitly requires **direct commits to master, no per-task branches or PRs**.  
**Snapshot source HEAD before this document-only handoff commit:** `c30776ba06da10fdfa18ebcd709ec864387ad582`  
**Handoff timestamp:** 2026-10-12, after exact-head A54 CI results became terminal.  
**Status:** The current A54 device regression builds and runs pass. **Initial release acceptance is not complete.**  
**Next owner:** Codex.

> This is an evidence-based transfer of the last observed state, not a release approval. After reading this file, inspect fresh `master`, current CI, and the authoritative TODO; source and workflow state may have advanced. The handoff commit changes documentation only.

## 1. Read first — authoritative sources and operating rules

- [Implementation TODO](REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md): **authoritative task/acceptance ledger**; inspect all checkbox states before acting.
- [Normative specification](REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md): security, architecture, physics, fidelity, provider, and feature constraints.
- [Current implementation status](IMPLEMENTATION_STATUS.md): useful engineering background, but the top-line `Updated: 2026-07-31` header is stale; use the dated evidence and latest TODO/CI as the current truth.
- [Release documentation](REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md#rma-194--release-acceptance-checklist) and [physical speech blocker](blockers/RMA-125_PHYSICAL_OFFLINE_SPEECH_BLOCKER.md).
- The user has required **Ralph Bridge rather than the default GitHub tool** for GitHub and CI work. Previous ChatGPT work used Ralph Bridge for exact-head state/CI and direct-to-`master` commits. Never create a branch/PR simply to implement each task. Inspect existing relevant Ralph branches/PRs to avoid duplicate work before any optional reconciliation.
- Fail closed: no cloud/provider substitution, leaked private audio/images/secrets, forged test evidence, weakened physics or thermal budgets, automatic calibration claims, cosmetic replacement of MuJoCo state, source-warning suppressions, or checkbox closure based only on a successful source/unit test.
- Do not implement deferred Level 2/3 depth, AR, mobile observer camera, learned VLA or iOS as part of this initial-release TODO.
- Multiple automated writers may operate on `master`: inspect the fresh HEAD before each write and use exact-head CAS/non-force commit semantics. After any ambiguous write response, reread HEAD and diff rather than blindly retrying.

## 2. Latest verified source change and prior build failure

**Source commit (already on master):** [`c30776ba06da10fdfa18ebcd709ec864387ad582`](https://github.com/ekkus93/weachy-mini/commit/c30776ba06da10fdfa18ebcd709ec864387ad582)  
**Message:** `RMA-002: accept pinned API 37.0 SDK platform metadata in preflight`.  
**Files:** `scripts/verify_toolchain.py` and `scripts/tests/test_rma002_sdk_package_identity.py`.

Context, in order:

1. A prior sequence added installed Android SDK `source.properties` identity checks, invoked by `scripts/build_unity_android.sh`, so directories alone cannot satisfy the pinned toolchain gate. It fixed a first compatibility issue with empty optional Java properties (e.g. `AndroidVersion.CodeName=`).
2. On previous master `137702ebb86e00e2cce12bcaa56ac9bda3691373`, the A54 [workflow 38200939731, **rerun attempt 2**](https://github.com/ekkus93/weachy-mini/actions/runs/38200939731) **passed physical ADB pinning** but failed `Build ARM64 API-26 device APK` because real installed SDK `platforms/android-37.0/source.properties` reported `AndroidVersion.ApiLevel=37.0` while the verifier compared only to the literal `37`.
3. Commit `c30776b` accepts **only the pinned equivalent platform identity forms** `37` and `37.0`; it retains exact Build Tools, NDK, and CMake revision requirements and rejects `36` / `37.1`. Regression tests were added for positive and negative platform forms.
4. The next exact-head A54 builds succeeded (see Section 3). The change is accepted and genuinely exercised by the physical self-hosted build; do **not** reinstall the SDK or weaken the pinned metadata checks to fix an already resolved issue.

Pinned toolchain remains in `toolchain.lock.json`: Unity `6000.5.2f1`; Android compile/target API `37` with package suffix `37.0`; Build Tools `36.0.0`; NDK `28.2.13676358`; CMake `3.31.6`; JDK `17`; AGP `9.3.1`; Gradle `9.5.0`; IL2CPP ARM64. The feasibility APK's Android minimum is API 26; the full product's provisional minimum and explicit on-device ASR prerequisites are separately documented.

## 3. Actual latest CI evidence at the handoff baseline

**Both self-hosted physical A54 workflows at exact SHA `c30776b` completed with `conclusion=success`**, verified through Ralph Bridge `get_ci_status`:

| Workflow / run | Result | What passed |
| --- | --- | --- |
| [Local Unity Android Device Validation No Artifacts — 38296774091](https://github.com/ekkus93/weachy-mini/actions/runs/38296774091) | **SUCCESS** | Pinned A54 device selection, Unity tests and production MuJoCo staging, newly built ARM64 API-26 APK, release APK verification, instrumented RMA-190 **core** E2E, RMA-090/091/092 camera path, RMA-111 tracking, RMA-154 visual servo, RMA-022 lifecycle, RMA-195 application composition, and authoritative rendering. |
| [Local Unity Android Validation — 38296774170](https://github.com/ekkus93/weachy-mini/actions/runs/38296774170) | **SUCCESS** | Pinned A54, Unity build and physical camera/lifecycle/application/rendering regressions, corresponding validation evidence and APK artifact uploads, non-gating RMA-184 device capability capture. |
| [Original A54 rerun 38200939731, attempt 2](https://github.com/ekkus93/weachy-mini/actions/runs/38200939731) | **FAILURE (superseded)** | ADB pinning passed, but the source preflight rejected `37.0` metadata; fixed by `c30776b` and verified by both successful runs above. |
| [Hosted CI — 38296774250](https://github.com/ekkus93/weachy-mini/actions/runs/38296774250) | **FAILURE, infrastructure/unexecuted** | All five jobs (Android, static, Reachy model, managed, native) reported failure with **zero steps observed**. Do **not** claim tested code failed these jobs; investigate job initialization, runner allocation, GitHub policy/billing, workflow permissions or Actions service condition. |
| [Release review — 38296774209](https://github.com/ekkus93/weachy-mini/actions/runs/38296774209) | **FAILURE, infrastructure/unexecuted** | One job with **zero steps observed**. |
| [RMA-173 silent-failure regressions — 38296774238](https://github.com/ekkus93/weachy-mini/actions/runs/38296774238) | **FAILURE, inspect infrastructure** | This run also failed without useful executed-step evidence in the most recent inspection; inspect status/job records before attributing to code. |

**Important evidence scope:** RMA-190 `core` in the passing A54 workflow does **not** implement/prove the last unchecked *actual human speech* scenario (on-device ASR → locally selected LLM → real behavior → offline audible TTS). The RMA-184 capabilities probe is **non-gating** and is not the integrated 1,800-second performance/thermal soak. Nor does one successful A54 build establish two independently provisioned developer environments.

Prior accepted regression/benchmark baselines documented in the TODO include RMA-190 front/rear camera, permissions/provider/model-failure/solver-fault scenarios, and physical RMA-134 local model plus physics p95. These historical completions remain valid for their specific tested commits/scopes; require new exact-head checks when source changes touch them.

## 4. The complete unchecked acceptance ledger (11 boxes)

A scan of all 3,602 lines of `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` at `c30776b` identified **11 unchecked boxes**. **Do not mark any of these complete just because the A54 regressions in Section 3 passed.**

| RMA | Open criterion (unchecked in the TODO) | What is still required |
| --- | --- | --- |
| **002** | Two clean developer environments build the same minimal Android application | Provision and independently prove a second **clean** Unity/Android toolchain/build environment; record exact pins, APK/toolchain evidence, clean-machine independence and repeatability. Current `kawa` alone is insufficient. |
| **125** | Real Android on-device ASR + offline TTS conversation with networking disabled | Human audible utterance and response through **shipping** microphone/on-device speech provider and exact-locale offline voice, with all networking disabled. |
| **125** | Missing speech services produce visible setup guidance | Physical missing/unavailable ASR/TTS language/voice scenarios with actionable UI diagnostics and no silent substitution. |
| **125** | No audio sent to cloud unless explicitly selected | Physical negative privacy/fallback check and source/telemetry evidence tied to real spoken-turn execution. |
| **184** | No unbounded memory or authoritative state-lag growth over a long run | Representative physical **1,800-second** combined production MuJoCo + selected local GGUF + UI/telemetry run, with bounded peak/growth metrics. |
| **184** | Thermal degradation follows the prescribed priority order | Actual sampled thermal pressure, governor decisions and protected 500 Hz physics; confirm ordering, not simulated telemetry or widened budgets. |
| **184** | Supported devices meet simulation and interaction targets | Complete and publish measured low/mid/high class matrix, real device class/probe and supported-with-limitations outcomes; physical representative-device runs. |
| **190** | On-device ASR → local LLM → behavior → offline TTS | Production-path **human-utterance** end-to-end on a real phone with networking disabled; validate behavior intent and actual audio focus/TTS lifecycle. |
| **194** | Android on-device ASR and offline TTS work where installed | Release acceptance is blocked on RMA-125 physical evidence; don't duplicate or waive. |
| **194** | Representative-device performance report exists | RMA-184 physical qualification matrix/report and limitations must be published. |
| **194** | All incomplete items resolved or explicitly moved to a named later milestone with rationale | Final explicit gate reconciliation and release decision **after** evidence; user approval if an initial-release criterion is to change milestone. |

All remaining RMA-190 scenarios except spoken-turn are checked. RMA-191 security/privacy, RMA-192 licenses/attribution, RMA-193 documentation, and the real RMA-195 shipping composition are historically implemented and accepted under their stated tests.

## 5. RMA-125 / RMA-190 — physical network-disabled spoken turn (highest priority)

Read [RMA-125 physical blocker](blockers/RMA-125_PHYSICAL_OFFLINE_SPEECH_BLOCKER.md). The A54 (`SM-A546E`, ARM64, Android API 36, approved ADB serial `R5CW31AX4FL`) is **currently proven visible** to the self-hosted runner in both exact-head passing workflows. Prior A54 validation established explicit `SpeechRecognizer.isOnDeviceRecognitionAvailable` and an installed offline TTS voice; **service-readiness is not the complete test**.

Implemented dedicated acceptance entry points exist:

- Manual workflow named **`RMA-190 Offline Speech End-to-End Android Acceptance`** (locate exact workflow YAML before dispatching; do not invent a Ralph profile).
- `scripts/run_rma190_offline_speech_end_to_end_android.sh`.
- `ReachyRma190OfflineSpeechEndToEndAcceptance` application harness.
- The shipping `ReachyMainScreen.RequestMicrophone()`, RMA-121 explicit on-device ASR, RMA-123 offline TTS, selected RMA-133 Qwen3-0.6B Q4_K_M through `LocalModelPackageManager`, and real behavior/simulation providers.

**Requires an actual human at the A54.** Disable Wi-Fi/mobile data (the harness handles network state and restoration), speak a real utterance into its microphone, require real ASR callback/transcript → production local model validated intent → deterministic behavior and native authoritative actuation → real audible TTS with listening focus released before speaking focus. Capture exact APK/build SHA, device, provider/voice identity, network-off evidence, finite physics/target state, sanitized callback/focus/timing/results. Never inject a synthetic transcript, record/store raw microphone audio, substitute cloud speech, or report success without actual audio.

Separately exercise: Android audio-route/focus interruption; no microphone/TTS overlap or self-transcription; missing local service/model/voice with user-visible setup guidance; no implicit provider/network fallback. A single successful positive spoken turn may close the RMA-190 scenario, **not automatically all three RMA-125 boxes** until these negative paths are genuinely covered.

## 6. RMA-184 — sustained workload, hardware classes and thermal limitation

The integrated opt-in RMA-184 soak is attached to the **`RMA-135 resource governor Android acceptance`** workflow. Read TODO RMA-184, `scripts/rma184_select_performance_class.py` and the RMA-135/184 validation docs and workflow source for the exact opt-in fields and dispatch mechanism. It is an **1,800-second**, production-runtime/full-model test, with the selected real local model, resource governor, actual RMA-181 degradation decisions, render/physics p95, memory growth, authoritative state-lag growth and decode throughput. Artifact upload is nonblocking; failure of acceptance metrics is blocking.

Historical caution: [`docs/validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md`](validation/RMA_135_SM_A546E_THERMAL_FINDING_2026-08-17.md) documents real rapid thermal throttling on the A54 under combined on-device MuJoCo+LLM load (~15–16 seconds), followed by governed inference suspensions and genuine physics deadline misses. Later RMA-135/184 extended acceptance failed with this type of pressure; do not mask it by raising thresholds, increasing retries, dropping required sustained load, or claiming a lighter/cloud-offloaded comparison as local inference acceptance. Correct action is to record measured support limitations, profile-class results, and an explicitly reviewed product decision if limits cannot be met.

The earlier **300-second** A54 profile capture is published in [`docs/validation/RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md`](validation/RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md), but does **not** fulfill 1,800-second or high-class qualification. The LG-H872 low-class device runs Android API 26; the shipping thermal signal is `Unavailable` on API 28 or below. It **cannot by itself** qualify the physical thermal-order gate: use a representative low-class phone supporting API 29+ thermal telemetry, or obtain an explicit reviewed alternative acceptance plan. High-class measurements also need a genuinely representative high-class device.

## 7. RMA-002 — independently reproducible clean-machine build

`scripts/verify_toolchain.py` verifies the locked manifest and installed SDK package identities. `scripts/build_unity_android.sh` enforces the metadata preflight **before** calling Unity. `scripts/tests/test_rma002_sdk_package_identity.py` includes the `37`/`37.0` cases introduced on the handoff baseline.

The A54 runner has now proved a build on one machine. For the open requirement, choose a separate independently provisioned host (not a second working directory or merely a second job on the same preprovisioned host); capture clean checkout, SDK/Unity installation/provisioning, pinned package hashes/metadata where applicable, exact source SHA, build result, APK identity and reproducibility comparison. Keep failure semantics strict for mismatched CMake/NDK/SDK packages. Reconcile acceptance only when both real machines' evidence meets the canonical criteria.

Existing relevant work may include a Ralph branch/PR called `ralph/rma002-two-environment-build-evidence` (historical PR #85). Inspect its current provenance/state before using it; **do not** create a new branch, unnecessary PR, or duplicate evidence.

## 8. Hosted GitHub Actions infrastructure — separate from source defects

On baseline `c30776b`, hosted CI, RMA release contracts, and RMA-173 produced failed jobs **without executing steps**, while the self-hosted Android/Unity workflows completed successfully. This is **not** proof the source failed Ruff, native, Android, or managed tests.

Codex should check exact-run GitHub Actions job metadata for runner provisioning, billing/quota, policies, disabled actions, workflow permissions, GitHub service errors, and absence/presence of real step execution. Restore meaningful hosted CI and requalify the exact candidate commit. If real tests then fail, diagnose and fix them; distinguish them from initial zero-step job failures. Do not ignore a required CI gate or downgrade it to satisfy release acceptance.

## 9. Ralph Bridge status and previous incident

The connected Ralph Bridge `v0.6.0` reported build `66909315f85923c0a9616162f07968c421ca5336`, server instance `9ec51a87914f412bac18b4d1b9b9697d` (started `2026-10-12T18:03:00.932638Z`), authentication `app`.

Earlier `rerun_failed_jobs`, `rerun_failed_job`, and `dispatch_validation` returned an opaque outer `INVALID_ARGUMENT` even though repository reads worked. The operator/Codex adjusted GitHub App settings; after that, the exact-head failed-jobs rerun was **accepted** with Ralph operation ID `c7895a25c954491aa253e28722db62a3`, proving the Phase 2 CI-write path is working for that operation. Subsequent `commit_files` successfully produced and verified `c30776b` on `master`. The earlier bug report is in the Ralph Bridge repository at [`docs/RALPH_BRIDGE_PHASE2_CI_WRITE_REJECTIONS_2026-10-12.md`](https://github.com/ekkus93/chatgpt_ralph_loop_tools/blob/master/docs/RALPH_BRIDGE_PHASE2_CI_WRITE_REJECTIONS_2026-10-12.md).

The source connector intermittently returned `INVALID_ARGUMENT` while requesting a log window of a **running** job; CI status and later completed-job inspection continued to work. Do not treat this as unresolved wholesale read/write failure without a fresh reproduction. Preserve and report actual failing tool envelopes if any reappear.

## 10. Suggested execution order for Codex

1. Reload `master`, the authoritative TODO/spec, this handoff and all relevant in-flight CI/PR information. Confirm no other automated writer has advanced HEAD; reconcile existing work before starting. Observe the successful baseline runs and their artifacts.
2. Restore hosted zero-step CI execution, then run/review current exact-head static, native, managed, Android, security/release and silent-failure gates. Fix actual failures at source without bypasses.
3. Perform the manual real-person offline spoken-turn experiment on the pinned A54 and negative speech/focus/setup/privacy tests. Record real evidence with device and source provenance. Close only proven RMA-125/RMA-190 acceptance criteria.
4. Run/diagnose RMA-184's physical 1,800-second production combined-load soak and collect representative low/mid/high hardware evidence; publish measured limits, do not weaken physics/thermal governor.
5. Complete the independent two-clean-machine RMA-002 Android build proof; do not mistake two CI jobs on one host for two separate environments.
6. Reconcile RMA-194 release decision and all 11 TODO checkboxes against exact durable test artifacts, or obtain explicit user agreement for any named-milestone deferral. Preserve the actual fidelity label **`geometric baseline (uncalibrated)`**; do not claim calibrated digital twin.
7. Update canonical TODO and status evidence, run full exact-head gates, document known limitations and final manifest/device/source/revision references. Only then consider release/tag as instructed by the user.

## 11. Handoff acceptance boundaries and provenance

The project is a **virtual** Reachy Mini on Android and does not require physical Reachy robot hardware. It uses the pinned Reachy source model (`a739a6e461eb6d722901f1cfc225265ffc85c28d`), MuJoCo 3.9.0 (pinned source `237c17e48539b6c90bf90d3161547cbdcbfaa1e0`), genuine native authoritative physics, and a selected Qwen3 0.6B Q4_K_M local model. Its audited fidelity is **geometric baseline (uncalibrated)** with placeholders; neither synthetic calibration fixtures nor unit tests justify stronger language. Keep on-device audio, camera data, BYOK secrets and diagnostics private by default.

**No release closure or new acceptance checkboxes are claimed by this handoff.** Its CI results are bound to the **pre-handoff source SHA** `c30776b`; this file's documentation-only commit will have a newer HEAD and may trigger new CI. Codex must reread the post-handoff HEAD, check exact-head CI and cite new evidence before claiming release qualification.

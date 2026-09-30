# RMA-190 Core End-to-End Validation — 2026-09-30

## Scope

This record captures physical Android evidence for the first three RMA-190 scenarios on the shipping application/runtime path:

1. offline launch with no network;
2. MuJoCo full-model load and neutral reset;
3. deterministic gesture replay.

It does **not** claim closure for the remaining RMA-190 scenarios (camera reprojection, spoken local conversation, optional VLM, cloud-provider combinations, permission/network/provider failure matrices, model corruption/OOM, or solver-fault reset). Those remain independently qualified requirements.

## Exact source and workflow evidence

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Exact source SHA: `35928d183fb48d12d228fdd4b8a2f1515db6bb0e`
- Workflow: `Local Unity Android Device Validation No Artifacts`
- Run: `36752801658`
- Job: `110015411312` (`Validate Unity Android device path on A54`)
- Result: **success**
- Physical-device step: `Install and run RMA-190 core end-to-end acceptance` — **success**

The same exact-head job also passed RMA-191/192/193 repository contracts, the RMA-125 A54 speech revalidation contract, Unity tests, the ARM64/API-26 APK build/verification, physical-device pinning, camera, tracking, visual-servo, lifecycle, shipping-composition, and authoritative-rendering device stages.

## Acceptance harness contract

The shipping physical harness is `scripts/run_rma190_core_end_to_end_android.sh`. It installs the built APK, clears application data, disables both Wi-Fi and mobile data before launch, starts the shipping application with the RMA-190 acceptance entry point, waits for the application-produced result, and validates that result fail-closed.

The accepted report must prove all of the following:

- `status == passed`;
- `network_offline == true`;
- `active_network_validated == false`;
- the production simulation service is `ReachyProductionSimulationApplicationService`;
- the production behavior service is `ReachyBaselineBehaviorApplicationService`;
- runtime status is `Running`;
- the model hash is nonzero;
- the authoritative full model exposes 18 bodies, 37 qpos values, 30 qvel values, and 9 actuator observations;
- neutral reset succeeds and advances continuity;
- the deterministic `Acknowledgment` gesture produces a nonempty hashed plan;
- both replay passes submit exactly the planned frame count;
- actual authoritative motion is observed;
- normal behavior is suspended for the direct replay window;
- the two gesture replays agree within the harness tolerance with finite motion/error metrics;
- no human ASR transcript, cloud credential, or local-model payload is injected by the harness.

This is production-path evidence rather than a fixture represented as physical input: the harness explicitly asserts that human ASR, cloud credentials, and a local model were **not** injected.

## RMA-190 reconciliation

The evidence above satisfies the physical acceptance boundary for these three authoritative TODO scenarios:

- **Offline launch with no network** — passed.
- **MuJoCo full model load and neutral reset** — passed.
- **Deterministic gesture replay** — passed.

The remaining RMA-190 checkboxes must stay open until their own required end-to-end evidence exists. In particular, this run does not convert the existing camera subsystem stages into proof of the specific `Front camera -> head rotation -> transformed Reachy-eye frame` scenario, and it does not claim spoken ASR/LLM/TTS, provider-combination, corruption/OOM, or solver-fault acceptance.

## Adjacent exact-head evidence

On the same source SHA:

- RMA-173 Silent Failure Regressions run `36752801842` passed all eight fail-closed obligations.
- RMA Release Review Contracts run `36752801891` passed the canonical RMA-191 security/privacy, RMA-192 license, RMA-193 documentation, and RMA-194 release-status contracts.
- The no-artifacts device workflow run `36752801658` passed the A54 physical-device path without depending on GitHub artifact upload availability.

The ordinary `Local Unity Android Validation` run `36752801584` passed Unity tests, built/verified the APK, pinned the physical device, and passed RMA-090 camera discovery, but failed at `Upload RMA-090 camera discovery evidence`. The corresponding no-artifacts physical workflow passed the full stack. That upload failure is therefore infrastructure/artifact-publication evidence, not a source or physical-device failure.

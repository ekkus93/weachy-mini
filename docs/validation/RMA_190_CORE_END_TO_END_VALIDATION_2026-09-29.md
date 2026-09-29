# RMA-190 Core End-to-End Validation — 2026-09-29

## Scope

This document records the exact-head physical-device evidence for the first RMA-190 automated end-to-end tranche.

The validated tranche covers only scenarios implemented by the shipping `ReachyRma190CoreEndToEndAcceptance` harness:

1. offline application launch with network disabled;
2. production MuJoCo full-model load and deterministic neutral reset;
3. deterministic baseline gesture replay through the production behavior controller target sink and authoritative MuJoCo state.

The harness explicitly does **not** close the remaining RMA-190 scenarios for camera/VLM, speech/local-LLM, cloud-provider combinations, network/provider failures, model corruption/OOM, or solver-fault recovery.

## Exact source under test

- Repository: `ekkus93/weachy-mini`
- Branch: `master`
- Commit: `c8495d346206fd85d100d6b7e17d1a2c4f1cb4a7`
- Commit message: `RMA-173: document silent-failure regression coverage`

## Validation run

- Workflow: `Local Unity Android Device Validation No Artifacts`
- Run: `36628711072`
- Job: `109612187221`
- Job name: `Validate Unity Android device path on A54`
- Result: success
- Event: push
- Attempt: 1

The run completed successfully on the exact source commit above. Ralph's synthesized CI report observed one job and no failing steps.

## Successful stages observed

The successful job completed all of the following stages:

- repository checkout;
- Python setup;
- local Unity and Android installation resolution;
- generated Reachy Unity presentation preparation;
- production MuJoCo runtime staging for Unity Android;
- Unity tests;
- ARM64 API-26 device APK build;
- ARM64 API-26 device APK verification;
- physical Android device pinning;
- `RMA-190 core end-to-end acceptance`;
- `RMA-090 camera discovery acceptance`;
- `RMA-091 camera acquisition acceptance`;
- `RMA-092 camera texture acceptance`;
- `RMA-111 lightweight tracking acceptance`;
- `RMA-154 visual-servo acceptance`;
- `RMA-022 lifecycle acceptance`;
- `RMA-195 application-composition acceptance`;
- authoritative rendering acceptance.

## RMA-190 scenario mapping

| RMA-190 scenario | Result from this run | Evidence boundary |
| --- | --- | --- |
| Offline launch with no network | Accepted by the RMA-190 core harness. | The harness captures a network snapshot after the shipping host is operational and fails if a validated Internet-capable active network is present. |
| MuJoCo full model load and neutral reset | Accepted by the RMA-190 core harness. | The harness requires the shipping production simulation service, waits for the production authoritative runtime, verifies nonzero model hash and 18-body model layout, performs neutral reset, and verifies continuity advancement. |
| Deterministic gesture replay | Accepted by the RMA-190 core harness. | The harness builds a deterministic acknowledgment trajectory, executes it twice through `ReachyProductionBehaviorControllerTargetSink` into the authoritative runtime, requires observed actuator motion, and compares final authoritative qpos/qvel within the configured tolerance. |
| Front camera -> head rotation -> transformed Reachy-eye frame | Not closed by this run. | Camera acquisition, texture, tracking, visual-servo, and composition stages passed, but this RMA-190 scenario still requires a dedicated live transformed-frame E2E assertion. |
| On-device ASR -> local LLM -> behavior -> offline TTS | Not closed by this run. | The RMA-195 composition stage verifies application wiring, but RMA-125/RMA-190 spoken-turn physical proof remains separate. |
| Rear-camera visual question with optional VLM | Not closed by this run. | No credentialed or optional VLM request was exercised by this core harness. |
| Independent cloud provider combinations | Not closed by this run. | No cloud credentials or live cloud-provider execution was injected. |
| Permission denial/revocation | Not closed by this run. | Earlier RMA camera/lifecycle stages cover their own scopes, but this RMA-190 scenario remains a separate E2E scenario. |
| Network loss, rate limit, malformed response, and cancellation | Not closed by this run. | The core harness runs with no cloud/provider failure injection. |
| Model corruption/OOM | Not closed by this run. | No model-corruption or OOM path was injected. |
| Solver fault and controlled reset | Not closed by this run. | No solver fault was injected into the shipping runtime during this harness. |

## Roadmap reconciliation target

This validation supports marking the following RMA-190 scenario checkboxes complete in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` once the roadmap file is edited safely:

- `Offline launch with no network.`
- `MuJoCo full model load and neutral reset.`
- `Deterministic gesture replay.`

Do not use this evidence to mark any other RMA-190 scenario complete.

## CI and infrastructure notes

Hosted CI for this source line was still exhibiting the separate zero-step/bootstrap failure pattern observed on adjacent commits. That hosted failure had empty job step lists and did not produce source-level failure evidence.

The physical A54 no-artifacts workflow above is the authoritative evidence for this RMA-190 core E2E tranche and for the physical-device stages it explicitly ran.

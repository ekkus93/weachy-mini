# RMA-190 automated end-to-end scenarios

**Date:** 2026-09-21
**Roadmap item:** `RMA-190`
**Authoritative roadmap:** `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`

## 1. Purpose

RMA-190 proves that the independently qualified subsystems are connected through
real application paths and remain fail-closed when a dependency is unavailable.
The suite must not turn lower-level unit tests into an end-to-end claim by merely
listing them together. A scenario is credited only when one acceptance harness
crosses the relevant production boundaries and records machine-readable evidence.

RMA-195 has lifted the original composition-layer blocker for simulation, camera,
speech, provider selection, perception, behavior, persistence, and the live
conversation coordinator. RMA-190 may therefore proceed in bounded tranches.
Physical speech closure remains dependent on a device whose explicit on-device
ASR service/language support is actually usable; a failing ASR preflight is
reported as a blocker rather than replaced by system/cloud recognition.

## 2. Truthfulness rules

- Physical scenarios use the shipping `ReachyApplicationHostBehaviour` and
  `ReachySettingsApplicationCompositionProvider`; no alternate composition may
  stand in for the installed app.
- MuJoCo remains authoritative. A scenario may not use requested targets, Unity
  animation, or synthetic transforms as proof of physical robot motion.
- Hardware-dependent scenarios may use deterministic fixtures only for external
  stimuli whose fixture nature is explicitly recorded. A fixture cannot be
  reported as a real camera frame, spoken utterance, cloud response, or physical
  calibration measurement.
- Network-offline scenarios must disable device network transports outside the
  application, verify the resulting Android connectivity state from inside the
  player, and restore the prior device state in cleanup.
- Cloud scenarios use test-owned endpoints/credentials or explicit user-provided
  providers. Credentials are never committed and raw secrets are never emitted
  in reports.
- Missing hardware, model artifacts, provider credentials, permissions, or
  calibration evidence is a visible blocked/failed scenario, not a pass.

## 3. Scenario matrix

| Roadmap scenario | Primary execution class | Current implementation |
| --- | --- | --- |
| Offline launch with no network | Physical Android / shipping host | Passed: run `36806841674`, SHA `1c741bce...` |
| MuJoCo full model load and neutral reset | Physical Android / production runtime | Passed: run `36806841674`, SHA `1c741bce...` |
| Deterministic gesture replay | Physical Android / production target sink + authoritative state | Passed: run `36806841674`, SHA `1c741bce...` |
| Front camera -> head rotation -> transformed Reachy-eye frame | Physical Android / real camera + calibration/warp | Passed: run `37605466118`, SHA `859aa4ed...` |
| On-device ASR -> local LLM -> behavior -> offline TTS | Physical Android / live conversation turn | Open; explicit on-device ASR physical preflight currently blocks the SM-A546E path |
| Rear-camera visual question with optional VLM | Physical Android / real frame + VLM | Open |
| Independent cloud provider combinations | Deterministic provider integration + optional physical endpoint | Passed: run `37757168563`, SHA `b9f4d739...` |
| Permission denial/revocation | Physical Android + deterministic application-state assertions | Passed: run `37605466032`, SHA `859aa4ed...` |
| Network loss, rate limit, malformed response, cancellation | Deterministic HTTP/provider integration + physical cancellation where needed | Passed: run `37757168563`, SHA `b9f4d739...` |
| Model corruption/OOM | Local-model package/provider integration | Passed: run `37759478803`, SHA `6f3019c4...` |
| Solver fault and controlled reset | Native-backed worker + application/runtime recovery | Open |

## 4. Tranche 1 contract

`ReachyRma190CoreEndToEndAcceptance` is started only by the Android launch extra
`reachy_rma190_core_e2e_acceptance`. `scripts/run_rma190_core_end_to_end_android.sh`
installs the exact APK, clears application data so no old cloud/model selection can
contaminate the offline launch, saves the device Wi-Fi/mobile-data state, disables
both transports, launches the shipping application, validates the JSON report, and
restores the previous network state unconditionally.

The player-side harness must prove all of the following in one run:

1. The shipping application host reaches `Ready` or `Degraded` without validated
   Internet connectivity.
2. The resolved simulation/behavior services are the production implementations.
3. The production authoritative runtime is `Running`, has a non-zero model hash,
   and exposes the pinned full-model dimensions: 18 rendered bodies, 37 qpos,
   30 qvel, and nine actuator observations.
4. `ResetNeutral()` succeeds, advances continuity, and settles to finite state.
5. The shipping behavior loop is temporarily suspended only to prevent competing
   commands while the acceptance harness drives the same production controller
   target sink used by behavior execution.
6. One deterministic `Acknowledgment` trajectory is generated once, hashed, and
   replayed twice from neutral through `ReachyProductionBehaviorControllerTargetSink`.
   Both executions must submit every planned frame, produce real authoritative
   actuator motion, preserve continuity during each replay, and finish with qpos
   and qvel agreement inside the documented replay tolerance.
7. The report explicitly records that no human ASR transcript, cloud credential,
   or local model was injected in this tranche.

The harness runs in the dedicated `rma190-core-end-to-end.yml` physical workflow.
It is intentionally independent of RMA-195 physical composition acceptance, so a
separate speech-service failure cannot prevent collection of these three core
RMA-190 scenario results.

## 5. Tranche 2 front-camera contract

`ReachyRma190CameraEndToEndAcceptance` runs only when launched with
`reachy_rma190_camera_e2e_acceptance`. The runner grants only Android camera
permission, starts the shipping front-camera acquisition path, and validates a
machine-readable report fail-closed.

The player-side harness uses the real CameraX front-camera stream, the production
authoritative MuJoCo runtime, the production behavior controller target sink, and
`ReachyAndroidPerceptionDriver`'s existing `TryBuildFrame` path. It pauses the
continuous behavior/perception loops only to avoid competing controller and GPU
pipeline work while the deterministic scenario is measured.

Because RMA-190 is validating integration rather than performing a new physical
calibration experiment, this tranche temporarily installs an explicitly labeled
`UncalibratedEstimate` calibration fixture derived from the live front-camera
dimensions and the authoritative baseline camera-body pose. The report records
that the fixture is deterministic and **not** a measured calibration, and the
original durable calibration set is restored in cleanup. No synthetic camera
frame or requested controller target is accepted as proof.

A pass requires a baseline observation-eligible `TransformedReachyEye` frame,
real authoritative head rotation from a deterministic Curiosity trajectory, and
a second transformed frame whose camera source sequence and authoritative state
sequence both advance on the same camera session and continuity. This harness
passed in physical Android workflow `37605466118` on exact SHA
`859aa4ed9fc04030e5056511d027af83e0baa85a`, producing artifact
`11475530644` (`rma190-camera-end-to-end-report-859aa4ed9fc04030e5056511d027af83e0baa85a`).

## 6. Tranche 2 permission contract

`ReachyRma190PermissionEndToEndAcceptance` proves the shipping application keeps
permission denial and revocation visible without substituting a fake camera,
stale frame, or alternate provider path. The scenario passed in physical Android
workflow `37605466032` on exact SHA
`859aa4ed9fc04030e5056511d027af83e0baa85a` on a pinned physical Android device.

## 7. Completion policy

The first three RMA-190 roadmap checkboxes are closed by physical Android run
`36806841674` on exact implementation SHA
`1c741bced63e62d5dcac5130f9ee9e981660b6cd`, with artifact
`11137798819`. That run executes the shipping host/runtime and production
behavior target sink with device networking disabled outside the application.

The front-camera transformed-frame checkbox is closed by physical Android run
`37605466118` on exact implementation SHA
`859aa4ed9fc04030e5056511d027af83e0baa85a`, with artifact
`11475530644`. The permission denial/revocation checkbox is closed by physical
Android run `37605466032` on the same exact SHA.

The independent cloud-provider-combination and network-loss/rate-limit/malformed-
response/cancellation scenarios are closed by deterministic-provider-integration
workflow run `37757168563` on exact SHA
`b9f4d7397630f6f6392f2dee9bf586ab1bb68f7b`, with artifact `11540502224`.
The harness uses the production OpenAI-compatible adapters, shared bounded HTTP
transport, behavior-intent validation, settings isolation, and fallback policy.
Its endpoints are explicit deterministic fixtures, which this specification permits
for provider integration; the evidence does not claim a successful live-cloud call.

The model-corruption/OOM scenario is closed by deterministic local-model
package/provider integration workflow run `37759478803` on exact SHA
`6f3019c4c9e8a8c963161267defaff71a966a658`, with artifact `11541646432`.
The same acceptance process crosses the production package manager and
`LocalLlmProvider`: installed corruption is revalidated and rejected, and
deterministic OOM fault injection produces typed resource exhaustion, a faulted
provider, explicit reload, and successful post-reload validated generation with
no hidden retry or provider fallback. The fixture is deliberately not represented
as a physical memory-pressure or performance measurement.

The remaining three roadmap scenarios remain open until their own end-to-end harnesses
exist and pass. Existing lower-level RMA-090 through RMA-195 evidence is supporting
evidence but is not substituted for the remaining RMA-190 scenario results.

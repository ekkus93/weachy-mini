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
| Offline launch with no network | Physical Android / shipping host | Tranche 1 harness implemented; physical evidence pending |
| MuJoCo full model load and neutral reset | Physical Android / production runtime | Tranche 1 harness implemented; physical evidence pending |
| Deterministic gesture replay | Physical Android / production target sink + authoritative state | Tranche 1 harness implemented; physical evidence pending |
| Front camera -> head rotation -> transformed Reachy-eye frame | Physical Android / real camera + calibration/warp | Tranche 2 harness implemented; physical evidence pending |
| On-device ASR -> local LLM -> behavior -> offline TTS | Physical Android / live conversation turn | Open; explicit on-device ASR physical preflight currently blocks the SM-A546E path |
| Rear-camera visual question with optional VLM | Physical Android / real frame + VLM | Open |
| Independent cloud provider combinations | Deterministic provider integration + optional physical endpoint | Open |
| Permission denial/revocation | Physical Android + deterministic application-state assertions | Open |
| Network loss, rate limit, malformed response, cancellation | Deterministic HTTP/provider integration + physical cancellation where needed | Managed failure-injection harness implemented; exact-head evidence pending |
| Model corruption/OOM | Local-model package/provider integration | Open |
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
sequence both advance on the same camera session and continuity. The roadmap
checkbox remains open until this harness passes on a physical Android device.

## 6. Provider failure-injection contract

`Rma190ProviderFailureEndToEndTests` drives the real
`OpenAiChatCompletionsLlmProvider` through `ReachySharedHttpTransport` using
a deterministic HTTP handler. It does not replace the provider with a result
fake, and it never needs a real credential or remote endpoint.

The single managed fixture covers:

1. connection loss via an `HttpRequestException`, which must surface as
   `HttpFailure` after exactly one transport attempt;
2. HTTP 429 rate limiting, which must remain a visible `HttpFailure` with no
   hidden retry;
3. a malformed provider payload that contains no recognizable assistant
   response, which must never become a successful behavior intent; and
4. in-flight caller cancellation after the HTTP send begins, which must reach
   the transport token and surface as `Cancelled`, not success or timeout.

Every case asserts `Succeeded == false` and an exact single-attempt count. The
static contract in `scripts/tests/test_rma190_provider_failure_e2e.py` keeps the
four required failure modes and no-hidden-retry assertions registered. The
roadmap checkbox remains open until the managed fixture passes on an exact
commit.

## 7. Completion policy

The first three RMA-190 roadmap checkboxes remain open until the physical Android
run passes on an exact commit and its artifact is recorded in the roadmap/validation
record. Implementation alone is not completion evidence.

The remaining eight roadmap scenarios remain open until their own end-to-end
harnesses exist and pass. Existing lower-level RMA-090 through RMA-195 evidence is
supporting evidence but is not substituted for the RMA-190 scenario result.

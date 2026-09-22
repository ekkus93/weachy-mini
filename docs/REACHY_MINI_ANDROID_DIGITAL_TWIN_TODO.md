      not missing application-composition wiring. Source-contract coverage in
      `scripts/tests/test_rma195_application_composition.py` permanently
      requires both the full behavior-intent capability and exact offline
      voice policy.
- [x] `ReachyProductionApplicationCompositionProvider` is confirmed dead code
      and has been removed. `ReachyMainScreenBootstrap` constructs only
      `ReachySettingsApplicationCompositionProvider`; the permanent
      `scripts/tests/test_rma195_application_composition.py` contract fails if
      the obsolete production-composition file reappears or the shipping
      bootstrap references it.

**RMA-195 composition blocker lifted (2026-09-21).** The shipping composition
now exposes the real simulation, camera, speech, provider, perception, behavior,
persistence, and UI services, with live conversation and on-demand transformed
frame paths wired. RMA-190 now owns end-to-end scenario implementation and
physical qualification; RMA-125/RMA-135/RMA-184 retain their separately
documented device-specific acceptance gaps.

## RMA-190 — Build automated end-to-end scenarios

**Status:** In progress. The former RMA-195 composition blocker is closed.
Each scenario remains unchecked until its own end-to-end evidence passes; a
hardware-dependent scenario may be implemented while physical evidence remains
pending, but implementation alone does not close its checkbox.

- [x] Offline launch with no network.
- [x] MuJoCo full model load and neutral reset.
- [x] Deterministic gesture replay.
- [ ] Front camera -> head rotation -> transformed Reachy-eye frame.

**Core end-to-end physical evidence (2026-09-22):** dedicated A54 run
`35768977213` passed on exact master SHA
`b188ce3b18635d24da79cb762e245c52e361ab5e`. The shipping Android app was
cold-launched with Wi-Fi/mobile data disabled, validated the production MuJoCo
full-model layout and two neutral resets, and replayed the same production
acknowledgment trajectory twice against authoritative state. The fail-closed
report required all 9 actuator observations, continuity advancement, actual
authoritative motion, and replay qpos/qvel error within the committed tolerance;
no ASR transcript, cloud credential, or local-model fixture was injected.

The earlier front-camera workflow run `35683866454` did not reach the camera
scenario because APK compilation failed in the shared RMA-190 SHA-256 helper;
that shared compiler defect is fixed on current master. The camera scenario
therefore remains open until a fresh physical run reaches and passes its
dedicated fail-closed report.

- [ ] On-device ASR -> local LLM -> behavior -> offline TTS.
- [ ] Rear-camera visual question with optional VLM.
- [ ] Independent cloud provider combinations.
- [ ] Permission denial/revocation.
- [ ] Network loss, rate limit, malformed response, and cancellation.
- [ ] Model corruption/OOM.
- [ ] Solver fault and controlled reset.

## RMA-191 — Complete privacy and security review

- [ ] Verify no embedded keys.
- [ ] Verify Keystore usage.
- [ ] Verify network security configuration.
- [ ] Verify provider disclosure before cloud-bound audio/image/text.
- [ ] Verify logs and diagnostics redaction.
- [ ] Verify temporary media cleanup.
- [ ] Review native buffer and lifetime safety.

## RMA-192 — Complete license and attribution review

- [ ] Reconcile packaged dependencies/assets against inventory.
- [ ] Verify notices in repository and app.
- [ ] Verify modified Reachy-derived assets have required attribution/share-alike treatment.
- [ ] Verify no endorsement claim.
- [ ] Verify selected local model redistribution/download presentation.

## RMA-193 — Complete documentation

- [ ] Build instructions.
- [ ] Android device requirements.

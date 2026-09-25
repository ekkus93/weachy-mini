# RMA-190/RMA-194 consolidation current A54 status — 2026-09-25

## Purpose

This addendum records the latest Ralph-audited A54 evidence and the current hardware boundary after the earlier consolidation documents were written.

Authoritative source baseline for all evidence branches audited here remains current `master` at `b188ce3b18635d24da79cb762e245c52e361ab5e` unless the row explicitly identifies a temporary workflow head.

## Device availability boundary

The A54 is no longer available to the self-hosted runner. No new A54 or Android-device validation should be dispatched until the device is reconnected/unlocked and the user explicitly confirms that it is available again.

Work that does not require the A54 can continue: source audits, duplicate-lane reconciliation, documentation-only evidence records, static/source-contract review, and CI infrastructure classification.

## Newly audited exact evidence

| Area | Evidence branch / PR | Evidence head or run | Ralph-audited result | Closure boundary |
| --- | --- | --- | --- | --- |
| RMA-190 rear-camera visual E2E | PR #43, `ralph/rma190-rear-visual-e2e` | current PR head `55036222afa6b30c6505c6646f94cdb9840aada9`; run `36179670744` | `A54 RMA-190 Rear Visual E2E` passed at the current PR head. Steps included source-contract validation, Unity/Android setup, presentation prep, production runtime staging, APK build/verify, A54 pin, and rear-camera visual acceptance. | Supports rear visual lane, but generic PR CI still fails zero-step/`JOB_NOT_FOUND`; merge/TODO closure remain blocked by clean-mergeability infrastructure. |
| RMA-190 permission denial/revocation | PR #53, `ralph/rma190-permission-end-to-end` | current PR head `8f672b3b77a5f5be4ff9a620e05687aebf4466c8`; run `36197725278` | The A54 workflow built the APK, verified it, and pinned the physical device, then failed in the acceptance step because the phone was awake but still behind secure keyguard: `mKeyguardShowing=true`; the foreground helper correctly refused to bypass an unoccluded PIN/pattern/password keyguard. | Hardware/keyguard condition, not a source failure. Leave the lane open and rerun only after the A54 is available and unlocked. |
| RMA-154 visual servo | PR #77, `ralph/rma154-a54-visual-servo-evidence` | evidence head `4d898660a1c4f76a3cbafa81c8233c62c7675f33`; temp workflow head `e6825fdb72539efb8bcd7b487e4b4a80ac71d306`; run `36148919716` | `A54 RMA-154 Visual Servo` passed. The docs-only evidence branch adds `docs/validation/RMA_154_A54_VISUAL_SERVO_VALIDATION_2026-09-25.md` and no source changes. | Supporting representative-device evidence only; does not close broader RMA-190 rear visual or spoken-turn scenarios. |
| RMA-195 application composition | PR #78, `ralph/rma195-a54-application-composition-evidence` | evidence head `1de44522801a7b4fd80d76c3fa59393f9eaf3f18`; temp workflow head `323f5692b153b7bbb3d9d05b7ed7fc66cb34ee3b`; run `36172744278` | `A54 RMA-195 Application Composition` passed. The physical run exercised shipping app composition, camera/speech/provider/behavior stack exposure, on-device ASR readiness, and offline TTS start/completion. | Supports RMA-190/RMA-194 readiness but does not complete full ASR -> local LLM -> behavior -> offline TTS spoken-turn acceptance. |
| RMA-125 network-disabled speech prerequisite | PR #79, `ralph/rma125-network-disabled-speech-evidence` | evidence head `bc59b100d44a4140a4816eef05ac02b58d53e872`; temp workflow head `c6493a7bef9c592df7567a2f53ffeb55a3857648`; run `36182986407` | `A54 RMA-125 Network-Disabled Speech Probe` passed. The workflow disabled Wi-Fi/mobile data, verified network reachability failed, then ran the shipping RMA-195 composition path with on-device ASR readiness and offline TTS completion. | Prerequisite evidence only; full RMA-125 conversation and RMA-190 spoken-turn criteria remain open until non-fabricated ASR transcript/turn evidence exists. |
| RMA-184 representative-device default profiles | PR #81, `ralph/rma184-a54-profile-capture-evidence` | evidence head `67f179f563562ce2c471e6305dfa8a8dffe89bb1`; pass run `36187273962`; extraction run `36192045766` | `A54 RMA-184 Default Profiles` passed. The intentional extraction failure emitted Ralph-readable metrics after acceptance passed: 30 FPS and 60 FPS NativePhysics/UnityRendering rows, with non-exercised workloads explicitly marked unavailable. | Supports RMA-184 measured default profiles and RMA-194 representative-device performance-report evidence after merge/TODO reconciliation; does not claim camera/local-LLM/audio/network workload success in that session. |

## Current CI/merge gate status

All audited documentation-only evidence branches still hit the same generic PR CI infrastructure pattern: jobs fail with no failed steps and Ralph reports `JOB_NOT_FOUND`/unavailable logs. These failures are not classified as source failures.

Until generic CI/mergeability recovers, keep evidence branches open and ready, but do not mark authoritative TODO checkboxes complete on `master` solely from unmerged evidence branches.

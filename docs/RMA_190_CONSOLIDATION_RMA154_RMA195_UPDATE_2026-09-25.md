# RMA-190 Consolidation Update — RMA-154/RMA-195 A54 Evidence — 2026-09-25

This note updates the RMA-190 consolidation/control record with additional physical A54 validation gathered after the earlier duplicate-lane audit.

## RMA-154 visual-servo A54 evidence

- Evidence PR: #77 (`ralph/rma154-a54-visual-servo-evidence`)
- Evidence branch head: `4d898660a1c4f76a3cbafa81c8233c62c7675f33`
- Temporary workflow branch head: `e6825fdb72539efb8bcd7b487e4b4a80ac71d306`
- Workflow run: `36148919716`
- Workflow: `A54 RMA-154 Visual Servo`
- Result: success

The run passed source contracts, Unity/Android setup, generated-presentation preparation, Android runtime staging, ARM64 API-26 APK build/verification, physical A54 pinning, and `Run RMA-154 visual-servo acceptance`.

This evidence supports camera/perception-driven behavior and representative Android qualification, but it does not close the full RMA-190 rear-camera visual-question/VLM scenario by itself.

## RMA-195 application-composition A54 evidence

- Evidence PR: #78 (`ralph/rma195-a54-application-composition-evidence`)
- Evidence branch head: `1de44522801a7b4fd80d76c3fa59393f9eaf3f18`
- Temporary workflow branch head: `323f5692b153b7bbb3d9d05b7ed7fc66cb34ee3b`
- Workflow run: `36172744278`
- Workflow: `A54 RMA-195 Application Composition`
- Result: success

The run passed source contracts, Unity/Android setup, generated-presentation preparation, Android runtime staging, ARM64 API-26 APK build/verification, physical A54 pinning, and `Run RMA-195 application-composition acceptance`.

The RMA-195 harness validates the shipping application host composition, real Android camera availability/permission, behavior-intent dispatch, provider/perception capabilities, explicit on-device ASR readiness, and offline TTS availability/completion with a non-network voice. It intentionally does not inject a human ASR transcript, cloud credentials, or local-model fixtures.

## Remaining scenario closure boundaries

Even with the RMA-154 and RMA-195 passes, the following RMA-190 boundaries remain open until scenario-specific evidence exists and is merged/reconciled:

- full spoken turn: on-device ASR utterance -> local LLM -> behavior -> offline TTS;
- rear-camera visual question with optional VLM;
- provider/network/model/solver failure-mode branches still blocked by the generic CI merge gate;
- final TODO reconciliation on current `master`.

## Merge-gate status

Both documentation-only evidence PRs currently hit the same generic zero-step/log-unavailable CI failure pattern (`JOB_NOT_FOUND`) seen across the repo. Their focused physical evidence is valid, but the PRs are merge-gated until the generic CI/mergeability issue is resolved.

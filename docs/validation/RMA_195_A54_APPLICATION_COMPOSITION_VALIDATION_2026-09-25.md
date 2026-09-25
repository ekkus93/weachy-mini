# RMA-195 A54 Application-Composition Validation — 2026-09-25

## Scope

This note records a physical Android validation run for the RMA-195 application-composition acceptance path on the Samsung A54-class device.

The validation was run from a temporary branch-scoped workflow whose app source was equivalent to current `master` at:

- `b188ce3b18635d24da79cb762e245c52e361ab5e`
- Temporary workflow branch head: `323f5692b153b7bbb3d9d05b7ed7fc66cb34ee3b`

The temporary workflow file is not intended to merge. This documentation branch preserves the evidence without adding the one-off workflow to `master`.

## Physical validation evidence

- Workflow: `A54 RMA-195 Application Composition`
- Run ID: `36172744278`
- Job: `RMA-195 application composition on A54`
- Result: `success`
- Device class: physical Samsung A54-class Android ARM64 API-31+ device pinned by the workflow.

Successful steps reported by Ralph Bridge:

1. Check out repository.
2. Set up Python.
3. Validate RMA-195 harness contracts.
4. Resolve local Unity and Android installations.
5. Prepare generated Reachy Unity presentation.
6. Stage production MuJoCo runtime for Unity Android.
7. Build ARM64 API-26 device APK.
8. Verify ARM64 API-26 device APK.
9. Pin one physical Android device.
10. Run RMA-195 application-composition acceptance.

## Acceptance semantics

The RMA-195 application-composition harness exercises the shipping application host and requires the production services to be present and operational. The device-side acceptance checks include:

- production simulation service composition;
- discovered Android camera service with granted camera permission and front/rear camera availability;
- Android speech-capability service;
- local/cloud LLM capability exposure;
- cloud VLM capability exposure;
- behavior-intent capability and successful behavior intent dispatch;
- settings persistence and main-screen UI services;
- explicit on-device ASR readiness;
- offline TTS availability and completed utterance with a non-network voice.

The harness intentionally does not inject a human ASR transcript, cloud credentials, or a local model fixture.

## Interpretation

This evidence strengthens RMA-190/RMA-194 release readiness because it proves the shipping application composition can expose and exercise the real Android speech/camera/provider/behavior stack on the representative A54 device.

It does not by itself close the full RMA-190 spoken-turn scenario because that scenario still requires a complete on-device ASR utterance → local LLM → behavior → offline TTS turn with scenario-specific evidence. It also does not close rear-camera visual-question/VLM acceptance.

## Follow-up

- Keep the temporary workflow branch out of `master`.
- Use this run as physical evidence during RMA-195/RMA-194/RMA-190 reconciliation once merge-gate CI is healthy.
- Continue to require scenario-specific evidence before closing broader end-to-end TODO checkboxes.

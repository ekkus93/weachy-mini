# Android Release and Operations Guide

This guide is the release-facing entry point for building, configuring, operating, and troubleshooting Weachy Mini on Android. Detailed architecture and validation documents remain authoritative for their individual subsystems; this page links them together without changing those contracts.

## Build instructions

Use the pinned toolchain in `toolchain.lock.json`; do not substitute arbitrary Unity, Android SDK/NDK, Gradle, JDK, .NET, or lint-tool versions. The complete workstation setup, static checks, native build, managed tests, Android bridge, Unity Android build, and physical-device probe commands are documented in [`docs/BUILDING.md`](BUILDING.md).

The normal Android product target is ARM64 (`arm64-v8a`) with IL2CPP. Before publishing a build, run the repository structure/static checks and the relevant managed/native suites, then build the Unity Android artifact from the same source revision. GitHub Actions evidence must be tied to the exact head SHA being released.

## Android device requirements

The application-wide Android deployment floor is ARM64 Android API 26 or newer. The representative-device policy classifies a device that meets the core floor but lacks the front camera, explicit API-31 on-device ASR, or offline TTS as `SupportedWithLimitations`; those optional capability gaps do not raise the global deployment floor.

A device for the full offline speech feature set additionally needs Android API 31 or newer, `SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)`, the target recognition language installed for that explicit on-device recognizer, and an installed language-matching TTS voice that does not require a network connection. A usable device for the broader feature set also needs:

- enough free storage for the app, native MuJoCo assets, diagnostics, and any selected local model;
- a camera exposed through the app's CameraX discovery path for camera/perception features; and
- hardware resources that satisfy the selected local-model memory/thermal policy if local LLM inference is enabled.

Feature availability is fail-closed. Missing camera, speech, model, calibration, provider, storage, or permission prerequisites are reported as unavailable/degraded rather than silently replaced with another provider or fidelity mode. Representative-device qualification remains tracked separately in RMA-184; the presence of instructions here is not a claim that every device class has passed that gate.

## Local model installation and import

The trusted model-package boundary is documented in [`docs/architecture/LOCAL_MODEL_PACKAGE_MANAGEMENT.md`](architecture/LOCAL_MODEL_PACKAGE_MANAGEMENT.md), with manifest rules in [`docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md`](architecture/LOCAL_LLM_MODEL_MANIFEST.md).

The selected local model is described by `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`. The model binary is not bundled in this repository; the manifest records the expected identity, source revision, license, artifact hash, size, and runtime compatibility that an installed artifact must match before it can be used.

The package manager validates manifest identity, expected size and SHA-256, storage headroom, resumable-download metadata, and the installed artifact before exposing an approved path. Corrupt installed artifacts do not produce an approved model path, and package failure never selects a different model or cloud provider automatically.

The current shipping settings UI intentionally labels **INSTALL**, **IMPORT**, **SELECT**, and **DELETE** as unavailable because the Android document/import and installer UI bridge is not complete. This is a visible product limitation, not an invitation to copy arbitrary model files into app storage. Development/acceptance harnesses may provision a model directly for controlled qualification, but those procedures do not turn the unavailable end-user UI into a supported workflow.

## Provider configuration and BYOK warning

Provider execution is selected per workload: ASR, TTS, LLM, and VLM are independent choices. Cloud-capable paths are off/fail-closed until their required profile, credential, and privacy-boundary authorization are present.

For Cloud LLM and Cloud VLM configuration in the app:

1. Open **Settings** and select **Cloud LLM** or **Cloud VLM**.
2. Enter the endpoint base URL and model identifier, and select the supported endpoint style.
3. Save the provider profile.
4. Enter your own API key and choose **SAVE API KEY**. On Android, credential material is stored through the Android Keystore-backed secret store and is not displayed again after saving.
5. Read the displayed data-boundary disclosure and explicitly authorize the cloud workload before use. Revoking authorization makes generation fail closed.

**BYOK warning:** credentials are supplied by the user for the endpoint they choose. Enabling a cloud workload may send the corresponding application data to that endpoint: conversation text for cloud LLM, transformed camera imagery/question text for cloud VLM, and audio only for an explicitly selected cloud speech provider. Weachy Mini does not embed a shared production API key and must not silently switch a local workload to a network provider.

Provider/fallback policy details are defined in [`docs/RMA_146_EXPLICIT_NO_FALLBACK_POLICY_SPEC_2026-08-11.md`](RMA_146_EXPLICIT_NO_FALLBACK_POLICY_SPEC_2026-08-11.md) and the RMA-195 composition record in the authoritative TODO.

## Calibration workflow

Camera reprojection and physical Reachy calibration are separate concerns.

For Android camera reprojection, the production perception path requires an exact camera calibration profile for the discovered camera/resolution before it publishes a Level 1 transformed Reachy-eye frame. Calibration storage is versioned and fail-closed; an absent or incompatible profile is reported rather than replaced with a measured claim. The current in-app **Camera calibration** action is still unavailable, so production camera calibration capture is not yet an end-user workflow. Architecture details are in [`docs/architecture/CALIBRATION_DATA_AND_CAPTURE.md`](architecture/CALIBRATION_DATA_AND_CAPTURE.md) and [`docs/architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md`](architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md).

Physical Reachy Mini calibration is optional for the virtual product and is only required when making measured physical-twin accuracy claims. The optional runbook is [`docs/operations/RMA_074_PHYSICAL_CALIBRATION_RUNBOOK.md`](operations/RMA_074_PHYSICAL_CALIBRATION_RUNBOOK.md). Synthetic or deterministic acceptance fixtures must remain labeled as uncalibrated/test fixtures and must never be presented as measured device calibration.

## Fidelity levels

Use fidelity labels as evidence claims, not UI decoration:

- **Geometric baseline** — model geometry/kinematics is present, without claiming validated dynamic or camera-servo fidelity.
- **Dynamic baseline** — MuJoCo is authoritative and the rendered robot follows native state with the validated baseline dynamics/electrical/mechanical/thermal constraints that have passed their acceptance evidence.
- **Servo-fidelity** — the authoritative dynamics plus validated behavior/perception/servo loop evidence support the corresponding servo-level claim.
- **Calibrated twin** — requires measured, approved calibration evidence for the relevant physical unit/device path; deterministic fixtures and estimated profiles do not qualify.

The release label must match the completion evidence in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`. Do not promote the product to a higher fidelity label merely because code for a later level exists.

## Camera Level 1 approximation and missing pixels

Level 1 camera reprojection is a geometric homography-based approximation from the Android camera into the Reachy-eye view. It is not depth reconstruction, AR scene reconstruction, or a movable observer camera. The coordinate-system and warp contracts are documented in [`docs/architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md`](architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md) and [`docs/architecture/GPU_HOMOGRAPHY_WARP.md`](architecture/GPU_HOMOGRAPHY_WARP.md).

Every transformed frame carries valid-coverage metadata and a validity mask. Pixels outside valid source coverage are **missing**, not invented. Consumers must use only valid pixels; stale pixels from an older frame, upscaled replacements, or hidden synthetic image content are not acceptable substitutes. Coverage propagation and failure behavior are defined in [`docs/architecture/CAMERA_VALID_COVERAGE_POLICY.md`](architecture/CAMERA_VALID_COVERAGE_POLICY.md).

## Troubleshooting and diagnostic bundles

Start with the visible service/settings health messages. Provider, camera, speech, local-model, simulation, storage, and calibration failures are intended to surface as explicit unavailable/degraded/faulted states rather than silent fallbacks.

For a shareable support artifact, use the **Export diagnostic bundle** action in the app HUD. A successful export reports the local output path. Bundles are bounded and redacted by policy; credentials, raw audio, raw images/media, transcripts, and conversation text are excluded. The export contract is documented in [`docs/RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md`](RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md), and structured-log redaction rules are in [`docs/RMA_170_STRUCTURED_LOGGING_SPEC_2026-08-14.md`](RMA_170_STRUCTURED_LOGGING_SPEC_2026-08-14.md).

When reporting a defect, include the exact app/source SHA, Android device model/API level, which provider/fidelity selections were active, the visible failure text, and the redacted diagnostic bundle when available. Do not paste API keys or raw private media into issue reports.

## Privacy and data flow

The default architecture favors local processing and explicit disclosure at network/privacy boundaries:

- MuJoCo simulation and Unity rendering are local.
- Android on-device ASR and an installed offline TTS voice can keep speech input/output local when those capabilities are selected and available.
- Local LLM inference uses an explicitly installed/approved local model and is governed by memory/thermal admission policy.
- Cloud LLM sends conversation text only after the cloud profile/credential and workload authorization are configured.
- Cloud VLM receives only a validated `TransformedReachyEye` image plus the visual question; arbitrary raw camera frames or URLs are not accepted by that provider contract.
- Cloud speech must be explicitly selected; offline speech failures do not silently promote audio to a network provider.
- Android provider credentials are stored through the Android Keystore-backed secret store.
- Structured diagnostics redact secrets and exclude private/raw-media classes from diagnostic bundles by default.

The detailed privacy/fallback contracts are in [`docs/RMA_146_EXPLICIT_NO_FALLBACK_POLICY_SPEC_2026-08-11.md`](RMA_146_EXPLICIT_NO_FALLBACK_POLICY_SPEC_2026-08-11.md), [`docs/RMA_161_CREDENTIAL_LIFECYCLE_SPEC_2026-08-13.md`](RMA_161_CREDENTIAL_LIFECYCLE_SPEC_2026-08-13.md), [`docs/RMA_162_PRIVATE_MEDIA_RETENTION_SPEC_2026-08-13.md`](RMA_162_PRIVATE_MEDIA_RETENTION_SPEC_2026-08-13.md), [`docs/RMA_170_STRUCTURED_LOGGING_SPEC_2026-08-14.md`](RMA_170_STRUCTURED_LOGGING_SPEC_2026-08-14.md), and [`docs/RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md`](RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md).

## Current user-visible limitations

This guide intentionally documents incomplete product surfaces instead of hiding them:

- local-model install/import/select/delete buttons are currently unavailable in the shipping settings UI;
- in-app camera calibration capture is currently unavailable;
- some end-to-end and representative-device physical qualification gates remain open in the authoritative TODO; and
- a feature that lacks its required permission, offline component, local model, calibration, credential, or authorization must remain unavailable rather than silently changing data flow or fidelity.

Treat `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` as the source of completion truth for those remaining release gates.

# RMA-082 Settings Validation

**Date:** 2026-08-03  
**Status:** Complete

## Scope

RMA-082 replaces the placeholder settings panel with seven durable settings
sections:

- independent ASR, TTS, LLM, and VLM provider selection;
- device-camera preference, preview, calibration, and reprojection entry points;
- speech language, voice, and network-status settings;
- local-model package and resource settings;
- simulation fidelity, calibration status, reset, and diagnostics;
- privacy, cloud-bound data indicators, history, and retention;
- licenses and attribution.

The implementation preserves the fixed front/three-quarter Reachy presentation
camera and does not claim that later CameraX, speech, model-package, cloud,
perception, or behavior runtimes are installed.

## Implemented contracts

### Provider and network truthfulness

Each provider kind has an independent selection record. Android-service and
cloud selections are structurally required to use `NetworkRequired`.
Constructing a network-backed selection with an offline label throws before it
can enter application state.

The settings surface displays execution class, connectivity requirement, and
actual availability separately. A stored preference therefore cannot be
mistaken for an operational provider. The derived privacy summary identifies
every selected provider that may send data off device.

### Visible unavailable actions

Camera preview, camera calibration, reprojection diagnostics, and local-model
install/import/select/delete controls remain enabled as explanatory entry
points. Invoking them changes settings status to an explicit unavailable
message naming the missing implementation milestone. No action silently claims
success.

### Durable state

The schema-versioned settings file is stored under
`Application.persistentDataPath`. The persistence service provides:

- durable preference capture and restoration;
- unsupported-value sanitization;
- temporary and backup replacement;
- malformed-file quarantine;
- safe-default recovery;
- visible degraded health with retained diagnostics.

Transient runtime availability is recomputed at startup and is not persisted as
a false capability claim.

### Fixed-camera preservation

The settings UI contains no orbit, pan, zoom, drag, touch-navigation, or
presentation-camera mutation path. Unity tests retain the camera position and
rotation, invoke every settings section and action, and compare the transform
afterward.

## Hosted contract validation

Workflow run `30851077541`, job `91810969892`, passed on exact commit
`96c7113eccca7eec4afc8fb5d346a56e0782126f`.

The hosted gate proved:

- the shared settings core compiles with warnings treated as errors;
- all RMA-080 and RMA-081 managed contracts remain green;
- RMA-082 managed tests pass;
- the exact seven-section vocabulary is present;
- ASR, TTS, LLM, and VLM are independently represented;
- all required settings actions are visible;
- Android-service and cloud choices require a network label;
- off-device selections feed the privacy summary;
- durable capture, apply, schema, and quarantine paths exist;
- all eight production service boundaries remain present;
- no camera-navigation or camera-transform mutation path is introduced.

Commit status `RMA-082 Settings` completed successfully.

## Unity license recovery and compiler findings

The first RMA-082 attempts were blocked before project compilation because the
self-hosted `kawa` runner had no active Unity Editor entitlement. After Unity
Hub was signed in, the rerun resolved active unlimited Unity Personal ULF and
assigned entitlements and loaded the project successfully.

That first licensed compile then exposed two source-level test compatibility
issues rather than a production-runtime defect:

1. Three nullable dereference diagnostics in `ReachyMainScreenTests.cs` were
   treated as errors. The tests now retain explicitly checked health and screen
   snapshots before dereferencing them.
2. Unity's target framework does not provide generic
   `Enum.GetValues<TEnum>()`. The settings test now uses the compatible typed
   cast from `Enum.GetValues(typeof(ReachySettingsSection))`.

No analyzer suppression or reduced warning policy was introduced.

## Unity and installed Android validation

Permanent workflow run `30851077505`, job `91811041976`, passed on exact commit
`96c7113eccca7eec4afc8fb5d346a56e0782126f`.

The run passed every gated stage:

- Unity 6000.5.2f1 license and Android toolchain resolution;
- deterministic Reachy asset import and generated presentation construction;
- production Android MuJoCo runtime staging;
- the complete Unity edit-mode and play-mode suites, including RMA-082 settings,
  fixed-camera, persistence round-trip, corrupt-file quarantine, and
  network-truthfulness tests;
- ARM64 API-26 IL2CPP APK build;
- APK architecture and packaging verification;
- installed LG-phone RMA-022 native lifecycle and pause/resume acceptance;
- installed LG-phone authoritative-rendering acceptance;
- Unity-test, lifecycle, rendering, and APK artifact uploads;
- final `Local Unity Android Validation` success status.

Artifacts:

- Unity tests: artifact `8870723146`, ZIP digest
  `9504e5a38e398b50825937edace31f1f828884d9194952d9b8d8eaba17078cba`;
- lifecycle report: artifact `8870793704`, ZIP digest
  `b240d3ade0b869408ecf59afdbb5a1e1ed3bac16cdd1d06d75d5f758a9d97c1e`;
- authoritative-rendering report: artifact `8870807294`, ZIP digest
  `0f3cd2964d250a64046e2284dbfcea6d4eebec0214e3331c3e541e7e3a333b98`;
- device APK: artifact `8870889900`, ZIP digest
  `3ac9693c0a5bd30a24d48dc25e13455ea375f77bd8eeb3705b0db4819bb2d413`.

## Acceptance decision

RMA-082 is accepted. Every settings domain is implemented, unavailable
capabilities remain visible and actionable, network-backed Android-service and
cloud selections cannot be represented as offline, durable settings failure is
visible, and the exact implementation passed hosted, Unity, ARM64 APK, and
installed-device validation.

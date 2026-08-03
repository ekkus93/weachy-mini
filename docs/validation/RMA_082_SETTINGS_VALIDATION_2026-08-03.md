# RMA-082 Settings Validation

**Date:** 2026-08-03  
**Status:** In progress — implementation and hosted contracts pass; Unity and installed-device validation are blocked by runner licensing

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
camera and does not claim that later runtime integrations are installed.

## Implemented contracts

### Provider and network truthfulness

Each provider kind has an independent selection record. Android-service and
cloud selections are structurally required to use `NetworkRequired`.
Constructing a network-backed selection with an offline label throws before it
can enter application state.

The settings surface displays execution class, connectivity requirement, and
actual availability separately. A stored preference therefore cannot be
mistaken for an operational provider.

### Visible unavailable actions

Camera preview, camera calibration, reprojection diagnostics, and local-model
install/import/select/delete controls remain enabled as explanatory entry
points. Invoking them changes the settings status to an explicit unavailable
message naming the missing implementation milestone.

### Durable state

The schema-versioned settings file is stored under
`Application.persistentDataPath`. Tests cover preference round-trip,
unsupported-value sanitization, atomic replacement, malformed-file quarantine,
safe-default recovery, and degraded health reporting.

### Fixed-camera preservation

The settings UI contains no orbit, pan, zoom, drag, touch-navigation, or
presentation-camera mutation path. Unity tests retain the camera position and
rotation, invoke every settings section and action, and compare the transform
afterward.

## Hosted contract validation

Workflow run `30847149038`, job `91798117294`, passed on source commit
`fb267f9a459e48e5acd33aa9022f73b399f65479`.

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

## Unity and Android validation blocker

The source head was submitted to the permanent self-hosted Unity/Android gate in
workflow run `30847148996` on runner `kawa`.

Attempts:

1. Job `91798116924` was canceled by workflow concurrency during generated
   presentation preparation and did not evaluate source.
2. Job `91798553550` checked out exact commit
   `fb267f9a459e48e5acd33aa9022f73b399f65479`, resolved Unity 6000.5.2f1 and the
   Android toolchain, imported the Reachy assets, and then stopped before project
   compilation with Unity exit status 198.
3. Job `91799008188` repeated the exact-head attempt and stopped at the same
   pre-compilation boundary.

Both completed attempts reported:

- Unity Licensing Client access token unavailable;
- no matching Editor entitlement;
- `com.unity.editor.headless` not found;
- `No valid Unity Editor license found. Please activate your license.`

This is a runner-account licensing failure, not a compile, test, APK, or device
acceptance result. No Unity test count, APK result, or installed-device claim is
made for RMA-082.

Unity's supported license management requires a signed-in Unity Hub account for
Personal or named-user licensing. No Unity credentials, serial, offline license
file, or service-account credentials are stored in this repository, and the
gate was not weakened to bypass licensing.

## Required completion run

After Unity Hub is signed in and a valid license is visible on `kawa`, rerun the
permanent `Local Unity Android Validation` workflow on the then-current exact
`master` SHA. RMA-082 completion requires all of the following on that same SHA:

- generated presentation import with no first-party compiler warnings;
- all Unity edit-mode tests, including the RMA-082 settings tests;
- the Unity play-mode suite;
- ARM64 API-26 IL2CPP APK build and verification;
- installed LG-phone RMA-022 lifecycle acceptance;
- installed LG-phone authoritative-rendering acceptance;
- uploaded test, lifecycle, rendering, and APK artifacts;
- successful `RMA-082 Settings` and `Local Unity Android Validation` commit
  statuses.

## Acceptance decision

RMA-082 is not yet accepted. Its implementation and hosted contracts are in
place, but the mandatory Unity, Android build, and installed-device gates have
not run because `kawa` has no active Unity license. The authoritative TODO must
remain in progress until exact-head device validation succeeds.

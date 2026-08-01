# RMA-081 Main Screen Validation

**Date:** 2026-07-31  
**Status:** Complete

## Scope

RMA-081 adds the first production application composition and the fixed-view
main screen. It preserves the generated authoritative Reachy presentation and
does not claim that later CameraX, speech, provider, perception, behavior, or
settings capabilities are implemented.

## Implemented behavior

The main screen displays:

- the fixed front / three-quarter Reachy presentation;
- the current interaction state and diagnostic detail;
- the active camera;
- the active provider and whether it executes locally or in the cloud;
- microphone, camera selector, settings, and diagnostics controls.

The complete interaction vocabulary is represented by the Unity-independent
`ReachyMainScreenStateStore`: idle, listening, transcribing, thinking, speaking,
interrupted, unavailable, and error. Snapshots are immutable and revisioned;
settings and diagnostics panels are mutually exclusive.

Unavailable controls do not silently no-op. Until their implementation phases,
selecting microphone or camera selection produces an explicit `Unavailable`
state and names the missing capability. The settings entry point identifies
RMA-082 rather than pretending that placeholder settings are active.

## Production composition

The production composition supplies all eight RMA-080 service boundaries. The
authoritative simulation adapter, fixed presentation camera, session state, and
main-screen UI initialize as ready. Audio, provider, perception, and behavior
initialize as explicitly unavailable optional services. The aggregate
application health is therefore `Degraded`, not falsely `Ready`.

Bootstrap is fail closed. It requires the generated `ReachyPresentationRoot`,
the production authoritative runtime, the tagged main camera, fixed
`fixed_front_three_quarter` metadata, and `AcceptsUserNavigation == false`. It
creates exactly one `ReachyApplicationShell`; it does not create a fallback
camera or alternate scene.

## Fixed-camera proof

The main-screen implementation contains no orbit, pan, zoom, drag, touch, or
free-camera input path. Static contract validation rejects common navigation or
camera-transform mutation tokens. Unity tests invoke all controls and assert
that the camera position and rotation remain unchanged and that
`AcceptsUserNavigation` remains false.

## Hosted contract validation

Workflow run `30679297688`, job `91312929646`, passed on exact commit
`61737fe03b370181430f8ecd93a2a240cc9a47b2`.

The hosted contract proved:

- all eight required interaction-state labels;
- unavailable, local, and cloud provider-location labels;
- immutable revisioned state changes;
- mutually exclusive settings and diagnostics panels;
- explicit unavailable-action diagnostics;
- all four required controls;
- all eight production service boundaries;
- the fixed-camera metadata requirement;
- absence of camera-navigation code paths.

## Unity and Android validation

Self-hosted workflow run `30679297685`, job `91312961243`, passed on exact
commit `61737fe03b370181430f8ecd93a2a240cc9a47b2` using the pinned Unity
6000.5.2f1 and Android toolchain on runner `kawa`.

The run proved:

- generated presentation import completed with no first-party compiler warning;
- 74 of 74 Unity edit-mode tests passed, including all three RMA-081 tests;
- one of one Unity play-mode tests passed;
- the ARM64 API-26 IL2CPP APK built and was verified;
- installed LG-phone native lifecycle acceptance passed;
- installed LG-phone authoritative-rendering acceptance passed;
- all expected evidence and APK artifacts uploaded.

Artifacts from that run:

- Unity test results: artifact `8811680206`, ZIP digest
  `e786fe0034693ae3fe170ab5622ff8340981147afd3e16881277d95999d80737`;
- lifecycle report: artifact `8811710487`, ZIP digest
  `391dd17b25dd88c206c04d3c959a1db71b679b97929f9fa8ebcba4db51f13e8b`;
- authoritative-rendering report: artifact `8811715732`, ZIP digest
  `bc26c6a39ab8875b1d753446c863d4a292d49c1e367b0689054ab8b4de04b402`;
- device APK: artifact `8811722921`, ZIP digest
  `0f23dc5cbe138f29ea2af22a2b79131f507e68e7c0f5134b19dd62dd9d5e3b14`.

## Defects found and corrected during validation

1. A constructor used only to prove an exception path triggered managed analyzer
   rule CA1806. The test now retains the result without suppressing analysis.
2. Unity 6000.5.2f1 reported `FindFirstObjectByType` obsolete. Bootstrap now uses
   the non-order-dependent `FindAnyObjectByType` API.
3. Unity nullable analysis found possible dereferences in new test discovery
   code. The tests now establish explicit nullable discovery boundaries before
   using the components.

## Acceptance decision

RMA-081 is accepted. The application has a production fixed-view shell, an
accurate state and capability presentation, explicit control entry points, and
no observer-camera navigation. RMA-082 may replace the settings shell with the
complete settings screens while retaining these state and service contracts.

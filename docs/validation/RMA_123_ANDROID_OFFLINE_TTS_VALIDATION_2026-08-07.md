# RMA-123 Android offline TTS validation

**Status:** Implementation candidate awaiting exact-SHA validation  
**Date:** 2026-08-07

RMA-123 implements Android offline `TextToSpeech` as a separate TTS provider. The provider is `DeviceService + None` and accepts only exact installed voices that Android reports as not requiring a network connection.

## Planned permanent gate

The permanent `.github/workflows/rma123-android-offline-tts.yml` gate builds `ReachyMini.Core` in Release with warnings as errors and runs the deterministic `ReachyMini.AndroidOfflineTts.Tests` executable.

The contract suite covers:

- offline provider descriptor/locality;
- asynchronous engine readiness and setup-required states;
- exact-locale voice enumeration;
- network-voice filtering;
- installed/missing voice-data state;
- deterministic default and explicit-preference voice selection;
- no silent substitution when a preferred voice is unavailable;
- start/done/stop/error mapping;
- network errors as offline-contract violations;
- direct network/wrong-locale/uninstalled voice rejection before synthesis;
- input limits;
- busy/no-queue behavior;
- cancellation and timeout propagation;
- request/provider identity;
- stream failure and missing terminal callback;
- no retry;
- teardown/disposal;
- Java, Unity, and manifest source-level no-fallback constraints.

## Hosted Android validation

Repository CI is expected to compile every production Java source through `android-plugin` under Java 17 `-Xlint:all -Werror` and run Android lint with warnings treated as errors at minSdk 26.

## Physical boundary

The standard `kawa` Unity/API-26 regression will be required on the exact implementation SHA and final evidence SHA. That run is packaging/no-regression evidence only unless it explicitly invokes TTS.

RMA-123 completion will not claim successful audible offline synthesis until a physical speech acceptance actually invokes the provider with networking disabled and records the selected engine/voice and result.

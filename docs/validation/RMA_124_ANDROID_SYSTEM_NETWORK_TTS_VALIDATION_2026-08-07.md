# RMA-124 Android system/network TTS validation

**Status:** Implementation validated; final evidence-SHA revalidation pending  
**Date:** 2026-08-07

RMA-124 adds Android system/network `TextToSpeech` as an explicit `DeviceService + ProviderControlled` TTS provider. It preserves Android's per-voice network-required status, requires exact explicit selection before any network-required voice may synthesize, and never promotes an offline request to network-backed speech automatically.

## Accepted implementation SHA

`8c85cf4b401f100ee3f99d6c6781dfe28098b4e2`

The implementation consists of the initial RMA-124 source commit `1ea74e95379d545246b969e36fbdb9931019081f` plus the test-only analyzer repair `8c85cf4b401f100ee3f99d6c6781dfe28098b4e2`. The repair changes the private deterministic-test `Catalog()` helper from an interface return type to `TtsVoice[]` to satisfy CA1859. Production behavior and the no-fallback/network-approval policy did not change.

## Validated provider boundary

The accepted implementation enforces all of the following:

1. Provider identity is `android-system-tts`, with location `DeviceService` and network requirement `ProviderControlled`.
2. Provider diagnostics and display text state that the Android system voice may use networking.
3. Voice enumeration preserves `Voice.isNetworkConnectionRequired()` as either `SpeechNetworkRequirement.None` or `SpeechNetworkRequirement.Required`.
4. Automatic voice selection considers only installed, exact-locale, non-network voices.
5. A network-required voice is never selected automatically, including when no offline voice is available.
6. A network-required voice must exactly match the provider instance's `explicitlySelectedNetworkVoiceId` before synthesis is permitted.
7. The managed provider passes a separate `networkVoiceApproved` boolean to Android only after the exact selected voice satisfies that policy.
8. The Java bridge independently rejects a network-required voice unless that approval boolean is true and rejects an approval boolean attached to a non-network voice.
9. Android synthesis uses exact `TextToSpeech.setVoice()` selection, then verifies `getVoice()` still reports the exact requested voice ID, exact configured locale, and unchanged network-required status before audio output.
10. Non-network voices must have their voice data installed.
11. `setLanguage()` closest-match selection, `QUEUE_FLUSH` replacement, automatic model/data installation, automatic retry, alternate-voice fallback, RMA-123 substitution, cloud/OpenAI fallback, and independent HTTP transport are absent from the provider path.
12. Cancellation, operation timeout, busy/no-queue behavior, request/provider identity, missing terminal callbacks, network/service failures, bounded callback overflow, and deterministic `TextToSpeech.shutdown()` teardown are visible and tested.
13. RMA-123 remains a separate offline-only provider and continues to reject network-required voices.

## Permanent RMA-124 contract gate

Workflow run `31187587270`, job `92895964248`: **success** on the exact implementation SHA.

The permanent workflow ran:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.AndroidSystemTts.Tests/ReachyMini.AndroidSystemTts.Tests.csproj --configuration Release
```

Evidence:

- `ReachyMini.Core` built with warnings-as-errors and no production warning/error failure.
- All **27 deterministic RMA-124 contracts passed**.
- The suite requires no live Android TTS engine, speaker, network connection, API key, or stored speech payload.
- The contracts cover provider/network disclosure, per-voice network status, automatic offline-only selection, explicit network-voice selection, duplicate/wrong-locale/missing-data failures, lifecycle callbacks, network error mapping, callback identity, missing terminal callbacks, cancellation, timeout, busy/no-queue behavior, provider selection integrity, no retry, disposal, Java/Unity source policy, and continued RMA-123 offline strictness.

The earlier run on initial SHA `1ea74e95379d545246b969e36fbdb9931019081f` demonstrated that production `ReachyMini.Core` was already warnings-as-errors clean; its only failure was test-only CA1859 on the private `Catalog()` helper. No analyzer suppression or policy relaxation was added.

## Hosted repository CI

Workflow run `31187585156`: **success** on the exact implementation SHA.

Relevant Android job `92895954069`: **success**.

The Android job passed the production bridge under the repository's pinned Android/JDK toolchain, including:

- Java 17 compilation with `-Xlint:all -Werror`;
- Android lint with warnings-as-errors;
- debug and release Android assembly;
- Android unit tests;
- ARM64 JNI packaging and expected-symbol checks;
- native ABI conformance validation;
- manifest and asset-license checks;
- compiled JNI load verification;
- restricted dependency inspection.

The same hosted run also passed native tests, static analysis, sanitizer tests, managed tests, and pinned Reachy-model validation. No lint baseline, warning suppression, fallback path, or network-policy weakening was added.

## Unity/API-26 physical regression

Workflow run `31187585125`, job `92896052562`: **success** on the exact implementation SHA.

The self-hosted `kawa` run passed every substantive regression step:

- production MuJoCo ARM64 runtime staging and packaging verification;
- deterministic Unity presentation generation;
- Unity EditMode/PlayMode test execution;
- ARM64 API-26 APK build and verification with the RMA-124 bridge packaged;
- physical RMA-090 camera discovery acceptance;
- physical RMA-091 CameraX acquisition acceptance;
- physical RMA-092 CameraX texture acceptance;
- physical RMA-111 lightweight tracking acceptance;
- physical RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- evidence/log uploads;
- APK upload;
- final commit-status publication.

Selected artifacts from the run include:

- RMA-090 report artifact `5367574356` and logcat artifact `5367574390`;
- RMA-091 report artifact `5367599234` and logcat artifact `5367599261`;
- RMA-092 report artifact `5367619202` and logcat artifact `5367619232`;
- RMA-111 report artifact `5367639100` and logcat artifact `5367639147`;
- lifecycle report artifact `5367660751` and logcat artifact `5367660781`;
- authoritative-rendering report artifact `5367668446` and logcat artifact `5367668497`;
- consolidated physical-validation evidence artifact `5367669766`;
- APK artifact `reachy-mini-android-arm64-api26`, artifact ID `5367677631`, SHA-256 `7f9e2adb3a21193d5144e9d047e7d1893834255248914fd71292053654e60ee9`.

This run proves that RMA-124 packages into the supported ARM64/API-26 application and does not regress the existing Unity/native/camera/tracking/lifecycle/rendering path.

## Privacy and no-fallback boundary

RMA-124 owns no HTTP client, endpoint, API key, or speech-payload persistence. If the explicitly selected Android voice reports that networking is required, network behavior belongs to the selected Android TTS engine and must remain visible to the user as network-required/provider-controlled behavior.

If an offline voice is missing, RMA-124 does not silently pick a network-required voice. If a network voice fails, it is not retried automatically and no alternate TTS provider is selected. RMA-123 is not used as an implicit fallback either; provider choice remains explicit in both directions.

## Live audible-synthesis coverage boundary

The standard `kawa` physical regression does **not** invoke RMA-124, select a network-required Android voice, speak an utterance, or capture audible output. Therefore this validation does **not** claim that system/network TTS has been audibly demonstrated on the LG-H872.

Positive RMA-124 speech acceptance remains a Phase-13 speech-integration requirement. That acceptance should identify the Android TTS engine, exact voice ID and BCP-47 locale, record `isNetworkConnectionRequired()`, prove explicit selection when the voice is network-required, record start/done/stop/error callbacks, and capture physical audio/output evidence.

## Final evidence-SHA requirement

This document is the evidence-only update after implementation-SHA acceptance. RMA-124 is not finally signed off until the permanent RMA-124 gate, normal hosted CI, and complete `kawa` Unity/API-26 regression pass again on the exact documentation commit containing this evidence.

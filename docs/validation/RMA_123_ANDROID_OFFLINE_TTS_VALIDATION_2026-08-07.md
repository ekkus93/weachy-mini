# RMA-123 Android offline TTS validation

**Status:** Implementation validated  
**Date:** 2026-08-07

RMA-123 implements Android offline `TextToSpeech` as a separate TTS provider. The provider is `DeviceService + None` and accepts only exact installed voices that Android reports as not requiring a network connection.

## Validated implementation SHA

`19d19a7f42a10475a7ce7650b96999bc61a9f86b`

The accepted implementation consists of the original RMA-123 implementation plus two fail-clean validation repairs:

- `04874f151b388cbc84eae1cdb8664d1a682f4237` changed a private managed helper to its concrete `ReadOnlyCollection<TtsVoice>` return type to satisfy CA1859 without suppressing analyzers.
- `19d19a7f42a10475a7ce7650b96999bc61a9f86b` marks Android's required legacy `UtteranceProgressListener.onError(String)` override as deprecated while retaining the modern error-code callback, preserving API-26 compatibility and Java 17 warnings-as-errors.

Neither repair changes the RMA-123 offline/no-fallback policy.

## Permanent RMA-123 contract gate

Workflow run `31181367570`, job `92875183823`: **success** on the exact implementation SHA.

The permanent gate ran:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.AndroidOfflineTts.Tests/ReachyMini.AndroidOfflineTts.Tests.csproj --configuration Release
```

Evidence:

- `ReachyMini.Core` build succeeded with **0 warnings and 0 errors**.
- All **38 deterministic RMA-123 contracts passed**.
- The suite validates offline provider locality, asynchronous TTS-engine readiness, missing engine/data setup states, exact-locale voice filtering, installed voice state, deterministic user-preference selection, no preferred-voice substitution, start/done/stop/error mapping, input limits, busy/no-queue behavior, cancellation, timeout, request/provider identity, stream failure, missing terminal callback, no retry, disposal, and source-level Java/Unity/manifest no-fallback boundaries.
- Direct network-required, wrong-locale, uninstalled, and duplicate voice requests are rejected before synthesis.
- Android network/network-timeout failures are treated as offline-provider contract violations rather than permitted network behavior.
- Source contracts reject `setLanguage`, `QUEUE_FLUSH`, automatic setup-activity launch, cloud/OpenAI paths, HTTP transports, ASR substitution, and silent callback overflow/drop behavior.

The deterministic suite requires no Android TTS engine, microphone, speaker, network connection, API key, or stored speech payload.

## Hosted repository CI

Workflow run `31181367627`: **success** on the exact implementation SHA.

Relevant Android job `92875184242`: **success**.

The Android job used the pinned JDK 17 and Android toolchain and ran the production `android-plugin` `lint test` gate successfully. The Java TTS bridge therefore compiled through the repository's warnings-as-errors Java configuration, including the API-26 legacy error callback, and Android lint completed successfully.

The complete hosted run also passed:

- managed warnings-as-errors/native-lifecycle tests;
- native warnings-as-errors and sanitizer tests;
- static/actionlint/Ruff/ShellCheck repository checks;
- pinned Reachy-model validation;
- Android build/lint/test.

No lint baseline, warning suppression, provider fallback, or offline-policy relaxation was added to make the gate pass.

## Unity/API-26 physical regression

Workflow run `31181367510`, job `92875183724`: **success** on the exact implementation SHA.

The self-hosted `kawa` gate passed every substantive step:

- deterministic generated Reachy Unity presentation;
- production MuJoCo runtime staging;
- Unity tests;
- ARM64 API-26 device APK build and verification;
- physical RMA-090 camera discovery acceptance;
- physical RMA-091 camera acquisition acceptance;
- physical RMA-092 camera texture acceptance;
- physical RMA-111 lightweight tracking acceptance;
- physical RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- every evidence upload;
- APK upload;
- final commit-status publication.

Selected artifacts from that exact run:

- APK: `local-unity-device-apk-19d19a7f42a10475a7ce7650b96999bc61a9f86b`, artifact ID `8995275033`, SHA-256 `42018cc017ec02427e70290e868addad3c5e7cc8883e8c92775d880e12e2bf20`.
- Unity test results: artifact ID `8995035783`, SHA-256 `ca83406979e837c9bac6a262505372643f8998654094141c81fde1cad062ea3a`.
- RMA-090 report: artifact ID `8995090337`, SHA-256 `db85674adc00e559da5f603505a7ddd4e49c237286cfb3c3d5de349a7cb18f51`.
- RMA-091 report: artifact ID `8995127526`, SHA-256 `8918422f2594a3b1425ca58ef82ad23d93c571988d518709709981ba0b5031fd`.
- RMA-092 report: artifact ID `8995146506`, SHA-256 `c3f99b812b176940e8a1aa26c551fe4d33d675a5bebf02c7440dfe66f53d15fd`.
- RMA-111 report: artifact ID `8995173745`, SHA-256 `7d66ac5a1a3965846230d13f40591b14c6fa3f5722c41a456bb2d518ab23bc42`.
- Lifecycle report: artifact ID `8995205420`, SHA-256 `3276f64d7bd3dcfc3a210087dfc26dbe087a95a2af65668f8ef5fe3d85fcb4ff`.
- Authoritative rendering report: artifact ID `8995228411`, SHA-256 `a74d64754358515362a0b2f82c1ce0707dfb691a66dcc8a9f04c5891c2cb43b9`.

This exact physical run proves that the new TTS bridge packages into the supported ARM64/API-26 application and does not regress the existing Unity/native/camera/tracking/lifecycle/rendering path.

## Offline/no-fallback boundary validated

RMA-123's accepted implementation enforces the following boundaries in both managed and Java layers:

1. Only an exact requested Android `Voice` may be synthesized.
2. The voice must exactly match the configured locale.
3. `Voice.isNetworkConnectionRequired()` must be false.
4. Voice data must be installed; `KEY_FEATURE_NOT_INSTALLED` is not accepted.
5. After `setVoice`, `getVoice()` must still report that same exact installed non-network voice before audio output starts.
6. `setLanguage` is not used as an implicit best-match voice selector.
7. Network-only or missing preferred voices do not cause another voice to be selected silently.
8. Missing voice data surfaces setup guidance using `android.speech.tts.engine.INSTALL_TTS_DATA`; the app does not automatically launch installation/download UI.
9. A failed utterance is not retried automatically.
10. RMA-123 never invokes RMA-124 or a cloud TTS provider.

## Live audible-synthesis coverage boundary

The standard physical regression does **not** invoke RMA-123, speak an utterance through Android `TextToSpeech`, disable networking for a TTS acceptance, or record audible/captured synthesis output. Therefore this evidence does **not** claim that offline speech has been audibly demonstrated on the LG-H872.

Positive offline-TTS acceptance remains a later Phase-13 speech-integration requirement. That acceptance should:

- identify the selected Android TTS engine;
- record the exact voice ID and BCP-47 locale;
- prove that the selected voice is installed and `isNetworkConnectionRequired() == false`;
- disable networking during the utterance;
- record start/done/error callbacks;
- capture audible or audio-output evidence;
- verify cancellation/teardown on the physical device.

Within the RMA-123 implementation boundary, provider identity/locality, exact offline-voice enforcement, setup guidance, deterministic preference behavior, lifecycle/error handling, cancellation/timeout/disposal, no-fallback policy, Java/Unity packaging, hosted validation, and repository/device regressions are validated.

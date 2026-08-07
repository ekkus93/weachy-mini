# RMA-121 Android on-device ASR validation

**Status:** Implementation validated  
**Date:** 2026-08-07

RMA-121 implements the explicit Android on-device ASR provider. It does not implement the RMA-122 Android system recognizer and contains no system/cloud fallback.

## Validated implementation SHA

`3ecbb7841e42b1d39f4afcffd333d978134a516c`

## Permanent RMA-121 contract gate

Workflow run `31162560807`, job `92815995415`: **success** on the exact implementation SHA.

The permanent gate ran:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.AndroidOnDeviceAsr.Tests/ReachyMini.AndroidOnDeviceAsr.Tests.csproj --configuration Release
```

Evidence:

- `ReachyMini.Core` build succeeded with **0 warnings and 0 errors**.
- All **33 deterministic RMA-121 contracts passed**.
- Contracts cover explicit on-device locality, API-version capability behavior, microphone permission gating, configured language behavior, language-model setup states, partial/final/no-match results, concurrency, timeout, cancellation, service disconnect, language-model absence, callback request identity, provider-instance identity, explicit teardown, unexpected network errors as locality violations, and no automatic retry/fallback.
- Source contracts verify that production uses `createOnDeviceSpeechRecognizer`, does not call the generic system `createSpeechRecognizer` factory, does not use `EXTRA_PREFER_OFFLINE` as locality proof, and does not trigger automatic model download.

The deterministic suite uses no microphone, Android recognition service, network connection, API key, or stored transcript.

## Hosted repository CI

Workflow run `31162560508`: **success** on the same exact implementation SHA.

Relevant Android job `92815994724`: **success**.

The production Java bridge was compiled and linted with:

- Java 17 `-Xlint:all -Werror`;
- Android lint with warnings treated as errors;
- the repository's API-26 deployment floor;
- no lint baseline and no warning suppression.

The managed, native, static, Reachy-model, and Android jobs all passed.

## Unity/API-26 physical regression

Workflow run `31162560196`, job `92816046446`: **success** on the exact implementation SHA.

The self-hosted `kawa` gate passed:

- generated Unity presentation;
- Unity tests;
- ARM64 API-26 APK build and verification;
- RMA-090 camera discovery;
- RMA-091 camera acquisition;
- RMA-092 camera texture acceptance;
- RMA-111 lightweight tracking;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance.

APK artifact:

- `local-unity-device-apk-3ecbb7841e42b1d39f4afcffd333d978134a516c`
- artifact ID `8987952374`
- SHA-256 `e481f5df3f47a9b80fbe948856ddb3d60174242ae2e4e288bfc636a4deb98f94`

This physical run is **packaging and no-regression evidence only** for RMA-121. The attached LG-H872 runs Android 8.0/API 26, below Android's API-31 explicit on-device `SpeechRecognizer` boundary. It therefore cannot be used as positive evidence that live on-device microphone recognition succeeds.

## Hardware coverage boundary

Positive end-to-end on-device recognition still requires an Android API-31+ device with an installed explicit on-device recognition service and the configured language model. That hardware coverage requirement must not be silently substituted with the API-26 regression result.

Within the RMA-121 implementation boundary, the provider, Java bridge, Unity bridge, packaging, deterministic contracts, and repository regression gates are validated. Later speech work can build on this provider without introducing a hidden system/cloud fallback.

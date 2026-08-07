# RMA-122 Android system ASR validation

**Status:** Implementation validated  
**Date:** 2026-08-07

RMA-122 implements Android system `SpeechRecognizer` as a separate ASR option. The provider is explicitly `DeviceService` + `ProviderControlled` and may use networking according to the selected Android recognition service. It is not RMA-121 explicit on-device ASR and contains no automatic provider fallback.

## Validated implementation SHA

`7b1ef0777fa34f45ca1b17d161335619e879b56e`

## Permanent RMA-122 contract gate

Workflow run `31168802903`, job `92835670130`: **success** on the exact implementation SHA.

The permanent gate ran:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.AndroidSystemAsr.Tests/ReachyMini.AndroidSystemAsr.Tests.csproj --configuration Release
```

Evidence:

- `ReachyMini.Core` build succeeded with **0 warnings and 0 errors**.
- All **29 deterministic RMA-122 contracts passed**.
- Contracts cover explicit provider-controlled locality/network disclosure, distinct RMA-121/RMA-122 provider identity, API-26 capability behavior, microphone permission, missing service, configured language, probe failures, partial/final/no-match ordering, network and timeout classification, service disconnect, unsupported/unavailable language, busy/no-queue behavior, caller cancellation, operation timeout, maximum utterance duration, callback request identity, provider-instance identity, stream failure, missing terminal callback, no automatic retry, disposal, and source-level no-fallback boundaries.
- Source contracts verify that the production Java bridge uses `SpeechRecognizer.createSpeechRecognizer`, does not call `createOnDeviceSpeechRecognizer`, does not use `EXTRA_PREFER_OFFLINE`, does not trigger model download, and does not substitute the RMA-121 or a cloud provider.

The deterministic suite uses no microphone, Android recognition service, network connection, API key, or stored transcript.

## Hosted repository CI

Workflow run `31168802896`: **success** on the same exact implementation SHA.

Relevant Android job `92835670248`: **success**.

The production Java bridge was compiled and linted with:

- Java 17 `-Xlint:all -Werror`;
- Android lint with warnings treated as errors;
- the repository's API-26 deployment floor;
- no lint baseline and no warning suppression.

The managed, native, static, Reachy-model, and Android jobs all passed.

## Unity/API-26 physical regression

Workflow run `31168802944`, job `92835736922`: **success** on the exact implementation SHA.

The self-hosted `kawa` gate passed:

- generated Unity presentation;
- Unity tests;
- ARM64 API-26 APK build and verification;
- RMA-090 camera discovery;
- RMA-091 camera acquisition;
- RMA-092 camera texture acceptance;
- RMA-111 lightweight tracking;
- RMA-022 lifecycle acceptance;
- authoritative rendering acceptance;
- every evidence/APK upload and final status publication.

APK artifact:

- `local-unity-device-apk-7b1ef0777fa34f45ca1b17d161335619e879b56e`
- artifact ID `8990397664`
- SHA-256 `8969313064ce8386bddaee05b61d85f768b2c9a0e22dee1514112a184f2c724c`

This physical run proves packaging and no regression of the supported Unity/API-26 device path. RMA-122 can exist on API 26 when a system recognition service is installed; unlike RMA-121 it is not gated on API 31.

## Live-transcription coverage boundary

The existing physical workflow does not speak into the device microphone and does not invoke the RMA-122 provider. Therefore this evidence **does not claim successful live transcription** on the LG-H872.

Positive microphone acceptance should be added as part of the speech interaction/audio-focus work and must record the selected Android recognition service and network state. Even if a particular service works with networking disabled, RMA-122 remains `ProviderControlled` and potentially network-backed because Android's generic system-recognizer contract does not guarantee locality.

Within the RMA-122 implementation boundary, provider identity/disclosure, system-recognizer bridge, Unity callback bridge, packaging, deterministic lifecycle/error behavior, no-fallback policy, hosted Java validation, and repository regressions are validated.

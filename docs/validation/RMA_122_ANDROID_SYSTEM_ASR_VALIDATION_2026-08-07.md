# RMA-122 Android system ASR validation

**Status:** Candidate implementation awaiting exact-SHA validation  
**Date:** 2026-08-07

RMA-122 adds Android system `SpeechRecognizer` as a separate ASR option. The provider is explicitly `DeviceService` + `ProviderControlled` and may use networking according to the selected Android recognition service. It is not RMA-121 explicit on-device ASR and contains no automatic provider fallback.

Required permanent gates:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.AndroidSystemAsr.Tests/ReachyMini.AndroidSystemAsr.Tests.csproj --configuration Release
gradle --no-daemon -p android-plugin lint test
```

The deterministic RMA-122 suite uses a fake Android platform and requires no microphone, Android recognition service, network connection, API key, or stored transcript. Source contracts verify that the production Java bridge uses the generic Android system recognizer, omits the explicit on-device factory and offline-preference hint, destroys recognizers, and contains no provider substitution.

The repository's normal hosted CI and self-hosted Unity/Android regression must also pass on the exact implementation SHA before this document is converted to final evidence.

The physical regression is packaging/no-regression evidence. It does not speak into the device and must not be represented as proof of successful live transcription. Positive speech acceptance is deferred to a dedicated microphone/audio-flow test, with RMA-122 still disclosed as potentially network-backed.

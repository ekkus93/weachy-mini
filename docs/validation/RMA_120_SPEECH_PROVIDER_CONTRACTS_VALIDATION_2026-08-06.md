# RMA-120 speech provider contract validation

**Status:** Candidate implementation awaiting exact-SHA CI  
**Date:** 2026-08-06

RMA-120 introduces only the shared speech-provider contract boundary. It intentionally does not claim Android ASR/TTS functionality; physical speech-service validation begins with RMA-121 through RMA-125.

The permanent RMA-120 workflow must pass on the exact implementation SHA before this document is converted to final evidence.

Required gate:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.SpeechContracts.Tests/ReachyMini.SpeechContracts.Tests.csproj --configuration Release
```

The deterministic suite uses in-process fake providers and requires no microphone, Android service, network connection, API key, or audio device.

# RMA-120 speech provider contract tests

This deterministic executable validates the Unity-independent ASR/TTS contract boundary introduced by RMA-120.

It uses only in-process fake providers. It does not request microphone access, call Android speech services, open network connections, use API keys, play audio, or depend on a physical device. Physical Android ASR/TTS behavior begins in RMA-121 through RMA-125.

Run with:

```text
dotnet run --project managed/ReachyMini.SpeechContracts.Tests/ReachyMini.SpeechContracts.Tests.csproj --configuration Release
```

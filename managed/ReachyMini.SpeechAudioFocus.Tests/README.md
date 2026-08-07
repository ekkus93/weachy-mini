# RMA-125 deterministic speech audio-focus contracts

This executable validates the Unity-independent RMA-125 microphone/audio-focus state machine and source-level Android integration policy.

It requires no Android device, microphone, speaker, speech service, network access, API key, or stored audio. It verifies strict single-audio ownership, no ASR/TTS overlap, focus release ordering, visible interruption handling, fail-closed release faults, exact session identity, Android modern audio-focus usage, route/call/microphone monitoring, no phone-state permission, and the explicit RMA-121 + RMA-123 offline-default wiring.

Run from the repository root:

```text
dotnet run --project managed/ReachyMini.SpeechAudioFocus.Tests/ReachyMini.SpeechAudioFocus.Tests.csproj --configuration Release
```

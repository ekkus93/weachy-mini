# RMA-123 Android offline TTS deterministic contracts

This executable test suite validates the Unity-independent Android offline TTS provider contract without requiring an Android device, TTS engine, network connection, or cloud credential.

It exercises descriptor locality, asynchronous engine readiness modeling, exact-locale offline voice filtering, installation state and setup guidance, deterministic user-preference selection, event/error mapping, input limits, busy/no-queue behavior, cancellation, timeout, exact request/provider identity, disposal, no retry, and source-level Java/Unity/manifest no-fallback constraints.

Run from the repository root:

```bash
dotnet run --project managed/ReachyMini.AndroidOfflineTts.Tests/ReachyMini.AndroidOfflineTts.Tests.csproj --configuration Release
```

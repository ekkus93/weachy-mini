# RMA-124 deterministic Android system/network TTS contracts

This console test target validates the RMA-124 managed provider boundary without an Android device, TTS engine, network connection, API key, or stored speech payload.

It covers provider/network disclosure, per-voice network status, explicit network-voice selection, prohibition on automatic network fallback, exact voice/locale validation, lifecycle callbacks, cancellation, timeout, busy/no-queue behavior, provider/request identity, visible failures, no retry, disposal, and source-level Java/Unity no-fallback constraints.

Run:

```bash
dotnet run --project managed/ReachyMini.AndroidSystemTts.Tests/ReachyMini.AndroidSystemTts.Tests.csproj --configuration Release
```

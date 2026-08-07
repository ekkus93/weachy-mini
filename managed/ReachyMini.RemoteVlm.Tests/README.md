# RMA-115 remote VLM contracts

Run:

```bash
dotnet run --project managed/ReachyMini.RemoteVlm.Tests/ReachyMini.RemoteVlm.Tests.csproj --configuration Release
```

The deterministic suite opens no network connection and needs no API key. Its 60 cases cover Responses and Chat Completions selection, transformed-frame and validity-mask enforcement, bounded image policy, coverage limitations, stale-entity exclusion, cancellation, concurrency, structured error validation, secret redaction, disposal, and single-attempt/no-fallback behavior.

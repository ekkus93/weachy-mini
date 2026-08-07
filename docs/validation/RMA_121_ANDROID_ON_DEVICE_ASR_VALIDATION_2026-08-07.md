# RMA-121 Android on-device ASR validation

**Status:** Candidate implementation awaiting exact-SHA validation  
**Date:** 2026-08-07

RMA-121 adds the explicit Android on-device ASR provider. It does not implement
the RMA-122 Android system recognizer and contains no system/cloud fallback.

Required permanent gates:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.AndroidOnDeviceAsr.Tests/ReachyMini.AndroidOnDeviceAsr.Tests.csproj --configuration Release
gradle --no-daemon -p android-plugin lint test
```

The managed suite is deterministic and uses no microphone, Android speech
service, network connection, API key, or stored transcript. The Android gate
compiles/lints the production Java bridge.

The repository's normal hosted CI and self-hosted Unity/Android regression must
also pass on the exact implementation SHA before this document is converted to
final evidence.

The attached physical regression device currently used by the repository is
API 26, below Android's API-31 explicit on-device SpeechRecognizer boundary.
Therefore that device cannot be used as positive evidence that real on-device
recognition succeeds. Final RMA-121 evidence must distinguish implementation and
negative/no-regression proof from later API-31+ end-to-end speech acceptance.

# RMA-122 Android system ASR contract tests

This deterministic executable validates the explicit Android **system** speech-recognition provider introduced by RMA-122.

The suite requires no microphone, Android recognition service, network connection, API key, or stored transcript. A fake platform exercises lifecycle and error behavior, while source contracts inspect the production Unity/Java bridge.

The key boundary is intentional: this provider is `DeviceService` + `ProviderControlled`. It may use networking according to the Android recognition service selected by the device. It is never presented as offline or equivalent to the RMA-121 explicit on-device provider.

Run with:

```text
dotnet run --project managed/ReachyMini.AndroidSystemAsr.Tests/ReachyMini.AndroidSystemAsr.Tests.csproj --configuration Release
```

The repository Android gate separately compiles and lints the production Java bridge with warnings treated as errors.

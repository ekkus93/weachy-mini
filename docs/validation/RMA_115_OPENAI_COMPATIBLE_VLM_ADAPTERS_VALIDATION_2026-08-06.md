# RMA-115 OpenAI-compatible VLM adapter validation

**Status:** Implementation boundary validated
**Date:** 2026-08-06
**Implementation SHA:** `54a505cd123a3b4d5d7a85346f129f7ef58261d1`

## Managed RMA-115 gate

Permanent workflow run `31140614864`, job `92749544939`, passed on the exact implementation SHA.

The gate ran:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.RemoteVlm.Tests/ReachyMini.RemoteVlm.Tests.csproj --configuration Release
```

The managed-core build completed with `0 Warning(s)` and `0 Error(s)`. The deterministic contract executable reported `RMA-115 OpenAI-compatible VLM adapter contracts passed: 60.` The suite uses fake encoders and a mock transport, opens no network connection, and requires no API credential.

## Repository CI

CI run `31140614829` passed on the exact implementation SHA. Its `android`, `native`, `static`, `reachy-model`, and `managed` jobs all completed successfully, including Android lint/tests, native warnings-as-errors and sanitizer tests, actionlint/static repository checks, pinned Reachy/MuJoCo model validation, and managed warnings-as-errors/native lifecycle tests.

## Unity, Android, and physical-device validation

Local Unity Android Validation run `31140614878`, job `92749545021`, passed on the exact implementation SHA. The self-hosted `kawa` gate successfully completed:

- Unity tests;
- ARM64 API-26 APK build and verification;
- physical-device RMA-090 camera discovery acceptance;
- physical-device RMA-091 camera acquisition acceptance;
- physical-device RMA-092 camera texture acceptance;
- physical-device RMA-111 lightweight tracking acceptance;
- physical-device RMA-022 lifecycle acceptance;
- authoritative rendering acceptance.

Selected retained artifacts from that exact run include:

- Unity test results: artifact `8979708589`, digest `sha256:3a4871b390069e537138bd3dbd0a6b3b822850efad4dd8be6ed4ae8ea4ba50b3`;
- RMA-111 lightweight tracking report: artifact `8979805156`, digest `sha256:a271b3dfce1113e86c9ff5673105347e23bb9b3f0ecc21de358ca270b66f3fa3`;
- physical-device APK: artifact `8979858729`, digest `sha256:2a9cd8cef86a42d80b5010970c2a4bba56bded6f1a85813808d0120631b9737f`.

## Acceptance conclusion

RMA-115 preserves the fail-closed perception boundary: remote VLM input is limited to eligible transformed Reachy-eye content with explicit validity handling; endpoint style and model identity are explicit; automatic retry, provider fallback, response storage, and streaming remain disabled; stale world-model history is not presented as current visual evidence; cancellation and concurrency failures remain visible; and bundled RMA-111 tracking continues to operate independently of a VLM.

The implementation commit contains no RMA-115 bootstrap/recovery scaffolding. This documentation update is intentionally separate from the implementation commit so the final repository evidence boundary can be revalidated on its own exact SHA.

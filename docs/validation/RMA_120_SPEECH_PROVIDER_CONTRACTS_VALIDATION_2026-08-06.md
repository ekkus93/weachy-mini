# RMA-120 speech provider contract validation

**Status:** Accepted exact-SHA evidence  
**Date:** 2026-08-06

## Scope boundary

RMA-120 introduces only the shared Unity-independent ASR/TTS provider contract boundary. It intentionally does not claim Android microphone capture, Android `SpeechRecognizer`, Android `TextToSpeech`, audio focus, or end-to-end speech functionality. Those begin in RMA-121 through RMA-125.

## Accepted implementation

Exact accepted implementation SHA:

```text
c26f1bf4a373b7b3060eeee325d6831ee9c25eb4
```

The implementation defines separate `IAsrProvider` and `ITtsProvider` contracts with explicit provider identity, locality/network requirements, ASR language capability, TTS voice/network capability, bounded operation identity and timeout, cancellation, asynchronous disposal, structured availability/failures, and fail-closed provider/event-origin checks.

`SpeechProviderPolicy` defaults automatic provider fallback, cross-privacy-boundary fallback, and automatic retry to disabled. The RMA-120 API exposes no fallback provider or fallback registry.

## Permanent RMA-120 gate

Workflow run: `31154981121`  
Job: `92792408530`  
Conclusion: `success`

The workflow checked out exact SHA `c26f1bf4a373b7b3060eeee325d6831ee9c25eb4` and ran:

```text
dotnet build managed/ReachyMini.Core/ReachyMini.Core.csproj --configuration Release --warnaserror
dotnet run --project managed/ReachyMini.SpeechContracts.Tests/ReachyMini.SpeechContracts.Tests.csproj --configuration Release
```

The production managed core build completed with:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

The deterministic speech contract executable passed all 36 cases and ended with:

```text
RMA-120 speech provider contracts passed: 36.
```

The suite covers interface separation, provider/network truthfulness, provider identity, availability, structured errors and bounds, selection kind/epoch/staleness, timeout bounds, redirect rejection, event-origin rejection, no-fallback/no-cross-privacy/no-auto-retry defaults, ASR language and event invariants, TTS voice and event invariants, cancellation signatures and propagation, and explicit disposal.

## Hosted repository CI

Workflow run: `31154981202`  
Conclusion: `success`  
Exact SHA: `c26f1bf4a373b7b3060eeee325d6831ee9c25eb4`

The repository-wide hosted CI completed successfully on the accepted implementation SHA.

## Self-hosted Unity/Android regression

Workflow run: `31154981088`  
Job: `92792471791`  
Runner: `kawa`  
Conclusion: `success`  
Exact SHA: `c26f1bf4a373b7b3060eeee325d6831ee9c25eb4`

The exact-SHA self-hosted run passed:

- generated Reachy Unity presentation and production MuJoCo staging;
- Unity tests;
- ARM64 API-26 APK build and verification;
- physical RMA-090 camera discovery;
- physical RMA-091 CameraX acquisition;
- physical RMA-092 GPU texture acceptance;
- physical RMA-111 lightweight tracking;
- RMA-022 lifecycle acceptance;
- authoritative-rendering acceptance;
- all evidence uploads, APK upload, and final commit-status publication.

Representative artifact evidence includes:

- RMA-090 artifact `8984903433`, digest `sha256:a7cff707cbc164adab4e323090828bfb88e42fd476d960cf7684123fc96e76f3`;
- RMA-091 artifact `8984938708`, digest `sha256:76072117380621e79b04a7119de03804cf789201e2678595b6bc6dc6d5ec5e90`;
- RMA-092 artifact `8984969205`, digest `sha256:408b8e0bf45f32f9e8eb73615c16970d6832629bc1ac975106e7f28c91cc65f5`;
- RMA-111 artifact `8984986658`, digest `sha256:4a23a01623b5b24f5be0694cb82865eb09e8063c96d0fe4971767c015f7fdd54`;
- lifecycle artifact `8985012226`, digest `sha256:5cd4d8d8c7e8c3f123ea802878e081e78f8f847ac78fd823fc66f70753c2e899`;
- authoritative-rendering artifact `8985025103`, digest `sha256:681d009bc71adb7c857c5ed4203581a10604526d5247c6b863e1edb72fe3716c`;
- final APK artifact `8985038698`, digest `sha256:b2d141bccee0cb3f60cae0ff2136ab45b629d11ad25061ceada6b342688bc875`.

These physical-device checks demonstrate that the new shared speech contracts did not regress the already accepted Unity/Android, camera, tracking, lifecycle, or rendering paths. They are **not** evidence of working Android ASR/TTS; RMA-121 through RMA-125 remain responsible for that functionality and its physical-device evidence.

## Conclusion

The RMA-120 contract implementation satisfies its defined scope on exact SHA `c26f1bf4a373b7b3060eeee325d6831ee9c25eb4`: ASR and TTS are independent; locality and network behavior are explicit; capabilities, lifecycle, cancellation, availability, and structured errors are represented; and the contract has no automatic or unauthorized cross-provider fallback mechanism.

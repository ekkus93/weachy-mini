# Android system ASR architecture

**RMA:** 122  
**Status:** Implementation candidate  
**Date:** 2026-08-07

## Scope

RMA-122 adds Android's normal system `SpeechRecognizer` as a separately selectable ASR provider. It does not replace RMA-121 explicit on-device ASR, and it does not implement cloud ASR.

The distinction is part of the provider contract rather than UI wording alone:

| Provider | Location | Network contract | Android factory |
| --- | --- | --- | --- |
| RMA-121 explicit on-device ASR | `OnDevice` | `None` | `createOnDeviceSpeechRecognizer` |
| RMA-122 Android system ASR | `DeviceService` | `ProviderControlled` | `createSpeechRecognizer` |

The RMA-122 display name includes `may use network`. `SpeechProviderDescriptor.MayUseNetwork` and `RequiresNetworkDisclosure` are therefore true. The app must never describe this provider as offline merely because recognition happens through an Android system API.

## Service and permission discovery

The production Java bridge probes `SpeechRecognizer.isRecognitionAvailable(Activity)` and microphone permission without creating a recognizer. Unlike RMA-121, RMA-122 is not gated on API 31: the repository's API-26 deployment floor can use the system-recognizer API when a recognition service is installed.

Recognition is not started until `RECORD_AUDIO` permission is present. Missing permission is reported as `PermissionRequired`; RMA-122 does not request permission itself because audio permission/focus orchestration belongs to later speech lifecycle work.

## Provider-controlled networking

`SpeechRecognizer.createSpeechRecognizer(Activity)` delegates recognition to the device-selected recognition service. That service can be local, network-backed, or change behavior according to its own implementation and configuration. RMA-122 therefore makes no locality guarantee.

The recognition intent deliberately does **not** set `RecognizerIntent.EXTRA_PREFER_OFFLINE`. That flag is only a preference and cannot turn the system provider into RMA-121's explicit on-device provider.

Network and network-timeout errors are preserved as network/timeout failures. They are not treated as RMA-121 locality violations, because networking is permitted by this provider's declared contract. They also do not trigger an alternate provider.

## Language behavior

An RMA-122 provider instance is configured for one explicit BCP-47 language tag. Requests for another tag fail before platform access. The selected tag is passed to `RecognizerIntent.EXTRA_LANGUAGE`.

RMA-122 does not claim that the Android system service has an installed offline model for that language. Runtime `ERROR_LANGUAGE_NOT_SUPPORTED` and `ERROR_LANGUAGE_UNAVAILABLE` remain visible structured failures.

## Utterance lifecycle

The managed provider permits one operation at a time. Concurrent work returns `Busy`; it is not queued. Every utterance is bound to the exact RMA-120 provider instance/request identity and to the smaller of the selected operation timeout and configured maximum utterance duration.

The Unity bridge and Java bridge preserve these lifecycle rules:

- Android `SpeechRecognizer` calls are marshalled onto the Android main looper;
- callbacks cross into C# through an `AndroidJavaProxy` and a bounded queue;
- queue overflow becomes a terminal visible failure rather than dropping transcript events;
- callback request-ID mismatch becomes a terminal contract failure;
- caller cancellation and provider timeout cancel the exact Java request;
- terminal recognition destroys the recognizer;
- provider disposal calls `close()`, cancelling/destroying any active recognizer;
- no automatic retry is performed.

Recognized events map to RMA-120 events as follows:

- ready => started;
- partial result => partial;
- final result => final;
- empty final / `ERROR_NO_MATCH` => no-match;
- `ERROR_NETWORK` => network failure;
- `ERROR_NETWORK_TIMEOUT` or speech timeout => timeout;
- busy / too many requests => busy;
- service disconnect/server/audio/client errors => structured service failures;
- unsupported/unavailable language => unsupported-language failure.

## No-fallback boundary

RMA-122 never calls the RMA-121 provider factory and never selects another ASR provider. The Java bridge uses only `createSpeechRecognizer`; it does not call `createOnDeviceSpeechRecognizer`, use `EXTRA_PREFER_OFFLINE`, trigger a model download, or call a cloud API.

Provider selection remains owned by RMA-120's explicit `SpeechProviderSelection` state. A request whose provider-instance identity does not match the RMA-122 instance fails before Android platform access.

## Packaging and hosted validation

The system bridge is packaged in the existing `ReachyOnDeviceAsr.androidlib` because that library already owns the speech-recognition manifest permission and `RecognitionService` package-visibility declaration. Packaging together does not merge provider identity: RMA-121 and RMA-122 have different Java classes, C# platform adapters, provider IDs, locality, and network contracts.

The first-party `android-plugin` project compiles every Java source in this androidlib under Java 17 `-Xlint:all -Werror` and Android lint with warnings treated as errors at the production API-26 floor.

The deterministic managed RMA-122 suite validates provider disclosure, API-26 capability behavior, permission/service availability, configured language, transcript event ordering, network/error classification, busy behavior, timeout/cancellation, provider identity, disposal, no retry, and source-level no-fallback contracts.

## Physical-validation boundary

The existing LG-H872/API-26 device is capable of packaging and running this provider because RMA-122 does not require API 31. However, the ordinary camera/lifecycle regression does not inject speech into the microphone and therefore is not positive transcription evidence.

A later speech acceptance should explicitly exercise microphone input and record which Android recognition service is selected, whether networking is enabled, and the actual result. RMA-122 must remain labeled provider-controlled regardless of whether a particular run happens to work with networking disabled.

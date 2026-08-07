# Android explicit on-device ASR architecture

**RMA:** 121  
**Status:** Implementation candidate  
**Date:** 2026-08-07

## Scope

RMA-121 implements only the explicit Android on-device automatic-speech-
recognition provider. Android system recognition remains a separate RMA-122
provider. Cloud transcription remains separate. A failure in this provider
cannot select either one.

The production provider reuses the RMA-120 `IAsrProvider` contract and publishes
`SpeechProviderLocation.OnDevice` plus `SpeechNetworkRequirement.None`.

## Locality boundary

Android API 31 introduced both
`SpeechRecognizer.isOnDeviceRecognitionAvailable(Context)` and
`SpeechRecognizer.createOnDeviceSpeechRecognizer(Context)`. RMA-121 requires
both. The production Java bridge never calls either `createSpeechRecognizer`
system-provider factory.

`RecognizerIntent.EXTRA_PREFER_OFFLINE` is not a locality guarantee and is
never added to the recognition intent. The provider treats a network error from
an explicitly on-device recognizer as a contract violation rather than as a
signal to retry through a network-backed recognizer.

Automatic retry, provider fallback, and model download are absent.

## Permission and recognizer creation

Capability probing can determine API level, microphone permission, and explicit
on-device recognizer availability without creating a recognizer. Both language
support checking and recognition re-check `RECORD_AUDIO` permission before
calling `createOnDeviceSpeechRecognizer`.

The provider returns `PermissionRequired` while permission is absent. RMA-121
does not auto-request microphone permission; permission/user-audio orchestration
is owned by later speech lifecycle work (especially RMA-125).

## Language support

The provider is configured for one explicit BCP-47 language tag.

- API 31-32 can use the explicit on-device recognizer, but Android does not
  expose `checkRecognitionSupport` yet. Availability therefore says that
  per-language support cannot be preflighted and runtime language errors remain
  authoritative.
- API 33+ performs `checkRecognitionSupport` on a temporary explicit on-device
  recognizer.
- Installed on-device language => available.
- Pending language model => setup required.
- Supported but not installed => setup required.
- Online-only language => rejected as unsupported by this provider.
- Unsupported language => unavailable.
- `ERROR_CANNOT_CHECK_SUPPORT` leaves the explicit provider usable but visibly
  marks preflight as unavailable.
- RMA-121 never calls `triggerModelDownload`; installation remains an explicit
  setup action.

Every temporary support recognizer is destroyed after its terminal callback or
cancellation.

## Utterance lifecycle

The managed provider allows one operation at a time. Concurrency overflow
returns `Busy`; there is no queue and no automatic retry.

Each operation is bound to the RMA-120 provider instance/request identity and to
the smaller of the request timeout and configured maximum utterance duration.
The Java bridge marshals all `SpeechRecognizer` operations onto Android's main
looper. Java callbacks cross into C# only through an `AndroidJavaProxy` that
publishes into task/queue primitives with asynchronous continuations; callbacks
do not directly mutate Unity application state.

The callback queue is bounded. Overflow becomes a terminal structured failure
instead of silently dropping transcript events.

Recognized events map as follows:

- ready/beginning of speech => started;
- partial result => partial;
- result => final;
- empty final or `ERROR_NO_MATCH` => no-match;
- `ERROR_SPEECH_TIMEOUT` => timeout;
- recognizer busy / too many requests => busy;
- server disconnected => service failure;
- language not supported => unsupported language;
- language unavailable => language-model unavailable;
- microphone/audio/client/service failures => structured errors;
- network/network-timeout from the explicit on-device recognizer => contract
  violation, with no fallback.

Caller cancellation, provider disposal, and operation timeout all cancel the
exact Java request. Terminal recognition paths destroy the recognizer.
`close()` cancels/destroys any recognizer still owned by the bridge.

## Android packaging

`ReachyOnDeviceAsr.androidlib` declares `RECORD_AUDIO`, marks the microphone
feature optional, and declares package visibility for
`android.speech.RecognitionService`.

The same production Java source is also compiled by the first-party
`android-plugin` hosted lint/test project under Java `-Xlint:all -Werror`, so
the checked-in bridge is not merely source-inspected.

## Validation boundary

The deterministic managed suite uses a fake Android platform to exercise
availability, permission gating, API 31/33 language behavior, partial/final and
no-match events, busy handling, timeout/cancellation, service death,
language-model absence, request identity, disposal, unexpected network errors,
and the no-retry boundary. Source contracts enforce the prohibited system
factory, `EXTRA_PREFER_OFFLINE`, and automatic model-download paths.

The current physical regression device is an LG-H872 running Android 8.0/API
26. It cannot positively exercise the API-31 explicit on-device recognizer. The
physical gate can prove that adding RMA-121 does not regress the supported
Unity/APK/camera/lifecycle/rendering path, but positive microphone recognition
must not be claimed from that device. An API-31+ device with an installed
on-device recognition service is required for later end-to-end offline speech
acceptance.

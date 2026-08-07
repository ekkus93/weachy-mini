# RMA-120 speech provider contracts

**Status:** Implementation candidate  
**Date:** 2026-08-06

## Scope

RMA-120 defines the Unity-independent contract boundary used by all later ASR and TTS implementations. It does **not** implement Android `SpeechRecognizer`, Android `TextToSpeech`, microphone capture, audio focus, OpenAI speech APIs, or provider fallback. Those are RMA-121 through RMA-125 and RMA-144/RMA-145.

The contract is intentionally fail-closed. A selected provider operation is bound to one provider instance and one monotonic selection epoch. The speech layer has no provider registry, alternate-provider callback, automatic retry path, or cross-provider fallback hook.

## Separate ASR and TTS interfaces

`IAsrProvider` and `ITtsProvider` are independent interfaces.

`IAsrProvider` exposes immutable provider identity and locality/network disclosure, ASR capabilities, an explicit availability check, a cancellable asynchronous recognition event stream, and asynchronous disposal.

`ITtsProvider` exposes immutable provider identity and locality/network disclosure, TTS capabilities, explicit availability, cancellable voice enumeration, per-voice network requirements, a cancellable asynchronous speech event stream, and asynchronous disposal.

The interfaces are not subtypes of each other and cannot be selected through a common ambiguous speech operation.

## Provider locality and network truthfulness

`SpeechProviderDescriptor` separates provider location from network requirement:

- `OnDevice` must declare `None`;
- `LocalNetwork` and `Cloud` must declare `Required`;
- `DeviceService` may declare `None`, `ProviderControlled`, or `Required`.

`DeviceService + ProviderControlled` exists specifically for Android/system speech providers where the operating-system-selected service may decide whether processing occurs locally or remotely. It must never be presented as proof of offline processing.

TTS voices independently carry `SpeechNetworkRequirement`, allowing one engine to expose both installed offline voices and voices that may or must use networking without conflating them.

## Selection, request identity, and no-fallback boundary

`SpeechProviderSelection` holds exactly one provider kind and one selected provider instance. Every explicit selection change advances a monotonic epoch. `SpeechOperationContext` captures request ID, provider kind, exact provider instance ID, selection epoch, and a bounded timeout.

`SpeechProviderContract.ValidateProviderForOperation` rejects execution through any descriptor that does not exactly match the operation context. `ValidateEventOrigin` rejects events whose provider instance or request ID differs from the selected operation.

This prevents framework-level provider substitution. A later concrete provider implementation is also required to obey its declared locality and must not internally redirect work to another provider. RMA-173 will add end-to-end silent-fallback regression tests once concrete providers exist.

`SpeechProviderPolicy` defaults automatic provider fallback, cross-privacy-boundary fallback, and automatic retry to `false`. No RMA-120 API accepts a fallback provider or fallback registry.

## Availability and structured failures

Availability is explicit and distinguishes available, unavailable, permission required, setup required, busy, and faulted states.

`SpeechProviderError` carries a bounded category, provider-safe code, bounded diagnostic text, and retry classification. Later concrete adapters remain responsible for redacting secrets/private media before constructing diagnostics.

ASR and TTS event types enforce shape invariants: only ASR partial/final events contain transcript text; only failed events contain `SpeechProviderError`; event sequences begin above zero and carry provider/request identity; cancellation is never converted into fabricated success.

## Lifecycle and cancellation

Every potentially blocking provider operation accepts a `CancellationToken`. Both provider interfaces derive from `IAsyncDisposable`. The contract does not allow fire-and-forget recognition or synthesis operations.

RMA-121 through RMA-125 remain responsible for Android lifecycle ownership, microphone permission, recognizer/engine destruction, audio focus, route changes, service death, and callback marshaling.

## Validation

The permanent RMA-120 gate builds the shared managed core with warnings treated as errors and runs an in-process deterministic contract executable. The suite uses fake providers only; it does not request microphone permission, contact Android speech services, play audio, open a network connection, or use credentials.

The suite validates interface separation, provider/network truthfulness, language and voice metadata, availability, structured failures, selection epochs, timeout bounds, provider/event origin checks, no-fallback defaults, cancellation signatures and propagation, and explicit disposal.

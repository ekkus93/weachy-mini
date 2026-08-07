# Android offline TTS architecture

**RMA:** 123  
**Status:** Implementation candidate  
**Date:** 2026-08-07

## Scope

RMA-123 adds Android `TextToSpeech` as an explicitly offline TTS provider. The provider is separate from RMA-124 system/network TTS and from later cloud TTS providers.

The provider descriptor is `TextToSpeech + DeviceService + None`: Android owns the TTS engine process/service, while RMA-123 permits synthesis only through an exact installed `Voice` whose Android metadata says `isNetworkConnectionRequired() == false`.

This is a hard execution boundary. A network-required voice is not an acceptable degraded mode, and an unavailable preferred voice does not cause another voice or provider to be selected silently.

## Asynchronous engine initialization

The Java bridge owns one Android `TextToSpeech` instance. Engine creation uses `TextToSpeech(Context, OnInitListener)` and completes asynchronously on the Android main looper.

Probe, voice enumeration, and synthesis requests that arrive during initialization are held only as same-provider initialization continuations. They are identified by request ID and can be removed by cancellation before initialization completes. A cancelled synthesis request therefore cannot begin speaking later when initialization finally succeeds.

Initialization failure is surfaced as setup-required/unavailable state. RMA-123 does not instantiate another TTS provider.

## Voice discovery and offline proof

The Java bridge enumerates `TextToSpeech.getVoices()` and records, for the configured exact BCP-47 locale:

- voice name/ID;
- locale;
- `Voice.isNetworkConnectionRequired()`;
- whether `Voice.getFeatures()` contains `TextToSpeech.Engine.KEY_FEATURE_NOT_INSTALLED`.

The managed provider exposes only exact-locale voices whose network requirement is `None`. It preserves `IsInstalled=false` so setup UI can explain that offline data exists conceptually but is not installed.

A voice is eligible for synthesis only when all of the following remain true immediately before speaking:

1. its ID is unique and exactly matches the requested voice ID;
2. its locale exactly matches the provider's configured BCP-47 locale, case-insensitively;
3. Android reports that it does not require a network connection;
4. Android does not mark its data as not installed.

The Java bridge repeats those checks, calls `setVoice` only after they pass, then reads `getVoice()` and verifies that Android retained that same exact installed offline voice. If not, synthesis is rejected before audio output.

RMA-123 deliberately does **not** call `setLanguage`. Locale-level selection can choose a best-match voice and therefore does not provide the exact voice identity required by this provider contract.

## Locale and user-preference selection

`AndroidOfflineTtsProvider.SelectVoice` provides deterministic selection over the already-filtered voice catalog:

- with an explicit user preference, only that exact installed offline voice is accepted;
- if the preferred voice is absent, network-backed, uninstalled, or wrong-locale, selection returns unavailable rather than substituting another voice;
- without an explicit preference, the provider chooses deterministically by display name and then voice ID among installed exact-locale offline voices.

The helper does not mutate application settings. Persisting the user's chosen language/voice remains settings/orchestration work.

## Missing data and installation guidance

Android language status and voice metadata can report missing synthesis data. RMA-123 maps that condition to `SetupRequired` / `MissingVoiceData` and surfaces the standard Android installation action name:

`android.speech.tts.engine.INSTALL_TTS_DATA`

The provider does not launch that activity automatically and does not trigger a download. Setup remains a user-visible action. If only network-required voices exist for the locale, diagnostics explicitly say those voices are prohibited and that offline voice data must be installed instead.

## Utterance lifecycle

The provider permits one operation at a time. Concurrent work returns `Busy`; it is not queued.

Every speech request is bound to the exact RMA-120 provider instance and request ID. The selected operation timeout, caller cancellation, and provider lifetime are linked into one cancellation path.

The Java bridge uses `UtteranceProgressListener` and maps:

- `onStart` => started;
- `onDone` => completed;
- `onStop` => cancelled;
- `onError` => structured failure.

The Unity bridge marshals callbacks through a bounded queue. Request-ID mismatch and queue overflow become visible terminal failures. A platform stream that ends without a terminal callback is also a failure; no completion is fabricated.

Cancellation removes pre-initialization work or stops the exact active utterance. Provider disposal cancels the managed lifetime, calls Java `close()`, stops active speech, and calls `TextToSpeech.shutdown()`.

## Android error mapping

Missing/not-yet-installed voice data maps to `MissingVoiceData`. Busy maps to `Busy`. Output/service errors remain service failures. Invalid requests or rejected voice identity are contract failures.

If Android reports `ERROR_NETWORK` or `ERROR_NETWORK_TIMEOUT` after an installed non-network voice was explicitly selected, RMA-123 treats that as a **contract violation**, not as an ordinary permitted network failure. The provider does not retry with a different voice or provider.

## No-fallback boundary

RMA-123 contains no network transport, cloud endpoint, provider registry, or alternate-provider callback. It does not:

- select network-required Android voices;
- call `setLanguage` to allow closest-match substitution;
- use `QUEUE_FLUSH` to replace another utterance;
- launch voice-data installation automatically;
- invoke RMA-124 or a cloud TTS provider;
- retry a failed utterance automatically.

The existing RMA-120 `SpeechProviderSelection` remains the authority for changing TTS provider instances.

## Packaging and hosted validation

The TTS Java bridge is packaged in the existing speech Android library that already owns the Android speech bridges. Both the Unity library manifest and the hosted `android-plugin` manifest declare package visibility for `android.intent.action.TTS_SERVICE`.

The first-party `android-plugin` build compiles the bridge with Java 17 `-Xlint:all -Werror`, Android lint warnings-as-errors, and the repository's API-26 deployment floor.

A dedicated deterministic managed suite validates offline locality, voice filtering/installation state, preference semantics, lifecycle/error behavior, exact request/provider identity, cancellation/timeout/disposal, and source-level no-fallback constraints without requiring an Android TTS engine or network connection.

## Physical-validation boundary

The ordinary `kawa` Unity/API-26 regression proves that the bridge packages and does not regress supported device behavior. It does not, by itself, prove audible offline synthesis.

Positive offline-TTS acceptance requires a device with installed TTS voice data and an explicit run that invokes RMA-123 with networking disabled. The later Phase-13 speech acceptance should record selected engine, exact voice ID/locale, network-disabled state, start/done callbacks, and audible or captured output evidence.

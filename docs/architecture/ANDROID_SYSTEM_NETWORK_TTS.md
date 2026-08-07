# Android system/network TTS — RMA-124

RMA-124 adds Android system `TextToSpeech` as a separate, explicitly network-aware TTS provider. It does not relax or replace the RMA-123 offline provider.

## Provider boundary

The provider identity is `android-system-tts` and is classified as:

- kind: text-to-speech;
- location: `DeviceService`;
- network requirement: `ProviderControlled`;
- display disclosure: the provider may use networking.

Android voices are enumerated for the configured exact BCP-47 locale. Every returned `TtsVoice` preserves whether Android reports the voice as network-required through `Voice.isNetworkConnectionRequired()`.

## Explicit network-voice selection

A network-required voice is not eligible merely because it exists, is preferred, or is the only voice available. The provider instance must be constructed with that exact voice ID as its explicitly selected network voice.

The selection rules are intentionally asymmetric:

1. automatic selection considers installed, exact-locale, non-network voices only;
2. automatic selection never returns a network-required voice, even if one has previously been explicitly approved;
3. a requested network voice must exactly equal the provider instance's explicit network selection;
4. the managed provider passes an explicit `networkVoiceApproved` flag to the Android bridge only after that exact match succeeds;
5. the Java bridge independently checks `Voice.isNetworkConnectionRequired()` and rejects a network voice unless the approval flag is true;
6. an approval flag attached to a non-network voice is rejected as an identity/policy mismatch.

This defense-in-depth boundary prevents a UI/default-selection bug from silently escalating an offline request into network-backed speech.

## Exact voice and locale

RMA-124 uses `TextToSpeech.setVoice()` rather than `setLanguage()`. Before audio starts, the Java bridge verifies that `getVoice()` still reports the exact requested voice ID, exact configured locale, and the same network-required classification. Non-network voices must also have their data installed.

There is no closest-match locale fallback, alternate-voice fallback, `QUEUE_FLUSH` replacement behavior, automatic model/data installation, or provider substitution.

## RMA-123 remains independent

`AndroidOfflineTtsProvider` and `ReachyOfflineTtsBridge` remain unchanged. RMA-123 continues to reject every network-required voice. RMA-124 is a separate provider and is never invoked automatically when RMA-123 is unavailable.

Likewise, RMA-124 does not fall back to RMA-123, OpenAI, another cloud provider, or an independent HTTP transport. A failed utterance is surfaced to the caller and is not retried automatically at this layer.

## Lifecycle and failure behavior

Only one provider operation may run at a time; competing operations fail busy rather than queueing. Caller cancellation, operation timeout, and provider disposal cancel the Android operation. Cancellation also removes work waiting for asynchronous `TextToSpeech` initialization. Teardown stops active speech and calls `TextToSpeech.shutdown()`.

Start, done, stop, and error callbacks carry the request identity through the Unity bridge. Callback identity mismatches and bounded-queue overflow become visible terminal failures rather than silent drops. Android network and network-timeout errors map to the structured `Network` error category.

## Privacy boundary

RMA-124 itself owns no HTTP client, endpoint, API key, or speech-payload persistence. Network behavior, when an explicitly selected Android voice requires it, is controlled by the selected Android TTS engine. The application therefore cannot claim that such a voice is private/offline; settings and diagnostics must show it as network-required.

## Validation boundary

The deterministic managed suite exercises the provider policy without a live Android TTS engine or network. The repository Android job compiles the production Java bridge with Java 17 `-Xlint:all -Werror` and Android lint with warnings-as-errors. The standard `kawa` physical regression proves packaging and no regressions on the API-26 application path.

That standard regression does not itself speak an RMA-124 utterance or prove actual network-backed audio. Positive audible TTS acceptance remains a separate Phase-13 speech-integration test and must record the engine, exact voice ID/locale, network-required status, callbacks, and physical output evidence.

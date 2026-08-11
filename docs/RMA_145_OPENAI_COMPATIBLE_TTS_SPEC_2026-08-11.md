# RMA-145 — OpenAI / OpenAI-Compatible TTS Specification

Date: 2026-08-11

## Goal

Implement a cloud/local-network text-to-speech provider over the existing `ITtsProvider` boundary using the shared RMA-140/RMA-141 provider configuration and HTTP transport layers. The provider must be explicit about endpoint, model, voice, output format, instruction support, authentication, MIME expectations, resource bounds, and cancellation. It must not silently switch providers, models, formats, authentication modes, or transport behavior.

## Provider contract

`ReachyOpenAiCompatibleTtsProvider` implements `ITtsProvider` and requires:

- a `ReachyProviderProfile` with endpoint style `AudioSpeech`;
- a configured `Tts` model binding;
- an explicit authentication mode (`None`, primary Bearer credential reference, or configured headers);
- a bounded non-empty declared voice set;
- `Cloud` or `LocalNetwork` provider location;
- an `IBufferedTtsAudioSink` for asynchronous audio handoff;
- a `ReachyOpenAiCompatibleTtsOptions` instance describing endpoint path, response format, request limits, response byte limits, instruction support, and accepted MIME types.

RMA-145 is deliberately a buffered provider. Profiles with `StreamingEnabled=true` are rejected rather than silently buffered.

## Wire request

The default endpoint is `v1/audio/speech`. The endpoint remains configurable as a bounded relative path for OpenAI-compatible servers.

The JSON request contains:

- `model` from the provider profile's `Tts` model binding;
- `input` from the `TtsRequest`;
- `voice` from the selected declared voice;
- `response_format` from the explicit encoded-audio format;
- `instructions` only when instructions are configured and the profile explicitly declares instruction support.

The supported response-format enum is:

- MP3;
- Opus;
- AAC;
- FLAC;
- WAV;
- PCM.

Input is bounded to at most 4096 characters. Instruction text is separately bounded to at most 4096 characters.

## Response validation

The shared HTTP transport now preserves the bounded response media type on successful results. RMA-145 requires:

- a 2xx transport result;
- a non-empty response body;
- response size at or below the configured byte bound;
- a present MIME type contained in the configured format-compatible allowlist.

A missing or unexpected MIME type is a typed malformed-response failure. The provider does not infer the format from filename conventions, magic bytes, or fallback defaults.

## Audio ownership and cleanup

`BufferedTtsAudio` owns a cloned audio buffer and zeroes it on disposal. The TTS provider:

1. builds a bounded JSON request buffer;
2. lets the shared transport clone/own its send buffer;
3. receives the bounded transport response buffer;
4. constructs a `BufferedTtsAudio` lease for the sink;
5. awaits the sink asynchronously;
6. disposes/zeroes the audio lease;
7. zeroes the borrowed transport response buffer and local request buffer.

No temporary audio file is created.

## Cancellation and failure behavior

The effective operation timeout is the minimum of the request timeout and provider-profile timeout. Caller cancellation produces `TtsEventKind.Cancelled`; timeout produces a typed timeout failure. Audio-sink failures are surfaced as `TTS_AUDIO_SINK_FAILURE` without leaking arbitrary sink exception text.

Non-idempotent TTS POSTs are not automatically retried. Provider/model fallback is not performed by this adapter.

## Security

Bearer authentication reuses `ReachyBearerCredentialTransportBinding`; credentials remain in the secure secret store and are transformed to the `Authorization` header only in memory. Sensitive values are not written to the provider profile or diagnostics.

TLS/cleartext rules remain governed by the RMA-140 provider profile and RMA-141 shared transport. No certificate-validation bypass or redirect relaxation is introduced.

## Closure boundary

The source implementation and local static contracts are complete. Formal RMA-145 closure requires the managed warnings-as-errors build and Unity compilation to compile the exact source set. Those compile gates are intentionally not claimed by this sandbox because it has no local .NET/Unity toolchain.

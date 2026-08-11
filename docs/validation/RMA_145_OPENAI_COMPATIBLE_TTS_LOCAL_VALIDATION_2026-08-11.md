# RMA-145 Local Validation — 2026-08-11

## Scope

Local source/static validation for the RMA-145 buffered OpenAI/OpenAI-compatible TTS implementation. This is not a substitute for the managed warnings-as-errors build or Unity compilation.

## Source set

- `Assets/ReachyMini/Runtime/Core/Speech/BufferedTtsAudioContracts.cs`
- `Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleTtsOptions.cs`
- `Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleTtsProvider.Core.cs`
- `Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleTtsProvider.Helpers.cs`
- RMA-141 transport content-type propagation in `ReachyHttpTransportContracts.cs`, `ReachySharedHttpTransport.Core.cs`, and `ReachySharedHttpTransport.Helpers.cs`

## Local checks

The following checks passed after the final source review:

- `python3 scripts/tests/test_rma140_secure_provider_configuration.py` — 4/4
- `python3 scripts/tests/test_rma141_shared_http_transport.py` — 6/6
- `python3 scripts/tests/test_rma142_openai_responses_llm_adapter.py` — pass
- `python3 scripts/tests/test_rma143_openai_compatible_text_adapters.py` — 8/8
- `python3 scripts/tests/test_rma144_openai_compatible_asr.py` — 8/8
- `python3 scripts/tests/test_rma145_openai_compatible_tts.py` — 8/8
- `python3 scripts/tests/test_provider_source_set_integrity.py` — 4/4
- Python `compileall` over `scripts/tests` — pass
- `git diff --check` — pass
- lexical delimiter scan over all new RMA-145 C# files — pass

## Validated invariants

- Existing `ITtsProvider` contract is used.
- Default `/v1/audio/speech` path is configurable without permitting absolute URL/traversal/query injection.
- Model, voice, response format, and supported instructions are explicit.
- MP3/Opus/AAC/FLAC/WAV/PCM response formats are represented explicitly.
- Response MIME and response byte bounds fail closed.
- Audio is handed to an asynchronous sink; no blocking wait or temporary file path exists.
- Owned request/audio buffers are cleared.
- Caller cancellation and timeout are distinguished.
- Audio-sink failures are typed and fail-visible.
- Bearer/configured-header/no-auth modes are explicit.
- Non-idempotent POST retry is not authorized implicitly.
- Streaming-enabled profiles are rejected for this buffered milestone rather than silently downgraded.
- Source-set integrity now checks all four RMA-145 source components.

## Remaining external validation

- Managed warnings-as-errors compile.
- Unity script compilation / package validation.
- Any platform audio-sink integration acceptance required by the broader speech UX milestone.

RMA-145 must remain described as locally implemented, not formally closed, until the compile gates pass.

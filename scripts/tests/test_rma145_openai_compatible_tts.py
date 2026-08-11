import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROVIDER_FILES = [
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleTtsOptions.cs",
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleTtsProvider.Core.cs",
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleTtsProvider.Helpers.cs",
]
AUDIO = ROOT / "Assets/ReachyMini/Runtime/Core/Speech/BufferedTtsAudioContracts.cs"
TRANSPORT_FILES = [
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyHttpTransportContracts.cs",
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachySharedHttpTransport.Core.cs",
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachySharedHttpTransport.Helpers.cs",
]


def source(paths: list[Path]) -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in paths)


class Rma145OpenAiCompatibleTtsTests(unittest.TestCase):
    def test_provider_uses_existing_tts_contract(self) -> None:
        provider = source(PROVIDER_FILES)
        self.assertIn("ITtsProvider", provider)
        self.assertIn("TtsCapabilities", provider)
        self.assertIn("TtsEventKind.Started", provider)
        self.assertIn("TtsEventKind.Completed", provider)
        self.assertIn("TtsEventKind.Cancelled", provider)
        self.assertIn("SpeechProviderError", provider)

    def test_request_contract_is_openai_compatible_and_configurable(self) -> None:
        provider = source(PROVIDER_FILES)
        self.assertIn('"v1/audio/speech"', provider)
        self.assertIn('"model"', provider)
        self.assertIn('"input"', provider)
        self.assertIn('"voice"', provider)
        self.assertIn('"response_format"', provider)
        self.assertIn('"instructions"', provider)
        self.assertIn("RelativeSpeechPath", provider)
        self.assertIn("ReachyProviderModelRole.Tts", provider)
        self.assertIn("MaximumInputCharactersLimit = 4096", provider)

    def test_supported_formats_are_explicit(self) -> None:
        combined = source([*PROVIDER_FILES, AUDIO])
        for value in ("Mp3", "Opus", "Aac", "Flac", "Wav", "Pcm"):
            self.assertIn(f"TtsEncodedAudioFormat.{value}", combined)
        for wire in ('"mp3"', '"opus"', '"aac"', '"flac"', '"wav"', '"pcm"'):
            self.assertIn(wire, combined)

    def test_response_mime_and_size_fail_closed(self) -> None:
        provider = source(PROVIDER_FILES)
        transport = source(TRANSPORT_FILES)
        self.assertIn("AcceptedResponseContentTypes", provider)
        self.assertIn("AcceptsContentType(result.ContentType)", provider)
        self.assertIn("TTS_RESPONSE_CONTENT_TYPE_INVALID", provider)
        self.assertIn("TTS_RESPONSE_EMPTY", provider)
        self.assertIn("MaximumResponseBytes", provider)
        self.assertIn("public string? ContentType", transport)
        self.assertIn("responseContent.Headers.ContentType?.MediaType", transport)

    def test_audio_ownership_is_bounded_and_zeroing(self) -> None:
        audio = AUDIO.read_text(encoding="utf-8")
        provider = source(PROVIDER_FILES)
        self.assertIn("IBufferedTtsAudioSink", audio)
        self.assertIn("BufferedTtsAudio : IDisposable", audio)
        self.assertIn("Array.Clear(owned", audio)
        self.assertIn("Array.Clear(\n                        borrowedResponseBody", provider)
        self.assertIn("Array.Clear(requestBody", provider)
        self.assertIn("audio?.Dispose()", provider)
        self.assertNotIn("File.Write", provider)
        self.assertNotIn("Path.GetTemp", provider)

    def test_async_handoff_and_cancellation_do_not_block(self) -> None:
        combined = source([*PROVIDER_FILES, AUDIO])
        self.assertIn("await audioSink.PlayAsync", combined)
        self.assertIn("CancellationTokenSource.CreateLinkedTokenSource", combined)
        self.assertIn("TTS_AUDIO_SINK_FAILURE", combined)
        self.assertNotIn(".Wait()", combined)
        self.assertNotIn(".Result", combined)
        self.assertNotIn("Thread.Sleep", combined)
        self.assertNotIn("Task.Run", combined)

    def test_authentication_and_retry_are_explicit(self) -> None:
        provider = source(PROVIDER_FILES)
        self.assertIn("ReachyCompatibleTtsAuthenticationMode.None", provider)
        self.assertIn("ReachyCompatibleTtsAuthenticationMode.BearerCredentialReference", provider)
        self.assertIn("ReachyCompatibleTtsAuthenticationMode.ConfiguredHeaders", provider)
        self.assertIn("ReachyBearerCredentialTransportBinding.Create", provider)
        self.assertIn("explicitlyAuthorizeNonIdempotentRetry: false", provider)
        self.assertIn("idempotencyKey: null", provider)
        self.assertNotIn(
            "catch (Exception)\n                {\n                    return await",
            provider,
        )

    def test_buffered_milestone_rejects_implicit_streaming(self) -> None:
        provider = source(PROVIDER_FILES)
        self.assertIn("profile.StreamingEnabled", provider)
        self.assertIn("buffered TTS profiles must disable streaming explicitly", provider)
        self.assertIn("ReachyHttpResponseMode.Buffered", provider)


if __name__ == "__main__":
    unittest.main()

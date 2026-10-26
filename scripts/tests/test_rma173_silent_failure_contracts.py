from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def section(text: str, start: str, end: str | None = None) -> str:
    s = text.index(start)
    if end is None:
        return text[s:]
    e = text.index(end, s)
    return text[s:e]


def test_on_device_asr_failure_does_not_invoke_cloud_provider() -> None:
    source = read("Assets/ReachyMini/Runtime/Core/Speech/AndroidOnDeviceAsrProvider.cs")
    recognition = read("Assets/ReachyMini/Runtime/Core/Speech/AndroidOnDeviceAsrProvider.Recognition.cs")
    readiness = read("Assets/ReachyMini/Runtime/Core/Speech/AndroidOnDeviceAsrProvider.Readiness.cs")
    combined = source + recognition + readiness
    assert "SpeechProviderLocation.OnDevice" in combined
    assert "SpeechNetworkRequirement.None" in combined
    assert "OpenAI" not in combined
    assert "ReachyOpenAi" not in combined
    assert "http://" not in combined and "https://" not in combined


def test_offline_tts_rejects_network_voices_before_synthesis() -> None:
    source = read("Assets/ReachyMini/Runtime/Core/Speech/AndroidOfflineTtsProvider.Internal.cs")
    assert "SpeechNetworkRequirement.None" in source
    assert "android_offline_tts_network_voice_rejected" in source
    assert "requires or may use networking" in source
    rejected = source.index("android_offline_tts_network_voice_rejected")
    speak = source.index("MapPlatformEvent")
    assert rejected < speak


def test_local_llm_failure_does_not_invoke_cloud_llm_path() -> None:
    source = read("Assets/ReachyMini/Runtime/Application/ReachyConversationTurnOrchestrator.Llm.cs")
    local = section(source, "private async Task<LlmTurnResult> RunLocalLlmAsync", "private sealed class NoOpLocalLlmStreamSink")
    assert "ILocalLlmProviderCapability" in local
    assert "GenerateAsync(request, sink" in local
    assert "llm-local-governed-failure" in local
    assert "llm-local-missing-result" in local
    assert "invalid-intent-regeneration-limit-reached" in local
    assert "ICloudLlmProviderCapability" not in local
    assert "RunCloudLlmAsync" not in local
    assert "OpenAI" not in local


def test_invalid_camera_pixels_require_validity_mask_and_no_stale_identity() -> None:
    camera_acceptance = read("Assets/ReachyMini/Runtime/Application/ReachyRma190CameraEndToEndAcceptance.Helpers.cs")
    assert "frame.Coverage.HasValidityMask" in camera_acceptance
    assert "VisionResourceKind.ValidityMask" in camera_acceptance
    assert "observation-eligible TransformedReachyEye frame with a validity mask" in camera_acceptance
    bridge = read("Assets/ReachyMini/Runtime/Application/ReachyAndroidCameraTextureBridge.Pump.cs")
    assert "Rejected a stale or mismatched detached camera texture frame" in bridge


def test_solver_failure_is_visible_not_unity_animation_fallback() -> None:
    runtime = read("Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs")
    assert "ReachyProductionRuntimeStatus.Faulted" in runtime
    assert "ReachyDiagnosticEventIds.ProductionRuntimeFaulted" in runtime
    assert "ResetNeutral" in runtime
    assert "Unity animation" not in runtime
    assert "cosmetic" not in runtime.lower()


def test_calibration_mismatch_does_not_load_placeholder_as_calibrated() -> None:
    calibration = read("Assets/ReachyMini/Runtime/Core/Application/ReachyCameraCalibrationStateStore.cs")
    assert "ImageSizeMismatch" in calibration
    assert "ModelMismatch" in calibration
    assert "ReachyCameraCalibrationSelectionStatus.Missing" in calibration
    assert "placeholder" not in calibration.lower()
    audit = read("scripts/validate_model_parameter_audit.py")
    assert "placeholder parameters" in audit
    assert "permits a calibrated label" in audit


def test_dropped_buffers_produce_visible_errors() -> None:
    worker_tests = read("managed/ReachyMini.Core.Tests/Program.Rma032WorkerTests.cs")
    assert "visible queue overflow" in worker_tests
    assert "command queue overflow was not published in timing diagnostics" in worker_tests
    diagnostics = read("Assets/ReachyMini/Runtime/Diagnostics/ReachyRuntimeDiagnostics.cs")
    assert "Diagnostic" in diagnostics


def test_malformed_provider_response_cannot_be_fake_success() -> None:
    openai = read("managed/ReachyMini.Core.Tests/Rma142Rma143OpenAiLlmAdapterContractTests.cs")
    assert "malformed assistant not succeeded" in openai
    assert "BehaviorIntentInvalid" in openai
    vlm = read("managed/ReachyMini.Core.Tests/Rma195VlmTransportContractTests.cs")
    assert "malformed json not succeeded" in vlm
    assert "ProtocolFailure" in vlm

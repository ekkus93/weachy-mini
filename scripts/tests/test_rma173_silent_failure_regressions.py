#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


class Rma173SilentFailureRegressionContracts(unittest.TestCase):
    def test_on_device_asr_failure_has_no_cloud_fallback(self) -> None:
        stack = read("Assets/ReachyMini/Runtime/Application/ReachyAndroidSpeechAudioStack.cs")
        contract = read(
            "managed/ReachyMini.AndroidOnDeviceAsr.Tests/Rma121SourceContracts.cs"
        )
        self.assertIn("ReachyAndroidOnDeviceAsrProviderFactory.Create", stack)
        self.assertNotIn("OpenAI", stack)
        self.assertIn('Reject(\n            source,\n            "OpenAI"', contract)

    def test_offline_tts_failure_has_no_network_voice_fallback(self) -> None:
        stack = read("Assets/ReachyMini/Runtime/Application/ReachyAndroidSpeechAudioStack.cs")
        contract = read(
            "managed/ReachyMini.AndroidOfflineTts.Tests/Rma123SourceContracts.cs"
        )
        behavior = read(
            "managed/ReachyMini.AndroidOfflineTts.Tests/AndroidOfflineTtsTests.Core.cs"
        )
        self.assertIn("ReachyAndroidOfflineTtsProviderFactory.Create", stack)
        self.assertNotIn("OpenAI", stack)
        self.assertIn("no alternate TTS provider was selected", contract)
        self.assertIn("NetworkVoiceRequestIsRejected", behavior)
        self.assertIn(
            "Network-required voice must be rejected before platform synthesis",
            behavior,
        )

    def test_local_llm_failure_does_not_activate_openai(self) -> None:
        source = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachyConversationTurnOrchestrator.Llm.cs"
        )
        self.assertIn("switch (execution)", source)
        self.assertIn("case ReachyProviderExecution.Cloud:", source)
        self.assertIn("return await RunCloudLlmAsync", source)
        self.assertIn("case ReachyProviderExecution.OnDevice:", source)
        self.assertIn("return await RunLocalLlmAsync", source)
        self.assertIn('"llm-local-governed-failure"', source)
        local_start = source.index(
            "private async Task<LlmTurnResult> RunLocalLlmAsync"
        )
        local_body = source[local_start:]
        self.assertNotIn("RunCloudLlmAsync", local_body)

    def test_invalid_camera_pixels_do_not_reuse_stale_content(self) -> None:
        source = read(
            "Assets/ReachyMini/Tests/Editor/"
            "ReachyCameraReprojectionSafetyTests.cs"
        )
        self.assertIn("invalid pixel", source)
        self.assertIn("retained red", source)
        self.assertIn("retained green", source)
        self.assertIn("retained blue", source)
        self.assertIn("Is.LessThan(ColorTolerance)", source)

    def test_solver_failure_cannot_switch_to_unity_animation(self) -> None:
        source = read(
            "Assets/ReachyMini/Runtime/Rendering/ReachyAuthoritativeRenderer.cs"
        )
        self.assertIn("ContainsProhibitedWriter", source)
        self.assertIn("GetComponentInChildren<Animator>", source)
        self.assertIn("GetComponentInChildren<Animation>", source)
        self.assertIn("GetComponentInChildren<PlayableDirector>", source)

    def test_calibration_mismatch_cannot_label_placeholders_calibrated(self) -> None:
        source = read("scripts/tests/test_model_parameter_audit.py")
        self.assertIn(
            "test_placeholder_parameters_cannot_be_labeled_calibrated",
            source,
        )
        self.assertIn('audit["fidelity"]["calibrated"] = True', source)
        self.assertIn(
            'self.assertIn("placeholder parameters", result.stderr)',
            source,
        )

    def test_dropped_command_and_state_buffers_are_visible(self) -> None:
        worker_test = read(
            "managed/ReachyMini.Core.Tests/Program.Rma032WorkerTests.cs"
        )
        state_test = read(
            "managed/ReachyMini.Core.Tests/Rma173SilentFailureRegressionTests.cs"
        )
        timing = read(
            "Assets/ReachyMini/Runtime/Simulation/ReachySimulationSnapshots.cs"
        )
        diagnostics = read(
            "Assets/ReachyMini/Runtime/Application/ReachyDiagnosticsScreenSource.cs"
        )
        self.assertIn(
            "command queue overflow was not published in timing diagnostics",
            worker_test,
        )
        self.assertIn("DroppedStateSnapshotCount", timing)
        self.assertIn("UnreadStatePublicationOverwriteIsCounted", state_test)
        self.assertIn('"Dropped state snapshots"', diagnostics)
        self.assertIn("ReachyDiagnosticsMetric.Degraded", diagnostics)

    def test_malformed_provider_response_cannot_become_success(self) -> None:
        source = read(
            "managed/ReachyMini.Core.Tests/"
            "Rma142Rma143OpenAiLlmAdapterContractTests.cs"
        )
        self.assertIn("MalformedAssistantJsonSurfacesAsInvalidNotThrown", source)
        self.assertIn(
            "ReachyLlmGenerationStatus.BehaviorIntentInvalid",
            source,
        )
        self.assertIn(
            'False(result.Succeeded, "malformed assistant not succeeded")',
            source,
        )


if __name__ == "__main__":
    unittest.main()

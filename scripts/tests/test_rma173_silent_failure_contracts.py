#!/usr/bin/env python3
"""RMA-173 source contracts for no-silent-fallback failure behavior.

These tests intentionally inspect the shipping production paths rather than test-only
fakes. They do not claim physical Android acceptance; they prevent regressions where a
failure path silently swaps provider, uses stale perception, fabricates successful AI
output, or bypasses the authoritative simulation path.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"
RENDERING = ROOT / "Assets/ReachyMini/Runtime/Rendering"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def segment_after(text: str, marker: str) -> str:
    return text[text.index(marker) :]


class Rma173SilentFailureContractTests(unittest.TestCase):
    def test_on_device_asr_failure_does_not_activate_cloud_asr(self) -> None:
        asr = read(APP / "ReachyConversationTurnOrchestrator.Asr.cs")
        self.assertIn("ReachyAndroidOnDeviceAsrProviderFactory.Create", asr)
        self.assertIn("ReachyAndroidSystemAsrProviderFactory.Create", asr)
        self.assertIn("return null;", asr)
        self.assertIn(
            "No usable on-device or system speech-recognition provider is available.",
            asr,
        )
        self.assertNotIn("OpenAi", asr)
        self.assertNotIn("Cloud", asr)

    def test_offline_tts_failure_does_not_activate_network_voice(self) -> None:
        tts = read(APP / "ReachyConversationTurnOrchestrator.Tts.cs")
        policy = read(CORE / "Speech/ReachyTtsVoiceSelectionPolicy.cs")
        self.assertIn("ReachyAndroidOfflineTtsProviderFactory.Create", tts)
        self.assertIn("ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice", tts)
        self.assertIn("return null;", tts)
        self.assertIn("voice.MayUseNetwork", policy)
        self.assertIn("!voice.IsInstalled", policy)
        self.assertIn("StringComparison.OrdinalIgnoreCase", policy)
        self.assertNotIn("return voices[0]", tts + policy)
        self.assertNotIn("NetworkTts", tts)
        self.assertNotIn("Cloud", tts)

    def test_local_llm_failure_does_not_activate_openai_or_cloud_path(self) -> None:
        llm = read(APP / "ReachyConversationTurnOrchestrator.Llm.cs")
        local = segment_after(
            llm,
            "private async Task<LlmTurnResult> RunLocalLlmAsync",
        )
        self.assertIn("RunCloudLlmAsync", llm)
        self.assertNotIn("RunCloudLlmAsync", local)
        self.assertNotIn("ICloudLlmProviderCapability", local)
        self.assertNotIn("ReachyProviderExecution.Cloud", local)
        self.assertIn("MaximumRegenerationAttempts", local)
        self.assertIn("return LlmTurnResult.Failure", local)
        self.assertIn("llm-local-governed-failure", local)
        self.assertIn("invalid-intent-regeneration-limit-reached", local)

    def test_invalid_camera_pixels_do_not_reuse_stale_world_model_content(self) -> None:
        driver = read(APP / "ReachyAndroidPerceptionDriver.cs")
        fault_publish = (
            "PublishSnapshot(\n"
            "                            ReachyPerceptionServiceExecutionState.Faulted,\n"
            "                            null,"
        )
        self.assertIn("FrameBuildResult.Failure", driver)
        self.assertIn("ReachyPerceptionServiceExecutionState.NoCameraFrame", driver)
        self.assertIn("PublishSnapshot(built.FailureState, null, built.FailureMessage)", driver)
        self.assertIn("Tracking failed", driver)
        self.assertIn(fault_publish, driver)
        self.assertIn("The world model rejected a tracking batch", driver)
        self.assertIn("ReachyUnityVisionFrameFactory.CreateOwnedTrackingFrame", driver)

    def test_solver_or_controller_failure_does_not_switch_to_unity_animation(self) -> None:
        runtime = read(RENDERING / "ReachyProductionAuthoritativeRuntime.cs")
        sink = read(RENDERING / "ReachyProductionBehaviorControllerTargetSink.cs")
        self.assertIn("worker.State == ReachySimulationRunState.Faulted", runtime)
        self.assertIn("EnterFault(", runtime)
        self.assertIn("renderer.enabled = false", runtime)
        self.assertIn("ShutdownRuntime(logFailures: true, preserveFault: true)", runtime)
        self.assertIn("runtime.SubmitPositionTargets(targets)", sink)
        self.assertIn("ReachySimulationCommandEnqueueResult.QueueFull", sink)
        self.assertIn("ReachySimulationCommandEnqueueResult.WorkerUnavailable", sink)
        self.assertNotIn("Animation.Play", runtime + sink)
        self.assertNotIn("Animator.Play", runtime + sink)

    def test_calibration_mismatch_does_not_load_placeholder_as_calibrated(self) -> None:
        driver = read(APP / "ReachyAndroidPerceptionDriver.cs")
        self.assertIn("SelectExact(", driver)
        self.assertIn("calibrationSelection.Profile", driver)
        self.assertIn("calibrationProfile == null", driver)
        self.assertIn("ReachyPerceptionServiceExecutionState.NoCalibration", driver)
        self.assertIn("No exact camera calibration is available", driver)
        self.assertIn("fail\n                    // closed as ImageSizeMismatch", driver)
        self.assertNotIn("placeholder", driver.lower())

    def test_dropped_command_and_state_buffers_produce_visible_errors(self) -> None:
        runtime = read(RENDERING / "ReachyProductionAuthoritativeRuntime.cs")
        sink = read(RENDERING / "ReachyProductionBehaviorControllerTargetSink.cs")
        self.assertIn("worker.EnqueueCommandBatch(batch)", runtime)
        self.assertIn(
            "if (result == ReachySimulationCommandEnqueueResult.Accepted)",
            runtime,
        )
        self.assertIn("nextCommandSequence = checked(nextCommandSequence + 1UL)", runtime)
        self.assertIn("ReachyBehaviorTargetSubmissionStatus.QueueFull", sink)
        self.assertIn("ReachyBehaviorTargetSubmissionStatus.ControllerUnavailable", sink)
        self.assertIn("TryCaptureLatestAuthoritativeState", runtime)
        self.assertIn("Status == ReachyProductionRuntimeStatus.Running", runtime)

    def test_malformed_provider_response_does_not_fabricate_successful_intent(self) -> None:
        llm = read(APP / "ReachyConversationTurnOrchestrator.Llm.cs")
        orchestrator = read(APP / "ReachyConversationTurnOrchestrator.cs")
        self.assertIn("llm-missing-validation", llm)
        self.assertIn("ReachyBehaviorIntentRecoveryPolicy.Evaluate", llm)
        self.assertIn(
            "decision.Action != ReachyBehaviorIntentRecoveryAction.Regenerate",
            llm,
        )
        self.assertIn("return LlmTurnResult.Failure", llm)
        self.assertIn("FailTurnClosed(llmResult.DiagnosticCode, llmResult.Detail)", orchestrator)
        self.assertIn("no fabricated apology speech, no silent", orchestrator)


if __name__ == "__main__":
    unittest.main()

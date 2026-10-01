#!/usr/bin/env python3
"""RMA-173 silent-failure regression gate.

These contracts are intentionally source-level and repository-wide: they make
privacy, provider-boundary, calibration, stale-frame, solver-fault, and malformed
AI-output failures stay visibly failed instead of becoming hidden fallbacks.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"
MANAGED_CORE_TESTS = ROOT / "managed/ReachyMini.Core.Tests"
NATIVE_TESTS = ROOT / "native/reachy_sim/tests"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma173SilentFailureRegressionTests(unittest.TestCase):
    def test_on_device_asr_failure_does_not_activate_cloud_or_system_asr(self) -> None:
        asr_turn = read(APP / "ReachyConversationTurnOrchestrator.Asr.cs")
        self.assertIn("ReachyAndroidOnDeviceAsrProviderFactory.Create", asr_turn)
        self.assertNotIn("ReachyAndroidSystemAsrProviderFactory.Create", asr_turn)
        self.assertNotIn("OpenAi", asr_turn)
        self.assertIn("return null;", asr_turn)
        self.assertIn("never a fallback", asr_turn)

    def test_offline_tts_failure_does_not_activate_network_voice(self) -> None:
        tts_turn = read(APP / "ReachyConversationTurnOrchestrator.Tts.cs")
        policy = read(CORE / "Speech/ReachyTtsVoiceSelectionPolicy.cs")
        self.assertIn("ReachyAndroidOfflineTtsProviderFactory.Create", tts_turn)
        self.assertNotIn("ReachyAndroidSystemTtsProviderFactory.Create", tts_turn)
        self.assertNotIn("OpenAi", tts_turn)
        self.assertIn("SelectExactOfflineVoice", tts_turn)
        self.assertIn("voice.IsInstalled", policy)
        self.assertIn("voice.MayUseNetwork", policy)

    def test_local_llm_failure_does_not_activate_openai_or_cloud_llm(self) -> None:
        llm_turn = read(APP / "ReachyConversationTurnOrchestrator.Llm.cs")
        switch_start = llm_turn.index("switch (execution)")
        switch_end = llm_turn.index(
            "private async Task<LlmTurnResult> RunCloudLlmAsync", switch_start
        )
        switch = llm_turn[switch_start:switch_end]
        self.assertIn("case ReachyProviderExecution.Cloud:", switch)
        self.assertIn("case ReachyProviderExecution.OnDevice:", switch)
        local_case = switch.index("case ReachyProviderExecution.OnDevice:")
        local_branch = switch[local_case:]
        self.assertIn("RunLocalLlmAsync", local_branch)
        self.assertNotIn("RunCloudLlmAsync", local_branch)
        self.assertIn("llm-local-governed-failure", llm_turn)
        self.assertIn("invalid-intent-regeneration-limit-reached", llm_turn)

    def test_invalid_camera_pixels_do_not_reuse_stale_content(self) -> None:
        driver = read(APP / "ReachyAndroidPerceptionDriver.cs")
        self.assertIn("ReachyUnityVisionFrameFactory.CreateOwnedTrackingFrame", driver)
        self.assertIn("catch (ArgumentException exception)", driver)
        self.assertIn("coverage is not observation-eligible", driver)
        self.assertIn("FrameBuildResult.Failure", driver)
        self.assertNotIn("lastSuccessfulFrame", driver)
        self.assertNotIn("cachedTrackingFrame", driver)

    def test_solver_failure_does_not_switch_to_unity_animation(self) -> None:
        simulation = read(APP / "ReachyProductionSimulationApplicationService.cs")
        settings = read(APP / "ReachySettingsApplicationCompositionProvider.cs")
        self.assertIn("ReachyProductionAuthoritativeRuntime", simulation)
        self.assertIn("throw new InvalidOperationException", simulation)
        self.assertIn("new ReachyProductionSimulationApplicationService", settings)
        self.assertNotIn("Animator", simulation)
        self.assertNotIn("AnimationClip", simulation)
        self.assertNotIn("fallback", simulation.lower())

    def test_calibration_mismatch_does_not_load_placeholders_as_calibrated(self) -> None:
        driver = read(APP / "ReachyAndroidPerceptionDriver.cs")
        self.assertIn("calibrations!.State.SelectExact", driver)
        self.assertIn("calibrationProfile == null", driver)
        self.assertIn("No exact camera calibration is available", driver)
        self.assertIn("ReachyPerceptionServiceExecutionState.NoCalibration", driver)
        self.assertNotIn("placeholder calibration", driver.lower())
        self.assertNotIn("fallback calibration", driver.lower())

    def test_dropped_command_and_state_buffers_produce_visible_errors(self) -> None:
        lifecycle = read(MANAGED_CORE_TESTS / "Program.Rma031SessionLifecycleTests.cs")
        native_contract = read(NATIVE_TESTS / "reachy_sim_contract_test_base.inc")
        native_concurrency = read(NATIVE_TESTS / "reachy_sim_concurrency_test.inc")
        self.assertIn("zero-step failure", lifecycle)
        self.assertIn("unknown reset failure", lifecycle)
        self.assertIn("operation after close", lifecycle)
        self.assertIn("stale handle after destroy", native_contract)
        self.assertIn("invalid state copy leaves required size unchanged", native_concurrency)
        self.assertIn("busy state copy leaves output unchanged", native_concurrency)
        self.assertIn("busy snapshot copy leaves output unchanged", native_concurrency)

    def test_malformed_provider_response_does_not_fake_successful_intent(self) -> None:
        llm_turn = read(APP / "ReachyConversationTurnOrchestrator.Llm.cs")
        cloud_method = llm_turn[
            llm_turn.index("private async Task<LlmTurnResult> RunCloudLlmAsync") : llm_turn.index(
                "private async Task<LlmTurnResult> RunLocalLlmAsync"
            )
        ]
        self.assertIn("validation == null", cloud_method)
        self.assertIn("llm-missing-validation", cloud_method)
        self.assertIn("validation.Succeeded && validation.Intent != null", cloud_method)
        self.assertIn("ReachyBehaviorIntentRecoveryPolicy.Evaluate", cloud_method)
        self.assertIn("Fail closed", cloud_method)
        self.assertNotIn("new ReachyBehaviorIntent", cloud_method)


if __name__ == "__main__":
    unittest.main()

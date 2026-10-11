#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma195ApplicationCompositionTests(unittest.TestCase):
    def test_shipping_bootstrap_uses_settings_composition_only(self) -> None:
        bootstrap = read(APP / "ReachyMainScreenBootstrap.cs")
        dead = APP / "ReachyProductionApplicationCompositionProvider.cs"
        self.assertFalse(dead.exists(), "obsolete production composition must stay removed")
        self.assertIn("ReachySettingsApplicationCompositionProvider provider =", bootstrap)
        self.assertIn(
            "AddComponent<\n"
            "                        ReachySettingsApplicationCompositionProvider>()",
            bootstrap,
        )
        self.assertNotIn("ReachyProductionApplicationCompositionProvider", bootstrap)

    def test_shipping_behavior_routes_complete_validated_intent_to_rma152_planner(self) -> None:
        contracts = read(CORE / "Application/ReachyApplicationContracts.cs")
        service = read(APP / "ReachyBaselineBehaviorApplicationService.cs")
        orchestrator = read(APP / "ReachyConversationTurnOrchestrator.cs")
        self.assertIn("interface IReachyBehaviorIntentCapability", contracts)
        self.assertIn("IReachyBehaviorIntentCapability", service)
        self.assertIn("bool TryExecuteIntent(", service)
        self.assertIn("intentPlanner.Plan(", service)
        self.assertIn("IReachyBehaviorIntentCapability? behaviorIntents", orchestrator)
        self.assertIn(
            "capability.TryExecuteIntent(intent, out string intentDiagnostic)", orchestrator
        )

    def test_rma135_workflow_does_not_hash_removed_composition(self) -> None:
        workflow = read(ROOT / ".github/workflows/rma135-resource-thermal-governor.yml")
        self.assertNotIn("ReachyProductionApplicationCompositionProvider.cs", workflow)
        self.assertEqual(
            3,
            workflow.count("ReachySettingsApplicationCompositionProvider.cs"),
        )

    def test_legacy_gesture_mapper_is_only_non_capability_fallback(self) -> None:
        orchestrator = read(APP / "ReachyConversationTurnOrchestrator.cs")
        capability_branch = orchestrator.index("if (capability != null)")
        mapper = orchestrator.index("ReachyBehaviorIntentGestureMapper.Map", capability_branch)
        warning = orchestrator.index("implement IReachyBehaviorIntentCapability", capability_branch)
        self.assertGreater(mapper, warning)
        self.assertIn("intent.GazeTarget != null", orchestrator[capability_branch:mapper])
        self.assertIn("intent.Expression.HasValue", orchestrator[capability_branch:mapper])
        self.assertIn("intent.Urgency.HasValue", orchestrator[capability_branch:mapper])
        self.assertIn("intent.Timing != null", orchestrator[capability_branch:mapper])

    def test_conversation_tts_uses_exact_installed_offline_voice_policy(self) -> None:
        tts = read(APP / "ReachyConversationTurnOrchestrator.Tts.cs")
        policy = read(CORE / "Speech/ReachyTtsVoiceSelectionPolicy.cs")
        self.assertIn("SelectExactOfflineVoice", tts)
        self.assertIn("voice.IsInstalled", policy)
        self.assertIn("voice.MayUseNetwork", policy)
        self.assertIn("StringComparison.OrdinalIgnoreCase", policy)
        self.assertNotIn("return voices[0]", tts)

    def test_speech_health_reports_partial_capability_and_setup_guidance(self) -> None:
        speech = read(APP / "ReachyAndroidSpeechCapabilityApplicationService.cs")
        self.assertIn("At least one speech capability is available.", speech)
        self.assertIn("Check individual ASR/TTS states", speech)
        self.assertIn("explicit on-device ASR service", speech)
        self.assertIn("requires Android API 31+", speech)
        self.assertIn("and an offline TTS voice in device settings", speech)
        self.assertNotIn("Microphone capture and speech playback are available.", speech)

    def test_speech_capability_does_not_claim_ready_before_probe_or_after_disposal(self) -> None:
        speech = read(APP / "ReachyAndroidSpeechCapabilityApplicationService.cs")
        base = read(CORE / "Application/ReachyApplicationServiceBase.cs")
        self.assertIn(
            "if (Health.State == ReachyServiceState.Initializing)",
            base,
        )
        on_initialize = speech[
            speech.index("protected override void OnInitialize()"):
            speech.index("protected override void OnDispose()")
        ]
        self.assertIn("Speech capability verification is pending", on_initialize)
        self.assertLess(
            on_initialize.index("SetDegraded("),
            on_initialize.index("_ = ProbeAsync(probeCancellation.Token)"),
        )
        probe = speech[speech.index("private async Task ProbeAsync("):]
        self.assertIn("if (cancellationToken.IsCancellationRequested)", probe)
        self.assertIn("No network speech provider was selected automatically.", probe)
        self.assertNotIn("exception.Message", probe)
        self.assertIn("probeCancellation?.Cancel();", speech)
        self.assertIn("probeCancellation?.Dispose();", speech)
        for name in (
            "local-unity-android-validation.yml",
            "local-unity-android-device-no-artifacts.yml",
        ):
            workflow = read(ROOT / ".github/workflows" / name)
            self.assertIn(
                "python3 -m unittest scripts.tests.test_rma195_application_composition",
                workflow,
            )
            self.assertLess(
                workflow.index("python3 -m unittest scripts.tests.test_rma195_application_composition"),
                workflow.index("- name: Pin one physical Android device"),
            )

    def test_live_speech_uses_one_shared_offline_audio_focus_stack(self) -> None:
        orchestrator = read(APP / "ReachyConversationTurnOrchestrator.cs")
        asr = read(APP / "ReachyConversationTurnOrchestrator.Asr.cs")
        tts = read(APP / "ReachyConversationTurnOrchestrator.Tts.cs")
        stack = read(APP / "ReachyAndroidSpeechAudioStack.cs")
        self.assertIn(
            "ReachyAndroidSpeechAudioStackFactory.CreateOfflineDefaultAsync",
            orchestrator,
        )
        self.assertIn("RunAsrAsync(\n                    speechStack.Asr", orchestrator)
        self.assertIn("RunTtsAsync(\n                    speechStack.Tts", orchestrator)
        self.assertIn("new SpeechAudioFocusCoordinator(", stack)
        self.assertIn("new AudioCoordinatedAsrProvider(", stack)
        self.assertIn("new AudioCoordinatedTtsProvider(", stack)
        self.assertIn("ReachyAndroidOnDeviceAsrProviderFactory.Create", stack)
        self.assertIn("ReachyAndroidOfflineTtsProviderFactory.Create", stack)
        self.assertNotIn("ReachyAndroidSystemAsrProviderFactory.Create", stack)
        self.assertNotIn("ReachyAndroidSystemTtsProviderFactory.Create", stack)
        self.assertNotIn("ReachyAndroidOnDeviceAsrProviderFactory.Create", asr)
        self.assertNotIn("ReachyAndroidOfflineTtsProviderFactory.Create", tts)
        self.assertIn("SelectExactOfflineVoice", tts)

    def test_llm_execution_does_not_cross_provider_boundary_on_failure(self) -> None:
        llm = read(APP / "ReachyConversationTurnOrchestrator.Llm.cs")
        switch_start = llm.index("switch (execution)")
        switch_end = llm.index("private async Task<LlmTurnResult> RunCloudLlmAsync", switch_start)
        switch = llm[switch_start:switch_end]
        cloud_case = switch.index("case ReachyProviderExecution.Cloud:")
        local_case = switch.index("case ReachyProviderExecution.OnDevice:")
        self.assertLess(cloud_case, local_case)
        local_body = switch[local_case:]
        self.assertIn("RunLocalLlmAsync", local_body)
        self.assertNotIn("RunCloudLlmAsync", local_body)

    def test_shared_simulation_adapter_survives_dead_composition_removal(self) -> None:
        simulation = APP / "ReachyProductionSimulationApplicationService.cs"
        settings = read(APP / "ReachySettingsApplicationCompositionProvider.cs")
        self.assertTrue(simulation.exists())
        self.assertIn("class ReachyProductionSimulationApplicationService", read(simulation))
        self.assertIn("new ReachyProductionSimulationApplicationService", settings)

    def test_physical_acceptance_uses_shipping_host_and_real_android_capabilities(self) -> None:
        acceptance = read(APP / "ReachyRma195ApplicationCompositionAcceptance.cs")
        runner = read(ROOT / "scripts/run_rma195_application_composition_acceptance_android.sh")
        workflow = read(ROOT / ".github/workflows/local-unity-android-validation.yml")
        self.assertIn("reachy_rma195_application_composition_acceptance", acceptance)
        self.assertIn("FindAnyObjectByType<ReachyApplicationHostBehaviour>", acceptance)
        self.assertIn("GetRequiredService<IReachyProviderService>", acceptance)
        self.assertIn("provider is not ILocalLlmProviderCapability", acceptance)
        self.assertIn("provider is not ICloudLlmProviderCapability", acceptance)
        self.assertIn("perception is not ICloudVlmProviderCapability", acceptance)
        self.assertIn("behavior is not IReachyBehaviorIntentCapability", acceptance)
        self.assertIn("ProbeOnDeviceAsrAsync", acceptance)
        self.assertIn("ExerciseOfflineTtsAsync", acceptance)
        self.assertIn("human_asr_transcript_injected = false", acceptance)
        self.assertIn("cloud_credentials_injected = false", acceptance)
        self.assertIn("local_model_injected = false", acceptance)
        self.assertIn("--ez reachy_rma195_application_composition_acceptance true", runner)
        self.assertIn("test -f '${REMOTE_RESULT_PATH}' && cat", runner)
        self.assertIn('if [[ "${report_json}" == \\{* ]]; then', runner)
        self.assertIn("if ! python3 -", runner)
        self.assertNotIn("capture()\n{\n  set +e", runner)
        self.assertIn('r["on_device_asr_state"] == "Available"', runner)
        self.assertIn('r["selected_tts_voice_may_use_network"] is False', runner)
        self.assertIn("run_rma195_application_composition_acceptance_android.sh", workflow)


if __name__ == "__main__":
    unittest.main()

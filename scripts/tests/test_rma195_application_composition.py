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
        self.assertIn('r["on_device_asr_state"] == "Available"', runner)
        self.assertIn('r["selected_tts_voice_may_use_network"] is False', runner)
        self.assertIn("run_rma195_application_composition_acceptance_android.sh", workflow)


if __name__ == "__main__":
    unittest.main()

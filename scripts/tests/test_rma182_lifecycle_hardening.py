#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


class Rma182LifecycleHardeningTests(unittest.TestCase):
    def test_simulation_pause_resume_keeps_no_catch_up_invariant(self) -> None:
        loop = read(
            "Assets/ReachyMini/Runtime/Simulation/ReachySimulationWorker.WorkerLoop.cs"
        )
        runtime = read(
            "Assets/ReachyMini/Runtime/Rendering/ReachyProductionAuthoritativeRuntime.cs"
        )
        self.assertGreaterEqual(loop.count("accumulatorSeconds = 0.0;"), 4)
        self.assertIn("case ControlRequestKind.Pause:", loop)
        self.assertIn("case ControlRequestKind.Resume:", loop)
        self.assertIn("previousTimestamp = Stopwatch.GetTimestamp();", loop)
        self.assertIn("PauseForApplicationInterruption()", runtime)
        self.assertIn("ResumeAfterApplicationInterruption()", runtime)
        self.assertNotIn("private void OnApplicationPause(bool paused)", runtime)

    def test_one_application_lifecycle_ingress_coordinates_services_and_camera(
        self,
    ) -> None:
        host = read(
            "Assets/ReachyMini/Runtime/Application/ReachyApplicationHostBehaviour.cs"
        )
        contracts = read(
            "Assets/ReachyMini/Runtime/Core/Application/ReachyApplicationInterruption.cs"
        )
        camera = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachyAndroidCameraAcquisition.Lifecycle.cs"
        )
        self.assertEqual(host.count("private void OnApplicationPause(bool paused)"), 1)
        self.assertIn("new ReachyApplicationInterruptionCoordinator(host)", host)
        self.assertIn("acquisition?.PauseForApplicationInterruption();", host)
        self.assertIn("result = coordinator.Pause();", host)
        self.assertIn("result = coordinator.Resume();", host)
        self.assertIn("acquisition?.ResumeAfterApplicationInterruption();", host)
        self.assertIn(
            "for (int index = ResumeOrder.Length - 1; index >= 0; --index)", contracts
        )
        self.assertIn(
            "for (int index = 0; index < ResumeOrder.Length; ++index)", contracts
        )
        self.assertNotIn("private void OnApplicationPause(bool paused)", camera)

    def test_camera_and_speech_release_or_cancel_active_resources(self) -> None:
        camera = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachyAndroidCameraAcquisition.Lifecycle.cs"
        )
        speech = read(
            "Assets/ReachyMini/Runtime/Core/Speech/"
            "SpeechAudioFocusCoordinator.Lifecycle.cs"
        )
        coordinated_asr = read(
            "Assets/ReachyMini/Runtime/Core/Speech/AudioCoordinatedAsrProvider.cs"
        )
        self.assertIn("RequirePlatform().Pause()", camera)
        self.assertIn("RequirePlatform().Resume()", camera)
        self.assertIn("RequirePlatform().Stop()", camera)
        self.assertIn("SpeechAudioInterruptionKind.ApplicationBackgrounded", speech)
        self.assertIn("interruptionGate.PauseForApplicationInterruption();", speech)
        self.assertIn("session.TryInterrupt(interruption)", speech)
        self.assertIn("lease.DisposeAsync()", coordinated_asr)

    def test_network_and_inference_jobs_are_generation_cancelled_not_restarted(
        self,
    ) -> None:
        http_core = read(
            "Assets/ReachyMini/Runtime/Core/Providers/ReachySharedHttpTransport.Core.cs"
        )
        http_lifecycle = read(
            "Assets/ReachyMini/Runtime/Core/Providers/"
            "ReachySharedHttpTransport.Lifecycle.cs"
        )
        llm = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/"
            "ReachyLocalLlmProvider.Generation.cs"
        )
        llm_lifecycle = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/"
            "ReachyLocalLlmProvider.Lifecycle.cs"
        )
        vlm = read(
            "Assets/ReachyMini/Runtime/Core/Perception/ReachyVlmScheduler.Lifecycle.cs"
        )
        scheduler = read("Assets/ReachyMini/Runtime/Core/Perception/ReachyVlmScheduler.cs")
        self.assertIn(
            "interruptionGate.CreateLinkedTokenSource(cancellationToken)", http_core
        )
        self.assertIn("IReachyApplicationInterruptionParticipant", http_lifecycle)
        self.assertIn(
            "interruptionGate.CreateLinkedTokenSource(cancellationToken)", llm
        )
        self.assertIn("IReachyApplicationInterruptionParticipant", llm_lifecycle)
        self.assertIn("lease.MarkCancellationRequested()", vlm)
        self.assertIn("if (lifecycleSuspended)", scheduler)
        self.assertIn("cancelled work is never restarted automatically", scheduler)

    def test_conversation_and_ui_resume_to_defined_state_without_overwriting_errors(
        self,
    ) -> None:
        conversation = read(
            "Assets/ReachyMini/Runtime/Core/Conversation/"
            "ReachyConversationStateMachine.Lifecycle.cs"
        )
        ui = read(
            "Assets/ReachyMini/Runtime/Core/Application/ReachyMainScreenState.Lifecycle.cs"
        )
        production = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachyProductionApplicationCompositionProvider.cs"
        )
        settings = read(
            "Assets/ReachyMini/Runtime/Application/"
            "ReachySettingsApplicationCompositionProvider.cs"
        )
        self.assertIn("unavailable-lifecycle-paused", conversation)
        self.assertIn("CancelAndDispose(cancellation)", conversation)
        self.assertIn("ReachyConversationState.Error", conversation)
        self.assertIn("ReachyInteractionState.Interrupted", ui)
        self.assertIn("ReachyInteractionState.Idle", ui)
        self.assertIn("ReachyInteractionState.Error", ui)
        self.assertGreaterEqual(
            production.count("IReachyApplicationInterruptionParticipant"), 2
        )
        self.assertIn("IReachyApplicationInterruptionParticipant", settings)

    def test_repeated_cycle_managed_contracts_are_registered(self) -> None:
        program = read("managed/ReachyMini.Core.Tests/Program.cs")
        tests = read(
            "managed/ReachyMini.Core.Tests/Rma182ApplicationInterruptionContractTests.cs"
        )
        speech = read(
            "managed/ReachyMini.SpeechAudioFocus.Tests/SpeechAudioFocusTests.cs"
        )
        vlm = read("managed/ReachyMini.VlmScheduling.Tests/Program.cs")
        self.assertIn("Rma182ApplicationInterruptionContractTests.RunAll();", program)
        self.assertIn("for (int cycle = 1; cycle <= 5; ++cycle)", tests)
        self.assertIn("repeat pause did not re-enter participants", tests)
        self.assertIn("repeat resume did not re-enter participants", tests)
        self.assertIn("application-background-cancels-releases-and-blocks", speech)
        self.assertIn("ApplicationInterruptionSuspendsAndCancelsRequests();", vlm)

    def test_roadmap_closes_every_rma182_item(self) -> None:
        roadmap = read("docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md")
        block = roadmap.split(
            "## RMA-182 — Harden pause/resume and interruption handling", 1
        )[1]
        block = block.split("## RMA-183", 1)[0]
        self.assertIn("**Status:** Complete (2026-08-15)", block)
        self.assertEqual(block.count("- [x]"), 6)
        self.assertNotIn("- [ ]", block)


if __name__ == "__main__":
    unittest.main()

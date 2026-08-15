import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma181PriorityDegradationContractTests(unittest.TestCase):
    def test_policy_encodes_required_preservation_order(self) -> None:
        policy = (CORE / "Performance/ReachyPriorityDegradationPolicy.cs").read_text()
        ordered_levels = (
            "Nominal = 0",
            "RenderReduced = 1",
            "CameraReduced = 2",
            "VlmSuspended = 3",
            "LlmReduced = 4",
            "Critical = 5",
        )
        positions = [policy.index(level) for level in ordered_levels]
        self.assertEqual(sorted(positions), positions)
        self.assertIn("trackingMaximumDimension: 480", policy)
        self.assertIn("NanosecondsPerSecond / 15L", policy)
        self.assertIn("vlmAllowed: false", policy)
        self.assertIn("LocalLlmGovernorMode.Minimal", policy)
        self.assertIn("LocalLlmGovernorMode.Suspended", policy)
        self.assertIn("RecoverySamplesRequired = 3", policy)

    def test_physics_and_audio_invariants_are_explicit(self) -> None:
        policy = (CORE / "Performance/ReachyPriorityDegradationPolicy.cs").read_text()
        self.assertIn(
            "ReachyMini.Core.ProjectMetadata.InitialPhysicsTimestepSeconds",
            policy,
        )
        self.assertIn("physicsStepSkippingAllowed = false", policy)
        self.assertIn("audioInteractionPreserved = true", policy)
        self.assertIn(
            "PhysicsStepSkippingAllowed => physicsStepSkippingAllowed",
            policy,
        )
        self.assertIn(
            "AudioInteractionPreserved => audioInteractionPreserved",
            policy,
        )

        rma181_files = [
            CORE / "Performance/ReachyPriorityDegradationPolicy.cs",
            CORE / "Application/ReachyPriorityDegradationRuntime.cs",
            APP / "ReachyUnityPriorityDegradationTarget.cs",
        ]
        combined = "\n".join(path.read_text() for path in rma181_files)
        self.assertNotIn("reachy_sim_step", combined)
        self.assertNotIn("InitialPhysicsTimestepSeconds =", combined)
        self.assertNotIn("skip physics", combined.lower())

    def test_real_subsystems_have_priority_degradation_hooks(self) -> None:
        llm = (CORE / "LocalModels/LocalLlmResourceGovernor.cs").read_text()
        vlm = (CORE / "Perception/ReachyVlmScheduler.cs").read_text()
        vlm_types = (CORE / "Perception/ReachyVlmSchedulingTypes.cs").read_text()
        tracking = (CORE / "Perception/ReachyLightweightTracking.cs").read_text()
        unity = (APP / "ReachyUnityPriorityDegradationTarget.cs").read_text()

        self.assertIn("IReachyPriorityDegradationTarget", llm)
        self.assertIn("PriorityDegradationPolicy", llm)
        self.assertIn("decision.MinimumLocalLlmMode", llm)

        self.assertIn("IReachyPriorityDegradationTarget", vlm)
        self.assertIn("MarkCancellationRequested", vlm)
        self.assertIn("VlmScheduleStatus.ResourceSuspended", vlm)
        self.assertIn("ResourceSuspended = 11", vlm_types)

        self.assertIn("IReachyPriorityDegradationTarget", tracking)
        self.assertIn("decision.TrackingMaximumDimension", tracking)
        self.assertIn("decision.TrackingMinimumIntervalNanoseconds", tracking)
        self.assertIn("stale tracking content was not reused", tracking)

        self.assertIn("Application.targetFrameRate = decision.TargetRenderFps", unity)
        self.assertIn("QualitySettings.shadows = ShadowQuality.Disable", unity)
        self.assertIn("QualitySettings.antiAliasing = 0", unity)
        self.assertIn("QualitySettings.softParticles = false", unity)

    def test_runtime_bridge_uses_existing_resource_and_physics_signals(self) -> None:
        runtime = (CORE / "Application/ReachyPriorityDegradationRuntime.cs").read_text()
        self.assertIn("ILocalLlmResourceSignalSource", runtime)
        self.assertIn("ILocalLlmPhysicsBudgetSource", runtime)
        self.assertIn("physicsBudget.Capture()", runtime)
        self.assertIn("resourceSignals.Capture(physics)", runtime)
        self.assertIn("ReachyPriorityDegradationSignals.FromResourceSnapshot", runtime)
        self.assertIn("coordinator.EvaluateAndApply(signals)", runtime)

    def test_managed_contracts_cover_llm_vlm_and_tracking(self) -> None:
        resource_tests = (ROOT / "managed/ReachyMini.ResourceGovernor.Tests/Program.cs").read_text()
        vlm_tests = (ROOT / "managed/ReachyMini.VlmScheduling.Tests/Program.cs").read_text()
        camera_tests = (
            ROOT / "managed/ReachyMini.Camera.Tests/Rma111LightweightTrackingContracts.cs"
        ).read_text()

        for marker in (
            "RMA-181 degradation order",
            "RMA-181 recovery hysteresis",
            "RMA-181 physics invariants",
            "RMA-181 local LLM floor",
        ):
            self.assertIn(marker, resource_tests)
        self.assertIn("PriorityDegradationSuspendsAndCancelsRequests", vlm_tests)
        self.assertIn(
            "PriorityDegradationReducesTrackingResolutionAndRateAsync",
            camera_tests,
        )
        self.assertIn("LastMaximumDimension", camera_tests)
        self.assertIn("StageInvocationCount", camera_tests)

    def test_roadmap_closes_all_rma181_requirements(self) -> None:
        todo = TODO.read_text()
        start = todo.index("## RMA-181 — Implement priority-based degradation policy")
        end = todo.index("## RMA-182", start)
        block = todo[start:end]
        self.assertIn("**Status:** Complete", block)
        self.assertNotIn("- [ ]", block)
        self.assertGreaterEqual(block.count("- [x]"), 5)
        self.assertIn("physics timestep", block.lower())
        self.assertIn("VLM", block)
        self.assertIn("LLM", block)


if __name__ == "__main__":
    unittest.main()

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Performance"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RUNTIME = ROOT / "Assets/ReachyMini/Runtime"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma180PerformanceHarnessContractTests(unittest.TestCase):
    def test_core_contract_covers_required_workloads_and_percentiles(self) -> None:
        contracts = (CORE / "ReachyPerformanceContracts.cs").read_text()
        telemetry = (CORE / "ReachyPerformanceTelemetry.cs").read_text()
        formatter = (CORE / "ReachyPerformanceReportJsonFormatter.cs").read_text()

        for workload in (
            "NativePhysics",
            "UnityRendering",
            "CameraAcquisition",
            "CameraWarp",
            "LightweightTracking",
            "LocalLlm",
            "Audio",
            "Network",
        ):
            self.assertIn(workload, contracts)

        for field in (
            "MedianMilliseconds",
            "P95Milliseconds",
            "P99Milliseconds",
            "MaximumMilliseconds",
            "PercentilesApproximate",
        ):
            self.assertIn(field, contracts)

        self.assertIn("PercentileReservoirCapacity = 4096", telemetry)
        self.assertIn("MaximumResourceSamples = 2048", telemetry)
        self.assertIn("targetFramesPerSecond != 30 && targetFramesPerSecond != 60", telemetry)
        self.assertIn('"median_ms"', formatter)
        self.assertIn('"p95_ms"', formatter)
        self.assertIn('"p99_ms"', formatter)
        self.assertIn('"max_ms"', formatter)

    def test_real_production_boundaries_emit_timing_samples(self) -> None:
        hooks = {
            "Simulation/ReachySimulationWorker.WorkerLoop.cs": (
                "ReachyPerformanceWorkload.NativePhysics"
            ),
            "Application/ReachyAndroidCameraAcquisition.cs": (
                "ReachyPerformanceWorkload.CameraAcquisition"
            ),
            "Rendering/ReachyCameraHomographyWarpPipeline.cs": (
                "ReachyPerformanceWorkload.CameraWarp"
            ),
            "Core/Perception/ReachyLightweightTracking.cs": (
                "ReachyPerformanceWorkload.LightweightTracking"
            ),
            "Core/LocalModels/ReachyLocalLlmProvider.Generation.cs": (
                "ReachyPerformanceWorkload.LocalLlm"
            ),
            "Core/Speech/AudioCoordinatedAsrProvider.cs": ("ReachyPerformanceWorkload.Audio"),
            "Core/Speech/AudioCoordinatedTtsProvider.cs": ("ReachyPerformanceWorkload.Audio"),
            "Core/Providers/ReachySharedHttpTransport.Core.cs": (
                "ReachyPerformanceWorkload.Network"
            ),
        }
        for relative, marker in hooks.items():
            text = (RUNTIME / relative).read_text()
            self.assertIn(marker, text, relative)
            self.assertIn("ReachyPerformanceTelemetry", text, relative)

    def test_unity_probe_records_render_memory_battery_and_thermal(self) -> None:
        probe = (APP / "ReachyPerformanceRuntimeProbe.cs").read_text()
        self.assertIn("ReachyPerformanceWorkload.UnityRendering", probe)
        self.assertIn("Time.unscaledDeltaTime", probe)
        self.assertIn("Profiler.GetTotalAllocatedMemoryLong", probe)
        self.assertIn("SystemInfo.batteryLevel", probe)
        self.assertIn("ReachyAndroidLocalLlmResourceSignalSource", probe)
        self.assertIn("LocalLlmPhysicsBudgetState.Unavailable", probe)
        self.assertIn("ResourceSampleIntervalSeconds = 10.0f", probe)

    def test_android_acceptance_runs_both_frame_profiles(self) -> None:
        acceptance = (APP / "ReachyRma180PerformanceAcceptance.cs").read_text()
        self.assertIn("CaptureProfileAsync(30", acceptance)
        self.assertIn("CaptureProfileAsync(60", acceptance)
        self.assertIn("DefaultProfileSeconds = 300", acceptance)
        self.assertIn("MaximumProfileSeconds = 3600", acceptance)
        self.assertIn("Application.targetFrameRate = targetFps", acceptance)
        self.assertIn("ReachyPerformanceReportJsonFormatter.Format(fps30)", acceptance)
        self.assertIn("ReachyPerformanceReportJsonFormatter.Format(fps60)", acceptance)
        self.assertIn("ReachyPerformanceWorkload.NativePhysics", acceptance)
        self.assertIn("ReachyPerformanceWorkload.UnityRendering", acceptance)
        runner = (ROOT / "scripts/run_rma180_performance_acceptance_android.sh").read_text()
        self.assertIn("reachy_rma180_performance_acceptance", runner)
        self.assertIn("reachy_rma180_profile_seconds", runner)
        self.assertIn("dumpsys battery", runner)
        self.assertIn("dumpsys meminfo", runner)
        self.assertIn("dumpsys thermalservice", runner)

    def test_managed_contract_is_registered(self) -> None:
        program = (ROOT / "managed/ReachyMini.Core.Tests/Program.cs").read_text()
        tests = (
            ROOT / "managed/ReachyMini.Core.Tests/Rma180PerformanceHarnessContractTests.cs"
        ).read_text()
        self.assertIn("Rma180PerformanceHarnessContractTests.RunAll();", program)
        self.assertIn("ExactPercentilesAndResourceSummaryAreReported", tests)
        self.assertIn("LongRunsRemainBounded", tests)
        self.assertIn("ThirtyAndSixtyFpsProfilesAreExplicit", tests)
        self.assertIn("InvalidAndPrivateSessionInputsFailClosed", tests)

    def test_roadmap_closes_all_rma180_requirements(self) -> None:
        todo = TODO.read_text()
        start = todo.index("## RMA-180 — Build performance harness")
        end = todo.index("## RMA-181", start)
        block = todo[start:end]
        self.assertIn("**Status:** Complete", block)
        self.assertNotIn("- [ ]", block)
        self.assertGreaterEqual(block.count("- [x]"), 5)


if __name__ == "__main__":
    unittest.main()

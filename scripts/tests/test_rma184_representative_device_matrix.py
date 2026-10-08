import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MATRIX = ROOT / "models/reachy-mini/android-device-matrix.json"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Performance/ReachyRepresentativeDeviceMatrix.cs"
PROBE = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma184RepresentativeDeviceProbe.cs"
RUNNER = ROOT / "scripts/run_rma184_device_probe_android.sh"
SPEC = ROOT / "docs/RMA_184_REPRESENTATIVE_DEVICE_MATRIX_SPEC_2026-08-15.md"
VALIDATION = ROOT / (
    "docs/validation/RMA_184_REPRESENTATIVE_DEVICE_MATRIX_LOCAL_VALIDATION_2026-08-15.md"
)
PROFILE_EVIDENCE = ROOT / "docs/validation/RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md"
PROGRAM = ROOT / "managed/ReachyMini.Core.Tests/Program.cs"
MANAGED = ROOT / ("managed/ReachyMini.Core.Tests/Rma184RepresentativeDeviceMatrixContractTests.cs")
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma184RepresentativeDeviceMatrixTests(unittest.TestCase):
    def test_machine_readable_matrix_validates(self) -> None:
        subprocess.run(
            [sys.executable, str(ROOT / "scripts/validate_rma184_device_matrix.py")],
            cwd=ROOT,
            check=True,
        )

    def test_matrix_covers_three_classes_and_required_device_fields(self) -> None:
        data = json.loads(MATRIX.read_text(encoding="utf-8"))
        classes = {entry["performance_class"] for entry in data["representative_devices"]}
        self.assertTrue({"low", "mid", "high"}.issubset(classes))
        for entry in data["representative_devices"]:
            for key in (
                "soc",
                "android_version",
                "ram_configuration_gib",
                "graphics_api",
                "camera_capability",
                "on_device_asr",
                "offline_tts",
                "measurement_status",
            ):
                self.assertIn(key, entry)

    def test_a54_speech_readiness_supersedes_old_probe_without_release_claim(self) -> None:
        data = json.loads(MATRIX.read_text(encoding="utf-8"))
        a54 = next(
            entry for entry in data["representative_devices"] if entry["id"] == "samsung-sm-a546e"
        )
        self.assertIn("rma125_a54_physical_service_revalidation", a54["on_device_asr"])
        self.assertIn("non_network_voice_available_per_rma125", a54["offline_tts"])
        self.assertIn(
            "docs/validation/RMA_125_A54_SPEECH_CAPABILITY_REVALIDATION_2026-09-30.md",
            a54["evidence"],
        )
        self.assertEqual(a54["support_status"], "supported_with_limitations")
        self.assertTrue(a54["measurement_status"].startswith("partial"))
        todo = TODO.read_text(encoding="utf-8")
        self.assertIn(
            "- [ ] On a device with installed services, the user can converse "
            "using Android on-device ASR and offline TTS with networking disabled.",
            todo,
        )
        spec = SPEC.read_text(encoding="utf-8")
        self.assertIn("fresh RMA-184 support-policy", spec)
        self.assertIn("1,800-second integrated long run remain unqualified", spec)

    def test_support_and_long_run_gates_are_fail_closed(self) -> None:
        source = CORE.read_text(encoding="utf-8")
        for token in (
            "ReachyDeviceSupportStatus.Unsupported",
            "ReachyDeviceSupportStatus.SupportedWithLimitations",
            "ReachyDeviceSupportStatus.Supported",
            "android_api_below_26",
            "graphics_api_not_vulkan_or_gles3",
            "explicit_on_device_asr_requires_api_31",
            "long_run_shorter_than_1800_seconds",
            "render_p95_exceeds_profile_budget",
            "physics_p95_exceeds_2ms_timestep",
            "unity_memory_growth_exceeds_profile_budget",
            "simulation_state_lag_accumulates",
            "local_llm_decode_below_1_token_per_second",
            "thermal_degradation_order_invalid",
        ):
            self.assertIn(token, source)

    def test_android_probe_records_required_runtime_metadata(self) -> None:
        probe = PROBE.read_text(encoding="utf-8")
        for token in (
            "reachy_rma184_device_probe",
            "Build$VERSION",
            'ReadBuildField("SOC_MODEL")',
            "SystemInfo.systemMemorySize",
            "SystemInfo.graphicsDeviceType",
            "SystemInfo.graphicsDeviceName",
            "ReachyAndroidCameraDiscovery",
            "ReachyAndroidOnDeviceAsrProviderFactory.Create",
            "ReachyAndroidOfflineTtsProviderFactory.Create",
            "ReachyRepresentativeDeviceSupportPolicy.Evaluate",
            "rma184-device-probe.json",
        ):
            self.assertIn(token, probe)
        self.assertNotIn("transcript", probe.lower())
        self.assertNotIn("prompt", probe.lower())

    def test_physical_runner_is_explicit_and_bounded(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn("set -euo pipefail", runner)
        self.assertIn("--ez reachy_rma184_device_probe true", runner)
        self.assertIn("android.permission.CAMERA", runner)
        self.assertIn("android.permission.RECORD_AUDIO", runner)
        self.assertIn("attempt < 90", runner)
        self.assertIn("rma184-device-probe.json", runner)

    def test_physical_lane_collects_non_gating_capability_evidence(self) -> None:
        workflow = (ROOT / ".github/workflows/local-unity-android-validation.yml").read_text(
            encoding="utf-8"
        )
        self.assertIn("bash scripts/run_rma184_device_probe_android.sh", workflow)
        self.assertIn("name: rma184-device-probe-${{ github.sha }}", workflow)
        self.assertIn("steps.rma184_device_probe.outcome == 'success'", workflow)
        self.assertIn(
            "RMA184_OUTPUT: ${{ runner.temp }}/rma184-device-probe/rma184-device-probe.json",
            workflow,
        )
        self.assertIn("Capture RMA-184 physical device capabilities (non-gating)", workflow)

    def test_managed_contract_is_registered(self) -> None:
        program = PROGRAM.read_text(encoding="utf-8")
        managed = MANAGED.read_text(encoding="utf-8")
        self.assertIn("Rma184RepresentativeDeviceMatrixContractTests.RunAll();", program)
        for token in (
            "PublishedProfilesAreDeterministic",
            "SupportPolicySeparatesCoreAndFullOfflineSupport",
            "LongRunQualificationAcceptsBoundedRepresentativeEvidence",
            "LongRunQualificationRejectsAccumulatingPressure",
        ):
            self.assertIn(token, managed)

    def test_measured_default_profiles_are_published_without_long_run_claim(self) -> None:
        data = json.loads(MATRIX.read_text(encoding="utf-8"))
        mid = next(
            entry for entry in data["default_profiles"] if entry["performance_class"] == "mid"
        )
        high = next(
            entry for entry in data["default_profiles"] if entry["performance_class"] == "high"
        )
        self.assertEqual(mid["measured_profile"]["run_id"], 36187273962)
        self.assertTrue(mid["measured_profile"]["representative_for_class"])
        self.assertEqual(high["measured_profile"]["run_id"], 36187273962)
        self.assertFalse(high["measured_profile"]["representative_for_class"])
        self.assertGreater(
            high["measured_profile"]["render_p95_ms"],
            high["maximum_render_p95_ms"],
        )
        evidence = PROFILE_EVIDENCE.read_text(encoding="utf-8")
        self.assertIn("run id: `36187273962`", evidence)
        self.assertIn("not close any representative-device long-run", evidence)

    def test_docs_do_not_claim_unrun_physical_qualification(self) -> None:
        spec = SPEC.read_text(encoding="utf-8")
        validation = VALIDATION.read_text(encoding="utf-8")
        self.assertIn("pending_measurement", spec)
        self.assertIn(
            "Physical-device characterization is intentionally not claimed",
            validation,
        )
        self.assertIn("RMA-181", spec)
        self.assertIn("1,800", spec)

    def test_roadmap_keeps_physical_qualification_open(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        start = todo.index("## RMA-184 — Representative-device matrix")
        end = todo.index("# Phase 20", start)
        block = todo[start:end]
        self.assertNotIn("**Status:** Complete", block)
        self.assertIn("- [x] Publish measured default profiles.", block)
        for gate in [
            "- [ ] Long-running tests do not accumulate unbounded memory or state lag.",
            "- [ ] Thermal degradation follows the documented priority order.",
            "- [ ] Supported devices meet the defined simulation and interaction targets.",
        ]:
            self.assertIn(gate, block)


if __name__ == "__main__":
    unittest.main()

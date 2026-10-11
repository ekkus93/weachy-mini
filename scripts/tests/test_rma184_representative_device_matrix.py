import json
import os
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
SOAK = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.Rma184.cs"
RMA135_ACCEPTANCE = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma135ResourceGovernorAcceptance.Acceptance.cs"
RMA135_RUNNER = ROOT / "scripts/run_rma135_resource_governor_acceptance_android.sh"
RMA135_WORKFLOW = ROOT / ".github/workflows/rma135-resource-governor-android.yml"
DEGRADATION_DRIVER = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyPriorityDegradationRuntimeDriver.cs"


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
            "degraded_render_budget_invalid",
            "MinimumAppliedRenderFramesPerSecond",
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


    def test_integrated_long_run_uses_production_paths_and_fail_closed_policy(self) -> None:
        soak = SOAK.read_text(encoding="utf-8")
        for token in (
            "ReachyRepresentativeDeviceProfile.MinimumLongRunSeconds",
            "ReachyPerformanceTelemetry.StartSession",
            "ReachyProductionAuthoritativeRuntime",
            "ReachyPriorityDegradationRuntimeDriver",
            "degradation.CurrentSignals",
            "successful_local_llm_generations",
            "minimum_local_llm_decode_tokens_per_second",
            "initial_state_lag_seconds",
            "final_state_lag_seconds",
            "thermal_degradation_order_valid",
            "minimum_applied_render_fps",
            "qualified_render_p95_budget_ms",
        ):
            self.assertIn(token, soak)
        # C# formatter may wrap qualified calls, enumerators and assignments
        # across lines. Assert the semantic tokens without coupling to layout.
        for pattern in (
            r"ReachyRepresentativeDeviceQualificationPolicy\s*\.\s*Evaluate\s*\(",
            r"thermalPressureSamples\s*>\s*0\s*&&\s*nonNominalDecisionSamples\s*>\s*0",
            r"LocalLlmGovernedGenerationStatus\s*\.\s*ResourceSuspendedBeforeStart",
            r"LocalLlmGovernedGenerationStatus\s*\.\s*ResourceCancelledDuringGeneration",
            r"report_contains_prompt_or_response_content\s*=\s*false",
        ):
            self.assertRegex(soak, pattern)
        self.assertNotIn("ReachySimSession.Create", soak)

        driver = DEGRADATION_DRIVER.read_text(encoding="utf-8")
        self.assertIn(
            "internal ReachyPriorityDegradationSignals? CurrentSignals",
            driver,
        )
        self.assertIn("priorityRuntime?.LastSignals", driver)

    def test_rma135_acceptance_invokes_optional_long_run_before_final_observation(self) -> None:
        source = RMA135_ACCEPTANCE.read_text(encoding="utf-8")
        call = source.index("RunRma184LongRunIfRequestedAsync")
        final_observation = source.index(
            "ReachySimulationTimingSnapshot finalWorker",
            call,
        )
        self.assertLess(call, final_observation)

    def test_long_run_runner_requires_real_duration_and_private_report(self) -> None:
        runner = RMA135_RUNNER.read_text(encoding="utf-8")
        for token in (
            "RMA184_LONG_RUN_SECONDS",
            "RMA184_LONG_RUN_SECONDS < 1800",
            "reachy_rma184_long_run_seconds",
            "reachy_rma184_performance_class",
            "rma184-representative-device-long-run.json",
            'report["measured_duration_seconds"] >= requested',
            'minimum_applied_fps = report["minimum_applied_render_fps"]',
            'report["render_p95_ms"] <= report["qualified_render_p95_budget_ms"]',
            'report["state_lag_growth_seconds"] <= 0.002',
            'report["minimum_local_llm_decode_tokens_per_second"] >= 1.0',
            'report["thermal_degradation_order_valid"] is True',
            'report["report_contains_prompt_or_response_content"] is False',
        ):
            self.assertIn(token, runner)

    def test_physical_verifier_checks_memory_lag_finite_and_private_content(
        self,
    ) -> None:
        runner = RMA135_RUNNER.read_text(encoding="utf-8")
        for contract in (
            'math.isfinite(report["measured_duration_seconds"])',
            'report["memory_growth_bytes"] == memory_growth',
            "memory_budgets_mib = [128, 192, 256]",
            'initial_lag = report["initial_state_lag_seconds"]',
            'final_lag = report["final_state_lag_seconds"]',
            'observed_lag_growth = max(0.0, final_lag - initial_lag)',
            'report["state_lag_growth_seconds"]',
            "math.isclose(",
            "observed_lag_growth <= 0.002",
            'math.isfinite(report["minimum_local_llm_decode_tokens_per_second"])',
            (
                'for forbidden in ("prompt", "transcript", "raw_audio", '
                '"response_json", "messages"):'
            ),
        ):
            self.assertIn(contract, runner)

    def test_optimized_python_cannot_disable_physical_report_assertions(self) -> None:
        runner = RMA135_RUNNER.read_text(encoding="utf-8")
        self.assertIn(
            'python3 -I - "${REPORT_DIR}/rma135-resource-governor-acceptance.json"',
            runner,
        )
        self.assertIn('python3 -I - "${RMA184_REPORT}"', runner)
        environment = dict(os.environ)
        environment["PYTHONOPTIMIZE"] = "1"
        result = subprocess.run(
            [sys.executable, "-I", "-c", "assert False, 'qualification must reject'"],
            env=environment,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("AssertionError", result.stderr)

    def test_integrated_soak_uses_physical_model_class_not_fixed_mid_class(self) -> None:
        matrix = json.loads(MATRIX.read_text(encoding="utf-8"))
        selector = ROOT / "scripts/rma184_select_performance_class.py"
        class_indices = {"low": "0", "mid": "1", "high": "2"}
        for device in matrix["representative_devices"]:
            result = subprocess.run(
                [
                    sys.executable,
                    str(selector),
                    "--matrix",
                    str(MATRIX),
                    "--model",
                    device["model"],
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertEqual(
                result.stdout.strip(),
                class_indices[device["performance_class"]],
            )
        unregistered = subprocess.run(
            [
                sys.executable,
                str(selector),
                "--matrix",
                str(MATRIX),
                "--model",
                "Unknown Device",
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(unregistered.returncode, 2)
        self.assertIn("expected exactly one", unregistered.stderr)

        runner = RMA135_RUNNER.read_text(encoding="utf-8")
        self.assertIn('RMA184_PERFORMANCE_CLASS="${RMA184_PERFORMANCE_CLASS:-auto}"', runner)
        self.assertIn("scripts/rma184_select_performance_class.py", runner)
        self.assertIn('RMA184_PERFORMANCE_CLASS="${expected_performance_class}"', runner)
        self.assertIn('"${RMA184_PERFORMANCE_CLASS}" != "${expected_performance_class}"', runner)
        self.assertIn("rma184_performance_class=%s", runner)
        workflow = RMA135_WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("RMA184_PERFORMANCE_CLASS: 'auto'", workflow)
        self.assertNotIn("RMA184_PERFORMANCE_CLASS: '1'", workflow)

    def test_self_hosted_matrix_contracts_run_before_physical_device_pin(self) -> None:
        gate = "python3 scripts/tests/test_rma184_representative_device_matrix.py"
        for name in (
            "local-unity-android-validation.yml",
            "local-unity-android-device-no-artifacts.yml",
        ):
            workflow = (ROOT / ".github/workflows" / name).read_text(
                encoding="utf-8"
            )
            self.assertIn(gate, workflow, name)
            self.assertLess(
                workflow.index(gate),
                workflow.index("- name: Pin one physical Android device"),
                name,
            )

    def test_api26_low_device_thermal_evidence_limit_is_fail_visible(self) -> None:
        matrix = json.loads(MATRIX.read_text(encoding="utf-8"))
        legacy_devices = [
            entry for entry in matrix["representative_devices"]
            if isinstance(entry.get("android_api"), int)
            and entry["android_api"] < 29
        ]
        self.assertTrue(legacy_devices, "Boundary-device compatibility must be explicit")
        source = (
            ROOT
            / "Assets/ReachyMini/Runtime/Application/ReachyAndroidLocalLlmResourceSignalSource.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("if (apiLevel < 29)", source)
        self.assertIn("return LocalLlmThermalStatus.Unavailable;", source)
        soak = SOAK.read_text(encoding="utf-8")
        self.assertRegex(
            soak,
            r"thermalPressureSamples\s*>\s*0\s*&&\s*nonNominalDecisionSamples\s*>\s*0",
        )
        spec = SPEC.read_text(encoding="utf-8")
        todo = TODO.read_text(encoding="utf-8")
        self.assertIn("API 26 thermal observability gap", spec)
        self.assertIn("cannot alone qualify", todo)
        self.assertIn("API 29+", todo)
        for acceptance in (
            "- [ ] Long-running tests do not accumulate unbounded memory or state lag.",
            "- [ ] Thermal degradation follows the documented priority order.",
            "- [ ] Supported devices meet the defined simulation and interaction targets.",
        ):
            self.assertIn(acceptance, todo)

    def test_integrated_physical_failure_reports_do_not_persist_private_provider_text(self) -> None:
        soak = SOAK.read_text(encoding="utf-8")
        parent = (
            ROOT / "Assets/ReachyMini/Runtime/Application"
            / "ReachyRma135ResourceGovernorAcceptance.cs"
        ).read_text(encoding="utf-8")
        self.assertNotIn("generation.Detail", soak)
        failure_writer = soak[soak.index("private static void TryWriteRma184Failure("):]
        self.assertNotIn("exception.Message", failure_writer)
        self.assertNotIn("writeException.Message", failure_writer)
        self.assertIn("exception.GetType().Name", failure_writer)
        self.assertIn('previous.status == "failed"', failure_writer)
        self.assertIn("!previous.qualification_passed", failure_writer)
        diagnostics = (
            ROOT / "Assets/ReachyMini/Runtime/Application"
            / "ReachyRma135ResourceGovernorAcceptance.Diagnostics.cs"
        ).read_text(encoding="utf-8")
        self.assertNotIn("unhandledFailureMessage", diagnostics)
        self.assertIn("Interlocked.Exchange(ref unhandledFailure, 1)", diagnostics)
        self.assertIn("Volatile.Read(ref unhandledFailure) != 0", parent)
        self.assertNotIn("unhandledFailureMessage", parent)
        self.assertNotIn('Bound(condition + "\\n" + stackTrace', diagnostics)
        checkpoint_failure = diagnostics[diagnostics.index("private static void TryWriteCheckpoint("):]
        self.assertNotIn("exception.Message", checkpoint_failure)
        self.assertIn("exception.GetType().Name", checkpoint_failure)
        report_handler = parent[parent.index("private async void Start()"):]
        self.assertNotIn("exception.ToString()", report_handler)
        self.assertNotIn("exception.Message", report_handler)
        self.assertNotIn("unhandledFailureMessage);", report_handler)
        self.assertIn("error = exception.GetType().Name,", report_handler)
        self.assertIn('TryWriteCheckpoint("failed", exception.GetType().Name)', report_handler)
        self.assertNotIn(" + exception);", report_handler)

    def test_physical_workflow_runs_probe_soak_and_survives_artifact_quota(self) -> None:
        workflow = RMA135_WORKFLOW.read_text(encoding="utf-8")
        for token in (
            "timeout-minutes: 75",
            "Acquire exact-SHA Unity Android APK",
            "Exact-SHA APK artifact unavailable",
            "build_unity_android.sh device-feasibility",
            "Capture fresh RMA-184 support-policy evidence",
            "run_rma184_device_probe_android.sh",
            "Run physical RMA-135 and RMA-184 long-run acceptance",
            "RMA184_LONG_RUN_SECONDS: '1800'",
            "RMA184_PERFORMANCE_CLASS: 'auto'",
            "RMA135_ACCEPTANCE_TIMEOUT_SECONDS: '3300'",
            "Upload RMA-135/RMA-184 physical evidence",
            "continue-on-error: true",
        ):
            self.assertIn(token, workflow)


if __name__ == "__main__":
    unittest.main()

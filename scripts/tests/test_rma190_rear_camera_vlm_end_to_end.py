#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
ACCEPTANCE = APP / "ReachyRma190RearCameraVlmEndToEndAcceptance.cs"
RUNNER = ROOT / "scripts/run_rma190_rear_camera_vlm_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-rear-camera-vlm-end-to-end.yml"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma190RearCameraVlmEndToEndContracts(unittest.TestCase):
    def test_acceptance_uses_shipping_rear_camera_and_production_vlm(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        for token in (
            "ReachyApplicationHostBehaviour",
            "ReachyAndroidPerceptionApplicationService",
            "ReachyAndroidCameraAcquisition",
            "ReachyAndroidCameraTextureBridge",
            "ReachyProductionAuthoritativeRuntime",
            "ReachyDeviceCameraFacing.Rear",
            "ReachyCameraFacing.Rear",
            "CaptureAcceptanceFrameAsync",
            "VisionFrameOrigin.TransformedReachyEye",
            "ReachyCloudVlmCredentialCoordinator",
            "ReachyProviderEndpointStyle.ChatCompletions",
            "perception.AnalyzeSceneAsync(",
            "production_vlm_transport_and_encoder = true",
            "synthetic_camera_frame_injected = false",
            "raw_camera_payload_persisted = false",
        ):
            self.assertIn(token, source)

    def test_fixture_is_explicitly_non_cloud_and_non_calibration_evidence(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        self.assertIn("ReachyCameraCalibrationProvenance", source)
        self.assertIn("deterministic_calibration_fixture = true", source)
        self.assertIn("measured_calibration_claimed = false", source)
        self.assertIn("external_vlm_fixture = true", source)
        self.assertIn("fixture_is_live_cloud_evidence = false", source)
        self.assertIn("not a measured device ", source)
        self.assertIn('"calibration."', source)

    def test_runner_uses_local_fixture_without_persisting_camera_payload(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        for token in (
            'reverse "tcp:${FIXTURE_PORT}" "tcp:${FIXTURE_PORT}"',
            "--ez reachy_rma190_rear_camera_vlm_e2e_acceptance true",
            "--es reachy_rma190_rear_camera_vlm_fixture_base_url",
            "data:image/jpeg;base64,",
            '"raw_request_persisted": False',
            '"authorization_value_persisted": False',
            'report["real_rear_camera"] is True',
            'report["production_vlm_transport_and_encoder"] is True',
            'report["fixture_is_live_cloud_evidence"] is False',
        ):
            self.assertIn(token, runner)
        self.assertNotIn('"request_body":', runner)
        self.assertNotIn('"authorization_value":', runner)

    def test_workflow_requires_physical_android_runner(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn(
            "name: RMA-190 Rear Camera VLM End-to-End Android Acceptance",
            workflow,
        )
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn(
            "run_rma190_rear_camera_vlm_end_to_end_android.sh",
            workflow,
        )
        self.assertIn(
            "rma190-rear-camera-vlm-end-to-end-${{ github.sha }}",
            workflow,
        )

    def test_resource_pressure_keeps_production_vlm_fail_closed(self) -> None:
        cloud_path = APP / "ReachyAndroidPerceptionApplicationService.CloudVlm.cs"
        cloud = cloud_path.read_text()
        driver = (APP / "ReachyPriorityDegradationRuntimeDriver.cs").read_text()
        self.assertIn("!decision.VlmAllowed", cloud)
        self.assertIn("priorityRuntime.LastSignals", driver)
        self.assertIn("sampled.AvailableMemoryBytes", driver)
        self.assertIn("sampled.PhysicsBudgetState", driver)

    def test_failed_physical_run_surfaces_bounded_resource_signals(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn('resource-transitions.txt', runner)
        self.assertIn('RMA-181 sampled resource transition:', runner)
        self.assertIn('transitions[-24:]', runner)
        self.assertIn('tail -n 24', runner)
        self.assertIn('if (( exit_code != 0 )); then', runner)

    def test_acceptance_waits_for_shipping_vlm_admission_before_fresh_capture(self) -> None:
        source = ACCEPTANCE.read_text(encoding="utf-8")
        for token in (
            "ReachyPriorityDegradationRuntimeDriver",
            "WaitForStableVlmAdmissionAsync",
            "AdmissionObservationMilliseconds = 600",
            "AdmissionTimeoutMilliseconds = 15_000",
            "ReachyPriorityDegradationPolicy.RecoverySamplesRequired",
            "driver.CurrentDecision",
            "ReferenceEquals(decision, previousDecision)",
            "decision.VlmAllowed && physicsSignalAvailable",
            "PhysicsSignalUnavailable",
            "stable_vlm_admission_samples",
            "vlm_admission_diagnostic",
        ):
            self.assertIn(token, source)
        wait_stage = source.index('SetStage("wait-production-vlm-admission")')
        capture_stage = source.index('SetStage("capture-real-rear-transformed-frame")')
        vlm_call = source.index("perception.AnalyzeSceneAsync(")
        self.assertLess(wait_stage, capture_stage)
        self.assertLess(capture_stage, vlm_call)
        self.assertNotIn("priorityDegradation.CurrentDecision =", source)

    def test_todo_stays_open_until_physical_run_passes(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        self.assertIn(
            "- [ ] Rear-camera visual question with optional VLM.",
            todo,
        )


if __name__ == "__main__":
    unittest.main()

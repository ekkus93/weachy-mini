#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RUNNER = ROOT / "scripts/run_rma190_rear_visual_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-rear-visual-end-to-end.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190RearVisualEndToEndContracts(unittest.TestCase):
    def test_acceptance_uses_real_rear_camera_and_shipping_perception(self) -> None:
        source = read(APP / "ReachyRma190RearVisualEndToEndAcceptance.cs")
        self.assertIn("ReachyApplicationHostBehaviour", source)
        self.assertIn("ReachyAndroidCameraAcquisition", source)
        self.assertIn("ReachyAndroidCameraTextureBridge", source)
        self.assertIn("ReachyAndroidPerceptionApplicationService", source)
        self.assertIn("ReachyDeviceCameraFacing.Rear", source)
        self.assertIn("ReachyCameraFacing.Rear", source)
        self.assertIn("VisionFrameOrigin.TransformedReachyEye", source)
        self.assertIn("VisionResourceKind.ValidityMask", source)
        self.assertIn("synthetic_camera_frame_injected = false", source)
        self.assertIn("string deviceModel = SystemInfo.deviceModel;", source)
        self.assertIn("int androidApiLevel = ReadApiLevel();", source)
        success_body = source[
            source.index("public static Report Success("):
            source.index("public static Report Failure(")
        ]
        self.assertIn("device_model = deviceModel", success_body)
        self.assertNotIn("device_model = SystemInfo.deviceModel", success_body)

    def test_visual_question_uses_optional_shipping_vlm_capability(self) -> None:
        source = read(APP / "ReachyRma190RearVisualEndToEndAcceptance.cs")
        self.assertIn("ICloudVlmProviderCapability", source)
        self.assertIn("AnalyzeSceneAsync(", source)
        self.assertIn("What is visible in this rear-camera scene?", source)
        self.assertIn("VisionOperationStatus.Succeeded", source)
        self.assertIn("VisionOperationStatus.Unavailable", source)
        self.assertIn("vlm_optional_unavailable", source)
        self.assertIn("cloud_credentials_injected = false", source)
        self.assertNotIn("new ReachyOpenAiVision", source)

    def test_rear_fixture_reuses_front_acceptance_calibration_math(self) -> None:
        helpers = read(APP / "ReachyRma190CameraEndToEndAcceptance.Helpers.cs")
        source = read(APP / "ReachyRma190RearVisualEndToEndAcceptance.cs")
        self.assertIn(
            "internal static ReachyCameraCalibrationProfile CreateDeterministicFixture",
            helpers,
        )
        self.assertIn("ReachyCameraCalibrationProvenance.UncalibratedEstimate", helpers)
        self.assertIn("mirrorHorizontally: false", source)
        self.assertIn("measured_calibration_claimed = false", source)

    def test_runner_is_fail_closed(self) -> None:
        runner = read(RUNNER)
        self.assertIn('pm grant "${PACKAGE_NAME}" android.permission.CAMERA', runner)
        self.assertIn("--ez reachy_rma190_rear_visual_e2e_acceptance true", runner)
        self.assertIn('report["status"] == "passed"', runner)
        self.assertIn('report["real_rear_camera"] is True', runner)
        self.assertIn('report["transformed_reachy_eye_frame"] is True', runner)
        self.assertIn('report["validity_mask_present"] is True', runner)
        self.assertIn('report["vlm_status"] in ("Succeeded", "Unavailable")', runner)
        self.assertIn('report["synthetic_camera_frame_injected"] is False', runner)
        self.assertIn('report["cloud_credentials_injected"] is False', runner)

    def test_workflow_uses_physical_device_without_artifact_quota_gate(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn(
            "name: RMA-190 Rear Camera Visual End-to-End Android Acceptance",
            workflow,
        )
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn("run_rma190_rear_visual_end_to_end_android.sh", workflow)
        self.assertNotIn("actions/upload-artifact", workflow)


if __name__ == "__main__":
    unittest.main()

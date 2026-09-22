#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RUNNER = ROOT / "scripts/run_rma190_camera_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-camera-end-to-end.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190CameraEndToEndContracts(unittest.TestCase):
    def test_acceptance_uses_real_shipping_camera_and_authoritative_motion(self) -> None:
        source = "\n".join(
            read(path)
            for path in sorted(APP.glob("ReachyRma190CameraEndToEndAcceptance*.cs"))
        )
        self.assertIn("ReachyApplicationHostBehaviour", source)
        self.assertIn("ReachyAndroidCameraAcquisition", source)
        self.assertIn("ReachyAndroidCameraTextureBridge", source)
        self.assertIn("ReachyProductionAuthoritativeRuntime", source)
        self.assertIn("ReachyBaselineBehaviorRequest.Curiosity()", source)
        self.assertIn("ReachyProductionBehaviorControllerTargetSink(runtime)", source)
        self.assertIn("ReachyDeviceCameraFacing.Front", source)
        self.assertIn("VisionFrameOrigin.TransformedReachyEye", source)
        self.assertIn("synthetic_camera_frame_injected = false", source)
        self.assertNotIn("ReachyRma154VisualServoAcceptance", source)

    def test_acceptance_fixture_is_explicitly_uncalibrated_and_restored(self) -> None:
        source = "\n".join(
            read(path)
            for path in sorted(APP.glob("ReachyRma190CameraEndToEndAcceptance*.cs"))
        )
        self.assertIn("ReachyCameraCalibrationProvenance.UncalibratedEstimate", source)
        self.assertIn("deterministic_calibration_fixture = true", source)
        self.assertIn("measured_calibration_claimed = false", source)
        self.assertIn("CopyProfiles", source)
        self.assertIn("originalProfiles", source)
        self.assertIn("State.ReplaceAll(", source)

    def test_validation_capture_reuses_production_try_build_frame(self) -> None:
        driver = read(APP / "ReachyAndroidPerceptionDriver.cs")
        source = "\n".join(
            read(path)
            for path in sorted(APP.glob("ReachyRma190CameraEndToEndAcceptance*.cs"))
        )
        self.assertIn("CaptureFrameForAcceptanceAsync", driver)
        self.assertIn("return CaptureFrameAsync(", driver)
        self.assertIn("FrameBuildResult built = TryBuildFrame(bridge);", driver)
        self.assertIn("CaptureFrameForAcceptanceAsync", source)
        self.assertNotIn("new ReachyCameraHomographyWarpPipeline", source)
        self.assertNotIn("ReachyCameraHomographyCalculator.Build", source)

    def test_runner_is_fail_closed_and_grants_only_camera_permission(self) -> None:
        runner = read(RUNNER)
        self.assertIn("pm grant \"${PACKAGE_NAME}\" android.permission.CAMERA", runner)
        self.assertIn("--ez reachy_rma190_camera_e2e_acceptance true", runner)
        self.assertIn('report["status"] == "passed"', runner)
        self.assertIn('report["real_front_camera"] is True', runner)
        self.assertIn('report["real_authoritative_head_motion"] is True', runner)
        self.assertIn('report["deterministic_calibration_fixture"] is True', runner)
        self.assertIn('report["measured_calibration_claimed"] is False', runner)
        self.assertIn('report["synthetic_camera_frame_injected"] is False', runner)
        self.assertIn("report JSON validation failed", runner)

    def test_workflow_uses_physical_android_runner(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("name: RMA-190 Front Camera End-to-End Android Acceptance", workflow)
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn("run_rma190_camera_end_to_end_android.sh", workflow)
        self.assertIn("rma190-camera-end-to-end-report-${{ github.sha }}", workflow)


if __name__ == "__main__":
    unittest.main()

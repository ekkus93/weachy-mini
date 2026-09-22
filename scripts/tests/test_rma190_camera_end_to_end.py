#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
RUNNER = ROOT / "scripts/run_rma190_camera_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-core-end-to-end.yml"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190CameraEndToEndContracts(unittest.TestCase):
    def test_harness_uses_real_front_camera_and_shipping_perception(self) -> None:
        source = "\n".join(
            read(path) for path in sorted(APP.glob("ReachyRma190CameraEndToEndAcceptance*.cs"))
        )
        self.assertIn("StartPreferred(ReachyCameraFacing.Front)", source)
        self.assertIn("ReachyAndroidCameraTextureBridge", source)
        self.assertIn("ReachyAndroidPerceptionDriver", source)
        self.assertIn("CaptureFrameForAnalysisAsync", source)
        self.assertIn("VisionFrameOrigin.TransformedReachyEye", source)
        self.assertIn("ReachyProductionBehaviorControllerTargetSink(runtime)", source)
        self.assertIn("ReachyBaselineBehaviorRequest.Curiosity()", source)
        self.assertIn("CanonicalCameraBodyId", source)
        self.assertIn("deterministic_uncalibrated_fixture_used = true", source)
        self.assertIn("ReachyCameraCalibrationProvenance.UncalibratedEstimate", source)
        self.assertIn("synthetic_camera_frame_injected = false", source)
        self.assertIn("raw_camera_frame_injected = false", source)

    def test_runner_grants_camera_and_validates_transformed_post_motion_frame(
        self,
    ) -> None:
        runner = read(RUNNER)
        self.assertIn("android.permission.CAMERA", runner)
        self.assertIn("--ez reachy_rma190_camera_e2e_acceptance true", runner)
        self.assertIn('report["camera_facing"] == "Front"', runner)
        self.assertIn('report["front_camera_mirrored"] is True', runner)
        self.assertIn(
            'report["baseline_frame_origin"] == "TransformedReachyEye"', runner
        )
        self.assertIn(
            'report["post_motion_frame_origin"] == "TransformedReachyEye"', runner
        )
        self.assertIn('report["authoritative_head_rotation_observed"] is True', runner)
        self.assertIn(
            'report["transformed_frame_after_rotation_observed"] is True', runner
        )
        self.assertIn('report["synthetic_camera_frame_injected"] is False', runner)

    def test_existing_rma190_physical_workflow_runs_camera_scenario(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("run_rma190_camera_end_to_end_android.sh", workflow)
        self.assertIn("rma190-camera-end-to-end-report-${{ github.sha }}", workflow)


if __name__ == "__main__":
    unittest.main()

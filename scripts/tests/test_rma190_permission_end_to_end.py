#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts" / "run_rma190_permission_end_to_end_android.sh"
WORKFLOW = ROOT / ".github" / "workflows" / "rma190-permission-end-to-end.yml"
RMA090 = ROOT / "scripts" / "run_rma090_camera_discovery_acceptance_android.sh"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190PermissionEndToEndContracts(unittest.TestCase):
    def test_runner_executes_fresh_physical_permission_cycle(self) -> None:
        runner = read(RUNNER)
        self.assertIn("run_rma090_camera_discovery_acceptance_android.sh", runner)
        self.assertIn("RMA090_CAMERA_REPORT_DIR", runner)
        self.assertIn("UNITY_DEVICE_APK_PATH", runner)
        self.assertIn("rma090-camera-discovery.json", runner)
        self.assertIn("android.permission.CAMERA", runner)
        self.assertIn("initial_access_denied_by_absent_permission", runner)
        self.assertIn("revoked_state_observed", runner)
        self.assertIn('"user_clicked_deny": False', runner)
        self.assertIn('"fresh_run": True', runner)

    def test_underlying_acceptance_crosses_android_and_application_state(self) -> None:
        source = read(RMA090)
        for token in (
            "pm revoke",
            "pm grant",
            "wait_for_permission_state",
            "NotRequested",
            "Granted",
            "Revoked",
            "rma090-camera-discovery-state.json",
            "camera_count",
        ):
            self.assertIn(token, source)

    def test_workflow_builds_shipping_apk_and_avoids_artifact_quota_dependency(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("self-hosted", workflow)
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn("run_rma190_permission_end_to_end_android.sh", workflow)
        self.assertIn("test_rma190_permission_end_to_end.py", workflow)
        self.assertNotIn("actions/upload-artifact", workflow)


if __name__ == "__main__":
    unittest.main()

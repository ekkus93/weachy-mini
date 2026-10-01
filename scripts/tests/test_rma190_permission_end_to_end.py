#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyCameraAcquisitionEvidence.cs"
RUNNER = ROOT / "scripts/run_rma190_permission_end_to_end_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma190-permission-end-to-end.yml"


def read(path):
    return path.read_text(encoding="utf-8")


class Rma190PermissionEndToEndContracts(unittest.TestCase):
    def test_evidence_invokes_shipping_permission_discovery(self):
        source = read(EVIDENCE)
        self.assertIn("reachy_rma190_permission_e2e_acceptance", source)
        self.assertIn('case "request_permission":', source)
        self.assertIn(".RequestAccessOrRefresh();", source)
        self.assertIn("PermissionRequestCount", source)
        self.assertNotIn("MarkDenied(", source)
        self.assertNotIn("MarkRevoked(", source)

    def test_runner_denies_real_android_dialog_then_proves_revocation(self):
        runner = read(RUNNER)
        self.assertIn("uiautomator dump", runner)
        self.assertIn("permission_deny_button", runner)
        self.assertIn("write_command request-deny request_permission", runner)
        self.assertIn("wait_for_report 'real Android denial callback'", runner)
        self.assertIn("pm grant \"${PACKAGE_NAME}\" android.permission.CAMERA", runner)
        self.assertIn("pm revoke \"${PACKAGE_NAME}\" android.permission.CAMERA", runner)
        self.assertIn("denied_fail_closed", runner)
        self.assertIn("revoked_fail_closed", runner)
        self.assertIn("real CameraX frames before revocation", runner)
        self.assertIn('"synthetic_permission_state_injected":False', runner)
        self.assertIn('"synthetic_camera_frame_injected":False', runner)

    def test_workflow_is_physical_and_avoids_artifact_quota_dependency(self):
        workflow = read(WORKFLOW)
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("build_unity_android.sh device-feasibility", workflow)
        self.assertIn("run_rma190_permission_end_to_end_android.sh", workflow)
        self.assertNotIn("actions/upload-artifact", workflow)


if __name__ == "__main__":
    unittest.main()

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APPLICATION = ROOT / "Assets/ReachyMini/Runtime/Application"
SCRIPT = ROOT / "scripts/run_rma161_credential_acceptance_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma161-credential-lifecycle.yml"


class Rma161CredentialPhysicalAcceptanceTests(unittest.TestCase):
    def test_android_acceptance_has_explicit_lifecycle_phases(self) -> None:
        source = (APPLICATION / "ReachyRma161CredentialAcceptance.cs").read_text(encoding="utf-8")
        for contract in (
            '"prepare"',
            '"verify-after-lock"',
            '"invalidate"',
            '"verify-cleared"',
            "CreateCredential",
            "UpdateCredential",
            "ReadCredential",
            "DeleteCredential",
            "RemoveProvider",
            "InvalidateEncryptionKeyForAcceptance",
            "RMA161_KEY_UNAVAILABLE",
            "app_data_clear_removed_credential",
            "full_secret_in_report = false",
        ):
            self.assertIn(contract, source)
        self.assertNotIn("Debug.Log(InitialCredential", source)
        self.assertNotIn("Debug.Log(UpdatedCredential", source)
        self.assertNotIn("message = exception.Message", source)

    def test_device_script_proves_lock_clear_and_text_evidence_privacy(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        for contract in (
            "wait_for_keyguard_showing",
            "wait_for_keyguard_dismissed",
            "input keyevent 223",
            "run_phase verify-after-lock",
            "run_phase invalidate",
            'shell pm clear "${PACKAGE_NAME}"',
            "run_phase verify-cleared",
            "assert_no_full_secret_in_text_evidence",
            "uiautomator dump",
            "screencap -p",
            "run-as",
        ):
            self.assertIn(contract, source)
        self.assertNotIn("set +e\nassert_no_full_secret", source)

    def test_physical_workflow_reuses_successful_exact_sha_apk(self) -> None:
        source = WORKFLOW.read_text(encoding="utf-8")
        for contract in (
            "workflow_run:",
            "Local Unity Android Validation",
            "github.event.workflow_run.conclusion == 'success'",
            "github.event.workflow_run.head_sha",
            "actions/download-artifact@v4",
            "local-unity-device-apk-${{ github.event.workflow_run.head_sha }}",
            "run_rma161_credential_acceptance_android.sh",
            "rma161-credential-report-${{ github.event.workflow_run.head_sha }}",
        ):
            self.assertIn(contract, source)
        self.assertNotIn("continue-on-error", source)


if __name__ == "__main__":
    unittest.main()

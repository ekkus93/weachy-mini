import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APPLICATION = ROOT / "Assets/ReachyMini/Runtime/Application"
SCRIPT = ROOT / "scripts/run_rma161_credential_acceptance_android.sh"
FOREGROUND_HELPER = ROOT / "scripts/android_device_acceptance_foreground.sh"
AUTHORITATIVE_WRAPPER = ROOT / "scripts/run_unity_authoritative_rendering_acceptance_android.sh"
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

    def test_device_script_proves_locked_access_clear_and_text_evidence_privacy(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        for contract in (
            "wait_for_keyguard_showing",
            "require_unoccluded_keyguard",
            "run_phase_while_keyguard_showing",
            "input keyevent 223",
            "run_phase_while_keyguard_showing verify-after-lock",
            "run_phase_while_keyguard_showing invalidate",
            'shell pm clear "${PACKAGE_NAME}"',
            "run_phase_while_keyguard_showing verify-cleared",
            "assert_no_full_secret_in_text_evidence",
            "uiautomator dump",
            "screencap -p",
            "run-as",
            "mKeyguardShowing=true",
            "mOccluded=true",
        ):
            self.assertIn(contract, source)
        self.assertNotIn("wait_for_keyguard_dismissed", source)
        self.assertNotIn("unlock-prepare.txt", source)
        self.assertNotIn("input text", source)
        self.assertNotIn("set +e\nassert_no_full_secret", source)

    def test_device_script_reuses_only_matching_installed_upstream_apk(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        for contract in (
            "verify_installed_apk_matches_artifact",
            'shell pm path "${PACKAGE_NAME}"',
            'exec-out cat "${base_apk_path}"',
            'sha256sum "${APK_PATH}"',
            "Installed APK does not match the exact upstream validated artifact.",
            "installed-apk-provenance.txt",
            "reinstall_skipped=true",
        ):
            self.assertIn(contract, source)
        self.assertNotIn('install -r -g "${APK_PATH}"', source)

    def test_foreground_prepare_fails_closed_and_restore_parks_runner_awake(self) -> None:
        source = FOREGROUND_HELPER.read_text(encoding="utf-8")
        self.assertIn("Could not prepare an awake device", source)
        self.assertIn("PIN/pattern/password keyguard cannot be bypassed", source)
        prepare_body = source.split("prepare_device()", 1)[1].split("case ", 1)[0]
        self.assertIn("return 1", prepare_body)
        restore_body = source.split("restore)", 1)[1].split(";;", 1)[0]
        self.assertIn("svc power stayon true", restore_body)
        self.assertIn("input keyevent 224", restore_body)
        self.assertNotIn("svc power stayon false", restore_body)

    def test_routine_authoritative_wrapper_does_not_run_rma161(self) -> None:
        source = AUTHORITATIVE_WRAPPER.read_text(encoding="utf-8")
        self.assertIn('wait "${implementation_pid}"', source)
        self.assertNotIn("RMA161_SCRIPT=", source)
        self.assertNotIn("RMA161_REPORT_DIR=", source)
        self.assertNotIn("RMA161_CREDENTIAL_REPORT_DIR=", source)
        self.assertNotIn("run_rma161_credential_acceptance_android.sh", source)

    def test_rma161_workflow_is_manual_and_pinned_to_exact_validated_apk(self) -> None:
        source = WORKFLOW.read_text(encoding="utf-8")
        for contract in (
            "workflow_dispatch:",
            "validated_sha:",
            "validated_run_id:",
            "inputs.validated_sha",
            "inputs.validated_run_id",
            "weachy-mini-android-device",
            "actions/download-artifact@v4",
            "local-unity-device-apk-${{ inputs.validated_sha }}",
            "run-id: ${{ inputs.validated_run_id }}",
            'test "$(git rev-parse HEAD)" = "${{ inputs.validated_sha }}"',
            "run_rma161_credential_acceptance_android.sh",
            "rma161-credential-report-${{ inputs.validated_sha }}",
        ):
            self.assertIn(contract, source)
        self.assertNotIn("workflow_run:", source)
        self.assertNotIn("continue-on-error", source)


if __name__ == "__main__":
    unittest.main()

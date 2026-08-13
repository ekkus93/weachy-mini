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

    def test_foreground_prepare_fails_closed_when_secure_keyguard_remains(self) -> None:
        source = FOREGROUND_HELPER.read_text(encoding="utf-8")
        self.assertIn("Could not prepare an awake device", source)
        self.assertIn("PIN/pattern/password keyguard cannot be bypassed", source)
        prepare_body = source.split("prepare_device()", 1)[1].split("case ", 1)[0]
        self.assertIn("return 1", prepare_body)

    def test_authoritative_wrapper_runs_rma161_before_device_release(self) -> None:
        source = AUTHORITATIVE_WRAPPER.read_text(encoding="utf-8")
        for contract in (
            "RMA161_SCRIPT=",
            "RMA161_REPORT_DIR=",
            "RMA161_CREDENTIAL_REPORT_DIR=",
            'bash "${RMA161_SCRIPT}"',
            'wait "${implementation_pid}"',
            'implementation_pid=""',
        ):
            self.assertIn(contract, source)
        trap_install = source.index("trap on_exit EXIT")
        authoritative_wait = source.rindex('wait "${implementation_pid}"')
        rma161_call = source.index('bash "${RMA161_SCRIPT}"')
        self.assertLess(trap_install, authoritative_wait)
        self.assertLess(authoritative_wait, rma161_call)

    def test_child_workflow_verifies_exact_parent_evidence_without_device_reacquire(self) -> None:
        source = WORKFLOW.read_text(encoding="utf-8")
        for contract in (
            "workflow_run:",
            "Local Unity Android Validation",
            "github.event.workflow_run.conclusion == 'success'",
            "github.event.workflow_run.head_sha",
            "runs-on: ubuntu-latest",
            "actions/download-artifact@v4",
            "unity-authoritative-device-report-${{ github.event.workflow_run.head_sha }}",
            "run-id: ${{ github.event.workflow_run.id }}",
            'report_dir="parent-evidence/rma161-credential-report"',
            'test -s "${report_dir}/prepare.json"',
            'test -s "${report_dir}/verify-after-lock.json"',
            'test -s "${report_dir}/invalidate.json"',
            'test -s "${report_dir}/verify-cleared.json"',
            'test -s "${report_dir}/installed-apk-provenance.txt"',
        ):
            self.assertIn(contract, source)
        self.assertNotIn("weachy-mini-android-device", source)
        self.assertNotIn("run_rma161_credential_acceptance_android.sh", source)
        self.assertNotIn("continue-on-error", source)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
from __future__ import annotations

import os
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
WORKFLOW = ROOT / ".github/workflows/rma190-offline-speech-end-to-end.yml"
RUNNER = ROOT / "scripts/run_rma190_offline_speech_end_to_end_android.sh"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190OfflineSpeechEndToEndContracts(unittest.TestCase):
    def test_harness_uses_shipping_path_without_speech_injection(self) -> None:
        source = read(APP / "ReachyRma190OfflineSpeechEndToEndAcceptance.cs")
        self.assertIn("FindAnyObjectByType<ReachyApplicationHostBehaviour>", source)
        self.assertIn("FindAnyObjectByType<ReachyMainScreen>", source)
        self.assertIn("LocalLlmBehaviorContract.CreateSelectedManifest()", source)
        self.assertIn("new LocalModelPackageManager(", source)
        self.assertIn("settings.CycleProvider(kind)", source)
        self.assertIn("screen.RequestMicrophone();", source)
        self.assertIn("provider.ProviderSnapshot", source)
        self.assertIn("providerSnapshot.ExecutionState", source)
        self.assertIn("providerSnapshot.ActiveModelId", source)
        self.assertNotIn("providerSnapshot.State", source)
        self.assertNotIn("providerSnapshot.ModelId", source)
        self.assertIn("behavior.Snapshot.Revision", source)
        self.assertIn("human_utterance_required = true", source)
        self.assertIn("synthetic_speech_injected = false", source)
        self.assertIn("transcript_persisted = false", source)
        self.assertIn("raw_audio_persisted = false", source)
        self.assertIn("cloud_credentials_injected = false", source)
        self.assertNotIn("new AsrEvent(", source)
        self.assertNotIn("Fake", source)

    def test_runner_proves_network_disabled_and_restores_state(self) -> None:
        runner = read(RUNNER)
        self.assertIn("svc wifi disable", runner)
        self.assertIn("svc data disable", runner)
        self.assertIn("cmd connectivity airplane-mode enable", runner)
        self.assertIn("settings get global airplane_mode_on", runner)
        self.assertIn("restore_network", runner)
        self.assertIn("PHYSICAL ACTION REQUIRED", runner)
        self.assertIn("Do not play a recording or inject a transcript", runner)
        self.assertIn("--ez reachy_rma190_offline_speech_e2e_acceptance true", runner)
        self.assertIn('"transcript_persisted"', runner)
        self.assertIn('"raw_audio_persisted"', runner)

    def test_pinned_serial_cannot_bypass_physical_device_eligibility(self) -> None:
        runner = read(RUNNER)
        self.assertIn('DEVICE_SERIAL="$(select_device_serial)"', runner)
        self.assertNotIn('REACHY_ANDROID_SERIAL:-$(select_device_serial)', runner)
        self.assertIn('local pinned="${REACHY_ANDROID_SERIAL:-}"', runner)
        self.assertIn('if [[ "${serial}" == "${pinned}" ]]', runner)
        self.assertIn('"${sdk}" =~ ^[0-9]+$', runner)
        self.assertIn('(( sdk >= 31 ))', runner)
        self.assertIn('"${abi}" == "arm64-v8a"', runner)
        self.assertIn('$1 !~ /^emulator-/', runner)

    def test_real_selector_rejects_ineligible_or_ambiguous_fake_adb_devices(self) -> None:
        # Fake ADB is a deterministic selector fixture, never physical evidence.
        runner = read(RUNNER)
        beginning = runner.index("select_device_serial()\n{")
        ending = (
            runner.index(
                '\n}\n\nDEVICE_SERIAL="$(select_device_serial)"',
                beginning,
            )
            + 2
        )
        command = (
            "set -euo pipefail\n"
            + runner[beginning:ending]
            + "\nselect_device_serial\n"
        )
        stub = r"""#!/usr/bin/env bash
if [[ "${1:-}" == "devices" ]]; then
    printf 'List of devices attached\n'
    printf 'good-one\tdevice\n'
    printf 'good-two\tdevice\n'
    printf 'old-api\tdevice\n'
    printf 'wrong-abi\tdevice\n'
    printf 'emulator-5554\tdevice\n'
    printf 'unauthorized\tunauthorized\n'
    exit 0
fi
if [[ "${1:-}" == "-s" && "${3:-}" == "shell" && "${4:-}" == "getprop" ]]; then
    case "${5:-}" in
        ro.product.cpu.abi)
            [[ "${2:-}" == "wrong-abi" ]] && echo armeabi-v7a || echo arm64-v8a
            exit 0
            ;;
        ro.build.version.sdk)
            [[ "${2:-}" == "old-api" ]] && echo 30 || echo 36
            exit 0
            ;;
    esac
fi
exit 3
"""
        with tempfile.TemporaryDirectory() as directory:
            fake_adb = Path(directory) / "fixture-adb"
            fake_adb.write_text(stub, encoding="utf-8")
            fake_adb.chmod(0o755)
            base_env = os.environ.copy()
            base_env.pop("REACHY_ANDROID_SERIAL", None)
            base_env["ADB_BIN"] = str(fake_adb)
            for pinned in ("good-one", "good-two"):
                with self.subTest(pinned=pinned):
                    outcome = subprocess.run(
                        ["bash", "-c", command],
                        capture_output=True,
                        text=True,
                        env={**base_env, "REACHY_ANDROID_SERIAL": pinned},
                        check=False,
                    )
                    self.assertEqual(outcome.returncode, 0, outcome.stderr)
                    self.assertEqual(outcome.stdout.strip(), pinned)
            for pinned in ("old-api", "wrong-abi", "emulator-5554", "unauthorized"):
                with self.subTest(rejected=pinned):
                    outcome = subprocess.run(
                        ["bash", "-c", command],
                        capture_output=True,
                        text=True,
                        env={**base_env, "REACHY_ANDROID_SERIAL": pinned},
                        check=False,
                    )
                    self.assertNotEqual(outcome.returncode, 0, pinned)
                    self.assertFalse(outcome.stdout.strip())
            unpinned = subprocess.run(
                ["bash", "-c", command],
                capture_output=True,
                text=True,
                env=base_env,
                check=False,
            )
            self.assertNotEqual(unpinned.returncode, 0)
            self.assertIn("found 2", unpinned.stderr)
        subprocess.run(["bash", "-n", str(RUNNER)], check=True)

    def test_report_assertions_ignore_python_optimization_environment(self) -> None:
        runner = read(RUNNER)
        self.assertIn(
            'python3 -I - "${REPORT_DIR}/${RESULT_FILE}"',
            runner,
        )
        self.assertNotIn(
            'python3 - "${REPORT_DIR}/${RESULT_FILE}"',
            runner,
        )

    def test_human_speech_evidence_is_privacy_scoped(self) -> None:
        runner = read(RUNNER)
        source = read(APP / "ReachyRma190OfflineSpeechEndToEndAcceptance.cs")
        self.assertNotIn("logcat -c", runner)
        self.assertNotIn("logcat -d", runner)
        self.assertNotIn("dumpsys audio", runner)
        self.assertNotIn("dumpsys connectivity", runner)
        self.assertNotIn("dumpsys package", runner)
        self.assertIn('read_report > "${REPORT_DIR}/device-report.json"', runner)
        self.assertIn("error = exception.GetType().Name", source)
        self.assertNotIn("exception.ToString()", source)
        self.assertNotIn("snapshot.Detail", source)
        self.assertNotIn("final_detail", source)
        self.assertIn("synthetic_speech_injected = false", source)
        self.assertIn("transcript_persisted = false", source)
        self.assertIn("raw_audio_persisted = false", source)

    def test_workflow_is_manual_physical_only(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("\n  push:", workflow)
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("run_rma190_offline_speech_end_to_end_android.sh", workflow)
        pin = workflow.index("- name: Pin one physical API-31+ Android device")
        prepare = workflow.index("- name: Prepare generated Reachy Unity presentation")
        build = workflow.index("- name: Build ARM64 API-26 device APK")
        self.assertLess(pin, prepare)
        self.assertLess(pin, build)
        self.assertIn("adb devices", workflow)
        self.assertIn("sdk >= 31", workflow)
        self.assertNotIn("upload-artifact", workflow)

    def test_authoritative_acceptance_remains_open_until_real_run(self) -> None:
        todo = read(TODO)
        self.assertIn(
            "- [ ] On a device with installed services, the user can converse "
            "using Android on-device ASR and offline TTS with networking disabled.",
            todo,
        )
        self.assertIn(
            "- [ ] On-device ASR -> local LLM -> behavior -> offline TTS.",
            todo,
        )


if __name__ == "__main__":
    unittest.main()

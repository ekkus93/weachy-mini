"""RMA-135/184: retain bounded private physical evidence, not device inventories."""

from __future__ import annotations

import os
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts/run_rma135_resource_governor_acceptance_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma135-resource-governor-android.yml"


class Rma135PrivatePhysicalEvidenceTests(unittest.TestCase):
    def test_report_does_not_collect_or_upload_unrestricted_device_dumps(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        workflow = WORKFLOW.read_text(encoding="utf-8")
        for forbidden in (
            'shell dumpsys package',
            'shell dumpsys thermalservice',
            'shell logcat',
            'shell getprop >',
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, runner)
        self.assertIn('"${REPORT_DIR}/package-before-launch.txt"', runner)
        self.assertIn('"${REPORT_DIR}/thermalservice.txt"', runner)
        self.assertIn('name: rma135-resource-governor-android-', workflow)
        self.assertIn('path: artifacts/rma135-resource-governor-android', workflow)

    def test_activity_resolution_avoids_private_package_inventory(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn('shell cmd package resolve-activity --brief', runner)
        self.assertIn('android.intent.category.LAUNCHER', runner)
        self.assertIn('"${REPORT_DIR}/launch-component.txt"', runner)
        self.assertNotIn('awk \'/android.intent.action.MAIN:', runner)

    def test_pinned_serial_is_hashed_in_uploaded_device_identity(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn('SERIAL_SHA256="$(printf', runner)
        self.assertIn('serial_sha256=%s', runner)
        self.assertNotIn('serial=%s', runner)
        self.assertNotIn('"${DEVICE_SERIAL}" "${model}" "${abi}"', runner)

    def test_early_preflight_failure_clears_stale_evidence(self) -> None:
        # This fixture has no APK, ADB call, or physical-device side effect.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            report = root / "report"
            checkpoints = report / "checkpoints"
            checkpoints.mkdir(parents=True)
            stale = [
                report / "rma135-resource-governor-acceptance.json",
                report / "rma135-resource-governor-acceptance-latest.json",
                report / "rma184-representative-device-long-run.json",
                report / "checkpoint-latest.json",
                report / "checkpoint-files.txt",
                checkpoints / "rma135-resource-governor-checkpoint-00001.json",
            ]
            for file in stale:
                file.write_text('{"status":"passed"}', encoding="utf-8")
            unrelated = report / "operator-notes.txt"
            unrelated.write_text("preserve", encoding="utf-8")
            env = dict(os.environ)
            env["REACHY_ANDROID_SERIAL"] = "fixture-not-a-device"
            env["RMA133_MODEL_CACHE_ROOT"] = str(root / "model-cache")
            result = subprocess.run(
                [
                    "bash",
                    str(RUNNER),
                    str(root / "missing.apk"),
                    str(report),
                ],
                env=env,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("RMA-135 APK is missing", result.stderr)
            for file in stale:
                self.assertFalse(file.exists(), file)
            self.assertEqual(unrelated.read_text(encoding="utf-8"), "preserve")

    def test_structured_performance_and_thermal_evidence_is_preserved(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn('rma184-representative-device-long-run.json', runner)
        self.assertIn('"thermal_pressure_samples"', runner)
        self.assertIn('"thermal_degradation_order_valid"', runner)
        self.assertIn('dumpsys meminfo "${PACKAGE_NAME}"', runner)


if __name__ == "__main__":
    unittest.main()

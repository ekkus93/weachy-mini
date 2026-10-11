"""RMA-135/184: retain bounded private physical evidence, not device inventories."""

from __future__ import annotations

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

    def test_structured_performance_and_thermal_evidence_is_preserved(self) -> None:
        runner = RUNNER.read_text(encoding="utf-8")
        self.assertIn('rma184-representative-device-long-run.json', runner)
        self.assertIn('"thermal_pressure_samples"', runner)
        self.assertIn('"thermal_degradation_order_valid"', runner)
        self.assertIn('dumpsys meminfo "${PACKAGE_NAME}"', runner)


if __name__ == "__main__":
    unittest.main()

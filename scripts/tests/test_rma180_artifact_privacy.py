#!/usr/bin/env python3
"""Guard RMA-180 physical performance artifacts against private global Android logs."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts/run_rma180_performance_acceptance_android.sh"
WORKFLOW = ROOT / ".github/workflows/rma-release-review-contracts.yml"


class Rma180ArtifactPrivacyContractTests(unittest.TestCase):
    def test_never_clear_or_capture_global_logcat(self) -> None:
        source = RUNNER.read_text(encoding="utf-8")
        self.assertNotIn('"${ADB[@]}" logcat -c', source)
        self.assertNotIn('"${ADB[@]}" logcat -d', source)
        self.assertNotIn('> "${REPORT_DIR}/logcat.txt"', source)
        self.assertIn('"${REPORT_DIR}/logcat.txt"', source)

    def test_retains_real_perf_and_bounded_resource_measurements(self) -> None:
        source = RUNNER.read_text(encoding="utf-8")
        for token in (
            "dumpsys battery",
            'dumpsys meminfo "${PACKAGE_NAME}"',
            "dumpsys thermalservice",
            "reachy_rma180_performance_acceptance true",
            'timings["NativePhysics"]["sample_count"] > 0',
            'timings["UnityRendering"]["sample_count"] > 0',
            'profile["resources"]["sample_count"] > 0',
        ):
            self.assertIn(token, source)
        self.assertNotIn("screencap", source)
        self.assertNotIn("dumpsys audio", source)

    def test_release_review_runs_the_privacy_contract(self) -> None:
        self.assertIn(
            "python3 scripts/tests/test_rma180_artifact_privacy.py",
            WORKFLOW.read_text(encoding="utf-8"),
        )


if __name__ == "__main__":
    unittest.main()

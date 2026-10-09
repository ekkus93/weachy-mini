#!/usr/bin/env python3
"""Keep physical local-LLM acceptance evidence free of device-wide private logs."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = (
    ROOT / "scripts/run_rma134_local_llm_acceptance_android.sh",
    ROOT / "scripts/run_rma135_resource_governor_acceptance_android.sh",
)
RELEASE_REVIEW = ROOT / ".github/workflows/rma-release-review-contracts.yml"


class Rma134Rma135EvidencePrivacyTests(unittest.TestCase):
    def test_device_wide_diagnostics_are_not_captured_or_cleared(self) -> None:
        for path in SCRIPTS:
            with self.subTest(script=path.name):
                source = path.read_text(encoding="utf-8")
                for forbidden in (
                    "devices -l >",
                    "shell getprop >",
                    "logcat -d -v threadtime",
                    "logcat -c",
                    "dumpsys activity activities",
                    "shell ps -A",
                ):
                    self.assertNotIn(forbidden, source)
                for old in (
                    "adb-devices.txt",
                    "getprop.txt",
                    "logcat.txt",
                    "activity.txt",
                    "processes.txt",
                ):
                    self.assertIn('"${REPORT_DIR}/' + old + '"', source)
                self.assertIn('dumpsys meminfo "${PACKAGE_NAME}"', source)
                self.assertIn(
                    'read_latest_checkpoint > "${REPORT_DIR}/checkpoint-latest.json"',
                    source,
                )

    def test_release_review_guards_both_physical_evidence_paths(self) -> None:
        workflow = RELEASE_REVIEW.read_text(encoding="utf-8")
        self.assertIn(
            "python3 scripts/tests/test_rma134_rma135_artifact_privacy.py", workflow
        )


if __name__ == "__main__":
    unittest.main()

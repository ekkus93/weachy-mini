#!/usr/bin/env python3
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
DOC = ROOT / "docs/validation/RALPH_LOOP_CI_INFRASTRUCTURE_BLOCKER_2026-09-28.md"

class InfrastructureBlockerContract(unittest.TestCase):
    def test_record_preserves_failure_classification(self):
        text = DOC.read_text(encoding="utf-8")
        for token in (
            "36461067521",
            "109059595985",
            "successfully completed",
            "Upload Unity test results",
            "artifact-publication/infrastructure failure",
            "No TODO checkbox is closed",
            "downstream APK/device steps: skipped",
            "secret-redacted text",
        ):
            self.assertIn(token, text)

if __name__ == "__main__":
    unittest.main()

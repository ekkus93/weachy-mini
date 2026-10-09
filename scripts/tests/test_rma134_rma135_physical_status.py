#!/usr/bin/env python3
"""Fail closed when the RMA-134/135 Android acceptance steps are skipped."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = (
    ROOT / ".github/workflows/rma134-local-llm-android.yml",
    ROOT / ".github/workflows/rma135-resource-governor-android.yml",
)


class PhysicalStatusTests(unittest.TestCase):
    def test_rma135_baseline_requires_actual_evidence(self) -> None:
        text = WORKFLOWS[1].read_text(encoding="utf-8")
        self.assertIn("--json databaseId", text)
        self.assertIn("/actions/runs/${run_id}/artifacts", text)
        self.assertIn("select(.expired == false)", text)
        self.assertIn("rma135-resource-governor-android-", text)
        self.assertIn('[[ "${candidate}" =~ ^[0-9a-f]{40}$', text)
        self.assertIn('git merge-base --is-ancestor "${candidate}" "${SHA}"', text)
        self.assertNotIn("--json headSha", text)

    def test_skip_never_marks_physical_accepted(self) -> None:
        for workflow in WORKFLOWS:
            with self.subTest(workflow=workflow.name):
                source = workflow.read_text(encoding="utf-8")
                self.assertIn("const scope = process.env.RELEVANT;", source)
                self.assertIn("const finalState =", source)
                self.assertIn('!relevant ? "pending" : succeeded ? "success" : "failure"', source)
                self.assertIn("Physical acceptance skipped; exact-head evidence pending", source)
                self.assertIn("Source-scope check unavailable", source)
                self.assertIn("state: finalState,", source)
                self.assertNotIn('state: succeeded ? "success" : "failure"', source)


if __name__ == "__main__":
    unittest.main()

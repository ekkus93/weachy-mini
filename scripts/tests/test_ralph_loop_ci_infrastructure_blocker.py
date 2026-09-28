#!/usr/bin/env python3
"""Guard the Ralph-loop CI infrastructure blocker classification."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOC = ROOT / "docs/validation/RALPH_LOOP_CI_INFRASTRUCTURE_BLOCKER_2026-09-28.md"


class RalphLoopCiInfrastructureBlockerTests(unittest.TestCase):
    def test_blocker_document_preserves_no_closure_boundary(self) -> None:
        doc = DOC.read_text(encoding="utf-8")
        for token in (
            "does **not** close any RMA checklist item",
            "does **not** claim that any pending pull request is source-qualified",
            "authoritative TODO checkboxes must remain open",
        ):
            self.assertIn(token, doc)

    def test_blocker_classifies_zero_step_signature_as_infrastructure(self) -> None:
        doc = DOC.read_text(encoding="utf-8")
        for token in (
            "JOB_NOT_FOUND",
            "no failed-step excerpts",
            "jobs expose no steps",
            "no compiler diagnostics",
            "classified as GitHub Actions, runner, log-retention, workflow-visibility",
        ):
            self.assertIn(token, doc)

    def test_blocker_records_unavailable_remediation_paths(self) -> None:
        doc = DOC.read_text(encoding="utf-8")
        for token in (
            "WRITE_PERMISSION_REQUIRED",
            "LOCAL_RUNNER_DISABLED",
            "VALIDATION_PROFILE_NOT_FOUND",
            "Guarded merge of PR #91 is rejected",
        ):
            self.assertIn(token, doc)

    def test_blocker_requires_exact_head_requalification_before_merge_or_closure(self) -> None:
        doc = DOC.read_text(encoding="utf-8")
        for token in (
            "exact-head qualification must be re-established",
            "normal job logs/steps again",
            "successfully reruns failed jobs",
            "approved repository profile",
            "authorized equivalent exact-head qualification path",
        ):
            self.assertIn(token, doc)


if __name__ == "__main__":
    unittest.main()

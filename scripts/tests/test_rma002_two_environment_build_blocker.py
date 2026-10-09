#!/usr/bin/env python3
"""Guard the RMA-002 two-environment build evidence boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
BLOCKER = ROOT / "docs/validation/RMA_002_TWO_ENVIRONMENT_BUILD_EVIDENCE_BLOCKER_2026-09-25.md"


class Rma002TwoEnvironmentBuildBlockerTests(unittest.TestCase):
    def test_todo_keeps_two_environment_acceptance_open(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        self.assertIn(
            "- [ ] Two clean developer environments can build the same minimal Android application.",
            todo,
        )
        self.assertNotIn(
            "- [x] Two clean developer environments can build the same minimal Android application.",
            todo,
        )

    def test_blocker_document_defines_evidence_without_closure_claim(self) -> None:
        blocker = BLOCKER.read_text(encoding="utf-8")
        for token in (
            "does **not** close the RMA-002 checklist item",
            "two independently provisioned clean environments",
            "exact environment identities",
            "same minimal Android build command",
            "artifact hash comparison",
            "does not claim reproducibility",
            "external qualification dependency",
        ):
            self.assertIn(token, blocker)


if __name__ == "__main__":
    unittest.main()

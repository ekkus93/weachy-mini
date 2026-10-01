#!/usr/bin/env python3
"""Guard the RMA-190 model corruption/OOM evidence boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
CONSOLIDATION = ROOT / "docs" / "validation" / "RMA_190_MODEL_FAILURE_CONSOLIDATION_2026-09-24.md"
LOCAL_VALIDATION = (
    ROOT
    / "docs"
    / "validation"
    / "RMA_190_MODEL_CORRUPTION_OOM_LOCAL_VALIDATION_2026-09-24.md"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190ModelFailureBoundaryTests(unittest.TestCase):
    def test_todo_keeps_model_corruption_oom_open(self) -> None:
        todo = read(TODO)
        self.assertIn("- [ ] Model corruption/OOM.", todo)
        self.assertNotIn("- [x] Model corruption/OOM.", todo)

    def test_consolidation_records_redacted_source_boundary_without_success_claim(self) -> None:
        source = read(CONSOLIDATION)
        for token in (
            "inspected head: `6350208c5beaa8566c5240a8a077b5972f4a948a`",
            "package-manager corruption",
            "provider out-of-memory behavior",
            "redacted URL-bearing record",
            "must not be reconstructed",
            "does **not** close the RMA-190 `Model corruption/OOM` checkbox",
            "No installed-model success",
        ):
            self.assertIn(token, source)

    def test_local_validation_doc_remains_branch_evidence_only(self) -> None:
        source = read(LOCAL_VALIDATION)
        for token in (
            "not Android physical evidence",
            "not roadmap checkbox closure",
            "current `master` therefore retains this as evidence-boundary documentation only",
            "still requires readable-source preservation",
        ):
            self.assertIn(token, source)


if __name__ == "__main__":
    unittest.main()

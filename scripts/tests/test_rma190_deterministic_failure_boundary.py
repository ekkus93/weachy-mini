#!/usr/bin/env python3
"""Guard the RMA-190 deterministic failure source-visibility boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/validation/RMA_190_DETERMINISTIC_FAILURE_ACCEPTANCE_STATUS_2026-09-28.md"


class Rma190DeterministicFailureBoundaryTests(unittest.TestCase):
    def test_failure_rows_track_exact_acceptance_evidence(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        for accepted in (
            "Network loss, rate limit, malformed response, and cancellation.",
            "Model corruption/OOM.",
        ):
            self.assertIn(f"- [x] {accepted}", todo)
            self.assertNotIn(f"- [ ] {accepted}", todo)
        for evidence in (
            "RMA-190 Provider End-to-End Acceptance",
            "37757168563",
            "b9f4d7397630f6f6392f2dee9bf586ab1bb68f7b",
            "11540502224",
            "RMA-190 Model Failure End-to-End Acceptance",
            "37759478803",
            "6f3019c4c9e8a8c963161267defaff71a966a658",
            "11541646432",
        ):
            self.assertIn(evidence, todo)

        item = "Solver fault and controlled reset."
        self.assertIn(f"- [ ] {item}", todo)
        self.assertNotIn(f"- [x] {item}", todo)

    def test_status_names_redacted_source_and_no_success_claim(self) -> None:
        status = STATUS.read_text(encoding="utf-8")
        for token in (
            "Program.Rma190DeterministicFailureAcceptance.LocalModel.cs",
            "Program.Rma190DeterministicFailureAcceptance.Network.cs",
            "redacted by Ralph output",
            "avoids reconstructing source from redacted data",
            "No hidden fallback, provider-success, model-failure, or "
            "solver-recovery success is claimed",
        ):
            self.assertIn(token, status)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Guard the RMA-190 deterministic failure source-visibility boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/validation/RMA_190_DETERMINISTIC_FAILURE_ACCEPTANCE_STATUS_2026-09-28.md"


class Rma190DeterministicFailureBoundaryTests(unittest.TestCase):
    def test_failure_rows_remain_open_until_exact_evidence(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        for item in (
            "Network loss, rate limit, malformed response, and cancellation.",
            "Model corruption/OOM.",
            "Solver fault and controlled reset.",
        ):
            self.assertIn(f"- [ ] {item}", todo)
            self.assertNotIn(f"- [x] {item}", todo)

    def test_status_names_redacted_source_and_no_success_claim(self) -> None:
        status = STATUS.read_text(encoding="utf-8")
        for token in (
            "Program.Rma190DeterministicFailureAcceptance.LocalModel.cs",
            "Program.Rma190DeterministicFailureAcceptance.Network.cs",
            "redacted by Ralph output",
            "avoids reconstructing source from redacted data",
            "No hidden fallback, provider-success, model-failure, or solver-recovery success is claimed",
        ):
            self.assertIn(token, status)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Guard the RMA-190 network/provider failure evidence boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/validation/RMA_190_NETWORK_FAILURE_CONSOLIDATION_2026-09-24.md"
VALIDATION = ROOT / "docs/validation/RMA_190_NETWORK_FAILURE_MODES_LOCAL_VALIDATION_2026-09-24.md"


class Rma190NetworkFailureBoundaryTests(unittest.TestCase):
    def test_network_failure_row_remains_open(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        item = "Network loss, rate limit, malformed response, and cancellation."
        self.assertIn(f"- [ ] {item}", todo)
        self.assertNotIn(f"- [x] {item}", todo)

    def test_status_does_not_claim_provider_success_or_checkbox_closure(self) -> None:
        status = STATUS.read_text(encoding="utf-8")
        validation = VALIDATION.read_text(encoding="utf-8")
        for token in (
            "inspected head: `79339b3ca97e68441fcda97aae167613bca1c2c4`",
            "does **not** transplant the network/provider failure source wholesale",
            "does **not** close the RMA-190",
            "without reconstructing redacted content",
        ):
            self.assertIn(token, status)
        for token in (
            "not physical Android evidence",
            "provider-success evidence",
            "python3 scripts/tests/test_rma190_network_failure_modes.py",
            "# exit 0",
            "CI infrastructure, not source failure",
        ):
            self.assertIn(token, validation)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Guard the bounded RMA-184 profile-summary evidence boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/validation/RMA_184_A54_PROFILE_SUMMARY_STATUS_2026-09-28.md"


class Rma184ProfileSummaryStatusTests(unittest.TestCase):
    def test_todo_closes_measured_profile_publication_but_not_device_release_gates(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        self.assertIn("- [x] Publish measured default profiles.", todo)
        self.assertNotIn("- [ ] Publish measured default profiles.", todo)
        self.assertIn("RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md", todo)
        for open_item in (
            "- [ ] Long-running tests do not accumulate unbounded memory or state lag.",
            "- [ ] Thermal degradation follows the documented priority order.",
            "- [ ] Supported devices meet the defined simulation and interaction targets.",
            "- [ ] Representative-device performance report exists.",
        ):
            self.assertIn(open_item, todo)
            self.assertNotIn(open_item.replace("[ ]", "[x]"), todo)

    def test_status_records_only_bounded_visible_summary(self) -> None:
        status = STATUS.read_text(encoding="utf-8")
        for token in (
            "Workflow head: `4eabd6995aa713686526eeecf6b2422d0e043745`",
            "Run ID: `36192045766`",
            "Job ID: `108259212316`",
            '"profile_count":2',
            '"status":"passed"',
            "does not close the authoritative RMA-184 or RMA-194",
            "does not reconstruct or fabricate",
            "detailed metric-level report",
        ):
            self.assertIn(token, status)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Prevent the release status snapshot from drifting from the RMA-190 roadmap."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/RMA_194_RELEASE_ACCEPTANCE_STATUS_2026-09-28.md"
SCENARIO = re.compile(r"^- \[([x ])\] (.+)$", re.MULTILINE)
BULLET = re.compile(r"^- (.+)$", re.MULTILINE)


def section(text: str, start: str, end: str) -> str:
    before, sep, tail = text.partition(start)
    if not sep:
        raise AssertionError(f"Missing section: {start}")
    body, sep, _ = tail.partition(end)
    if not sep:
        raise AssertionError(f"Missing section terminator: {end}")
    return body


class Rma194StatusReconciliationTests(unittest.TestCase):
    def test_rma184_profile_publication_is_reconciled_without_physical_overclaim(
        self,
    ) -> None:
        todo = TODO.read_text(encoding="utf-8")
        status = STATUS.read_text(encoding="utf-8")
        rma184 = section(todo, "## RMA-184", "## RMA-195")
        self.assertIn("- [x] Publish measured default profiles.", rma184)
        self.assertIn("36187273962", rma184)
        self.assertIn("36187273962", status)
        self.assertIn("RMA_184_A54_DEFAULT_PROFILE_CAPTURE_2026-09-25.md", status)
        self.assertNotIn("measured default-profile publication remains pending", status)
        for missing_qualification in (
            "1,800-second integrated long-run",
            "thermal degradation order",
            "representative supported-device",
        ):
            self.assertIn(missing_qualification, status)
        self.assertIn(
            "- [ ] Long-running tests do not accumulate unbounded memory or state lag.",
            rma184,
        )
        self.assertIn(
            "- [ ] Thermal degradation follows the documented priority order.",
            rma184,
        )
        self.assertIn(
            "- [ ] Supported devices meet the defined simulation and interaction targets.",
            rma184,
        )

    def test_rma190_scenario_status_matches_authoritative_todo(self) -> None:
        todo = TODO.read_text(encoding="utf-8")
        status = STATUS.read_text(encoding="utf-8")
        roadmap = section(todo, "## RMA-190", "## RMA-191")
        snapshot = section(
            status,
            "## RMA-190 status snapshot",
            "## Current exact-head evidence on `master`",
        )
        rows = SCENARIO.findall(roadmap)
        self.assertEqual(11, len(rows), "RMA-190 scenario count changed; audit this gate")
        self.assertEqual(11, len({item for _, item in rows}))
        checked = {item for flag, item in rows if flag == "x"}
        open_items = {item for flag, item in rows if flag == " "}
        accepted_text = section(
            snapshot,
            "The authoritative TODO records these accepted scenarios:",
            "The following scenarios remain open until their own end-to-end evidence passes:",
        )
        open_text = (
            snapshot.split(
                "The following scenarios remain open until their own end-to-end evidence passes:",
                1,
            )[1]
            .lstrip("\n")
            .split("\n\n", 1)[0]
        )
        self.assertEqual(checked, set(BULLET.findall(accepted_text)))
        self.assertEqual(open_items, set(BULLET.findall(open_text)))
        self.assertFalse(checked & open_items)
        self.assertIn("fixture endpoints rather than live cloud credentials", snapshot)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""RMA-190 consolidation-status drift checks."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/RMA_190_CONSOLIDATION_STATUS_2026-09-24.md"
PERMISSION_UPDATE = ROOT / "docs/RMA_190_CONSOLIDATION_PERMISSION_UPDATE_2026-09-25.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def collapse_whitespace(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def rma190_scenarios() -> list[str]:
    todo = read(TODO)
    section = todo.split("## RMA-190 — Build automated end-to-end scenarios", 1)[1]
    checklist = section.split("## RMA-191 — Complete privacy and security review", 1)[0]
    return [
        match.group(1).strip()
        for match in re.finditer(r"^- \[ \] (.+)$", checklist, flags=re.MULTILINE)
    ]


class Rma190ConsolidationStatusContracts(unittest.TestCase):
    def setUp(self) -> None:
        self.status = read(STATUS)
        self.combined_status = self.status + "\n" + read(PERMISSION_UPDATE)
        self.status_flat = collapse_whitespace(self.combined_status)

    def test_every_open_rma190_scenario_has_a_canonical_lane_entry(self) -> None:
        missing = []
        for scenario in rma190_scenarios():
            variants = {
                scenario,
                scenario.removesuffix("."),
                scenario.replace(", and ", ", "),
                scenario.removesuffix(".").replace(", and ", ", "),
            }
            if not any(variant in self.combined_status for variant in variants):
                missing.append(scenario)
        self.assertEqual([], missing)

    def test_status_file_does_not_close_scenarios(self) -> None:
        for token in (
            "does **not** close any RMA-190 checkbox",
            "Scenario checkboxes remain open until",
            "required exact-head CI",
            "physical Android",
            "provider, native, model-package, and artifact evidence",
        ):
            self.assertIn(token, self.status_flat)

    def test_truncated_todo_reconciliation_is_explicitly_blocked(self) -> None:
        for token in (
            "PR #39",
            "unsafe as currently written",
            "+20 / -3362",
            "only 76 lines long",
            "not a valid TODO reconciliation",
            "PR #71",
            "safe replacement evidence lane",
        ):
            self.assertIn(token, self.status_flat)

    def test_duplicate_lanes_must_not_be_discarded_without_transplant(self) -> None:
        for token in (
            "must not be closed or discarded until their unique coverage is transplanted",
            "Consolidate network/model/solver duplicate coverage",
            "transplant unique required changes into the canonical branch",
        ):
            self.assertIn(token, self.status_flat)

    def test_current_global_blocker_is_infrastructure_not_source(self) -> None:
        for token in (
            "zero failed steps",
            "JOB_NOT_FOUND",
            "CI infrastructure failures, not as repository source failures",
            "Re-run exact-head CI only after the zero-step Actions failure is fixed",
        ):
            self.assertIn(token, self.status_flat)

    def test_supporting_lanes_are_not_counted_as_scenario_closure(self) -> None:
        for token in (
            "support the scenario lanes but should not be counted as separate RMA-190 scenario closure lanes",
            "main-thread metadata safety",
            "SHA-256 type shadowing",
            "helper restoration",
        ):
            self.assertIn(token, self.status_flat)

    def test_permission_lane_reflects_current_pr84_inventory(self) -> None:
        for token in (
            "PR #84",
            "ralph/rma190-permission-denial-revocation-e2e",
            "PR #53",
            "PR #53 and PR #84 have been compared",
        ):
            self.assertIn(token, self.status_flat)


if __name__ == "__main__":
    unittest.main()

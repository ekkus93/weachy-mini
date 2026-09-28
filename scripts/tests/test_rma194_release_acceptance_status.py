#!/usr/bin/env python3
"""RMA-194 release-acceptance status map drift checks."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs/RMA_194_RELEASE_ACCEPTANCE_STATUS_2026-09-28.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def rma194_unchecked_items() -> list[str]:
    todo = read(TODO)
    section = todo.split("## RMA-194 — Release acceptance checklist", 1)[1]
    checklist = section.split("**Acceptance criteria — initial release gate**", 1)[0]
    return [
        match.group(1).strip()
        for match in re.finditer(r"^- \[ \] (.+)$", checklist, flags=re.MULTILINE)
    ]


class Rma194ReleaseAcceptanceStatusContracts(unittest.TestCase):
    def test_every_open_rma194_release_item_is_mapped(self) -> None:
        status = read(STATUS)
        missing = [item for item in rma194_unchecked_items() if item not in status]
        self.assertEqual([], missing)

    def test_status_file_is_not_a_todo_closure_claim(self) -> None:
        status = read(STATUS)
        for token in (
            "planning/status artifact only",
            "does not close any checkbox",
            "TODO checkbox closure must wait for merged evidence",
            "exact current-master reconciliation pass",
        ):
            self.assertIn(token, status)

    def test_blockers_cover_ci_spoken_turn_and_rma002(self) -> None:
        status = read(STATUS)
        for token in (
            "zero-step/`JOB_NOT_FOUND` infrastructure failures",
            "Full RMA-125/RMA-190 spoken-turn evidence is still missing",
            "RMA-002 still requires second independently provisioned Unity/Android build evidence",
            "PR #91 is the current consolidation lane",
        ):
            self.assertIn(token, status)

    def test_unmerged_evidence_branches_are_explicitly_not_final_release_evidence(self) -> None:
        status = read(STATUS)
        for token in (
            "Evidence exists in unmerged PRs",
            "Branch ready but unmerged",
            "Implemented in branches; not merged",
            "Exact-head hosted qualification",
            "Merge-gate CI",
        ):
            self.assertIn(token, status)

    def test_non_blocker_classifications_do_not_hide_source_failures(self) -> None:
        status = read(STATUS)
        self.assertIn("Artifact upload quota failures are infrastructure failures", status)
        self.assertIn("Zero-step PR CI failures with unavailable logs are infrastructure failures", status)
        self.assertIn("hardware availability condition, not a source failure", status)
        self.assertIn("Lack of physical Reachy Mini hardware is not a release blocker", status)

    def test_status_tracks_current_pr_references(self) -> None:
        status = read(STATUS)
        for token in (
            "PR #91",
            "PR #83",
            "RMA-192 license/attribution review",
            "RMA-190 consolidation evidence",
            "RMA-191 security/privacy evidence now folded into PR #91",
        ):
            self.assertIn(token, status)


if __name__ == "__main__":
    unittest.main()

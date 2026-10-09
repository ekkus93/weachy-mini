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


def rma194_acceptance_items() -> list[tuple[bool, str]]:
    """Parse the separate final acceptance criteria, not the release checklist."""
    todo = read(TODO)
    section = todo.split("**Acceptance criteria — initial release gate**", 1)[1]
    section = section.split("# Future milestones", 1)[0]
    return [
        (flag == "x", item.strip())
        for flag, item in re.findall(r"^- \[([x ])\] (.+)$", section, flags=re.MULTILINE)
    ]


class Rma194ReleaseAcceptanceStatusContracts(unittest.TestCase):
    def test_final_acceptance_status_table_matches_authoritative_todo(self) -> None:
        status = read(STATUS)
        acceptance = rma194_acceptance_items()
        self.assertEqual(3, len(acceptance), "RMA-194 acceptance criteria changed; audit")
        section = status.split("## Final acceptance criteria status", 1)[1]
        section = section.split("\n## ", 1)[0]
        rows = re.findall(r"^\| (.+?) \| (Open|Accepted)\. \|", section, flags=re.MULTILINE)
        expected = [(item, "Accepted" if accepted else "Open") for accepted, item in acceptance]
        self.assertEqual(expected, rows)
        if any(not accepted for accepted, _ in acceptance):
            self.assertIn("Release status: not accepted", status)
            self.assertIn("No final release tag", status)

    def test_every_open_rma194_release_item_is_mapped(self) -> None:
        status = read(STATUS)
        missing = [item for item in rma194_unchecked_items() if item not in status]
        self.assertEqual([], missing)

    def test_status_file_is_not_a_todo_closure_claim(self) -> None:
        status = read(STATUS)
        for token in (
            "planning/status artifact only",
            "does **not** close any checkbox",
            "TODO checkbox closure must wait for exact current-master evidence",
            "exact current-master evidence",
        ):
            self.assertIn(token, status)

    def test_blockers_cover_ci_spoken_turn_rma002_rma184_and_release_license(self) -> None:
        status = read(STATUS)
        for token in (
            "zero-step/`JOB_NOT_FOUND` infrastructure failures",
            "Full RMA-125/RMA-190 spoken-turn evidence remains missing",
            "RMA-002 still requires second independently provisioned Unity/Android build evidence",
            "RMA-184 has published measured A54 30/60 FPS default profiles",
            "the 1,800-second integrated long-run, thermal-order",
            "RMA-194 license row still needs final release-candidate packaging/release audit",
        ):
            self.assertIn(token, status)

    def test_open_items_are_explicitly_open_not_claimed_done(self) -> None:
        status = read(STATUS)
        self.assertGreaterEqual(status.count("| Open."), len(rma194_unchecked_items()))
        self.assertIn("real cloud-provider success is not claimed", status)
        self.assertIn("detailed rows are not reconstructed from redacted output", status)
        self.assertIn(
            "partial reconstruction from redacted inventory output is not acceptable",
            status,
        )

    def test_non_blocker_classifications_do_not_hide_source_failures(self) -> None:
        status = read(STATUS)
        self.assertIn("Artifact upload quota failures are infrastructure failures", status)
        self.assertIn(
            "Zero-step CI failures with unavailable logs are infrastructure failures",
            status,
        )
        self.assertIn("hardware availability condition, not a source failure", status)
        self.assertIn("Lack of physical Reachy Mini hardware is not a release blocker", status)


if __name__ == "__main__":
    unittest.main()

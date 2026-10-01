#!/usr/bin/env python3
"""RMA-194 release-acceptance status gate.

This gate prevents the repository from accidentally describing the current tree as
release-accepted while the authoritative TODO still has open physical,
end-to-end, privacy, license, documentation, and release criteria.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
GUIDE = ROOT / "docs/ANDROID_RELEASE_OPERATOR_GUIDE.md"
STATUS = ROOT / "docs/RMA_194_RELEASE_ACCEPTANCE_STATUS_2026-09-28.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma194ReleaseAcceptanceTests(unittest.TestCase):
    def test_status_document_keeps_release_unaccepted_until_evidence_closes(self) -> None:
        status = read(STATUS)
        self.assertIn("Release status: not accepted", status)
        self.assertIn("No final release tag", status)
        self.assertIn("No final release label is assigned", status)
        for token in ("RMA-125", "RMA-184", "RMA-190", "RMA-191", "RMA-192", "RMA-193"):
            self.assertIn(token, status)

    def test_authoritative_todo_keeps_unqualified_release_items_open(self) -> None:
        todo = read(TODO)
        for open_item in (
            "- [ ] App launches and runs offline on supported hardware.",
            "- [ ] Android on-device ASR and offline TTS work where installed.",
            "- [ ] Selected benchmark-backed local LLM works without blocking physics.",
            "- [ ] OpenAI and compatible providers are independently configurable.",
            "- [ ] Behavior planner validates all AI output.",
            "- [ ] No silent provider, privacy, calibration, or kinematic fallbacks remain.",
            "- [ ] Diagnostics identify active fidelity, providers, and failures.",
            "- [ ] Representative-device performance report exists.",
            "- [ ] Licenses and attribution are complete.",
            "- [ ] All incomplete items are either resolved or moved to an "
            "explicitly named later milestone with rationale.",
            "- [ ] No deferred Level 2, Level 3, AR, movable observer camera, "
            "or VLA code is half-enabled in production.",
            "- [ ] The release is labeled accurately as geometric baseline, "
            "dynamic baseline, servo-fidelity, or calibrated twin according to "
            "completed evidence.",
        ):
            self.assertIn(open_item, todo)

    def test_authoritative_todo_preserves_evidenced_checked_items(self) -> None:
        todo = read(TODO)
        for checked_item in (
            "- [x] MuJoCo is authoritative and stable.",
            "- [x] Full closed-loop Reachy model is rendered from native state.",
            "- [x] Level 1 camera reprojection passes reference tests.",
        ):
            self.assertIn(checked_item, todo)
        self.assertIn("RMA-032/060-065", todo)
        self.assertIn("RMA-051/052", todo)
        self.assertIn("RMA_104_REPROJECTION_TEST_SUITE_VALIDATION", todo)

    def test_deferred_future_features_remain_out_of_release_scope(self) -> None:
        todo = read(TODO)
        guide = read(GUIDE)
        status = read(STATUS)
        combined = "\n".join((todo, guide, status))
        for token in (
            "Level 2 depth reprojection",
            "Level 3 scene reconstruction",
            "AR placement",
            "movable observer camera",
            "VLA",
        ):
            self.assertIn(token, combined)
        self.assertIn("must remain disabled", guide)
        self.assertIn("must not be labeled as active release features", status)

    def test_release_labeling_rule_rejects_calibrated_claim_without_physical_profile(self) -> None:
        guide = read(GUIDE)
        status = read(STATUS)
        self.assertIn("Do not label the app above the evidence actually passed", guide)
        self.assertIn("No calibrated twin label is allowed", status)
        self.assertIn("named physical calibration profile", status)
        self.assertIn("held-out physical evidence", status)


if __name__ == "__main__":
    unittest.main()

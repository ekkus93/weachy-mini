#!/usr/bin/env python3
"""Guard the RMA-192 license/attribution review boundary."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
STATUS = ROOT / "docs" / "validation" / "RMA_192_LICENSE_ATTRIBUTION_REVIEW_STATUS_2026-09-28.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma192LicenseReviewBoundaryTests(unittest.TestCase):
    def test_rma192_checkboxes_remain_open(self) -> None:
        todo = read(TODO)
        for item in (
            "Reconcile packaged dependencies/assets against inventory.",
            "Verify notices in repository and app.",
            "Verify modified Reachy-derived assets have required attribution/share-alike treatment.",
            "Verify no endorsement claim.",
            "Verify selected local model redistribution/download presentation.",
        ):
            self.assertIn(f"- [ ] {item}", todo)
            self.assertNotIn(f"- [x] {item}", todo)

    def test_status_records_redacted_inventory_boundary(self) -> None:
        status = read(STATUS)
        for token in (
            "inspected head: `9c13614f78849e2a2b469f0d4e9c8b6c7f2af945`",
            "third_party/inventory.json",
            "redacted URL-bearing record",
            "not transplanted into current `master`",
            "No partial inventory",
            "does **not** close RMA-192",
        ):
            self.assertIn(token, status)


if __name__ == "__main__":
    unittest.main()

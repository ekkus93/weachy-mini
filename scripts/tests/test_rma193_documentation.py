#!/usr/bin/env python3
"""RMA-193 release documentation coverage gate."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs/ANDROID_RELEASE_OPERATOR_GUIDE.md"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma193DocumentationCoverageTests(unittest.TestCase):
    def test_android_release_operator_guide_covers_every_rma193_topic(self) -> None:
        guide = read(GUIDE)
        required_headings = (
            "## Build instructions",
            "## Android device requirements",
            "## Model installation and import",
            "## Provider configuration and BYOK warning",
            "## Calibration workflow",
            "## Fidelity-level explanation",
            "## Camera Level 1 approximation and missing-pixel behavior",
            "## Troubleshooting and diagnostic bundles",
            "## Privacy and data-flow explanation",
        )
        for heading in required_headings:
            self.assertIn(heading, guide)

    def test_release_documentation_preserves_fail_closed_product_boundaries(self) -> None:
        guide = read(GUIDE)
        for token in (
            "must not be automatic fallbacks",
            "never load placeholder calibration",
            "Invalid pixels must not reuse stale content",
            "Cloud LLM may receive conversation text",
            "Cloud VLM may receive transformed camera frames",
            "zero-step runner failure",
            "infrastructure evidence",
            "not source acceptance",
            "physical Reachy Mini hardware is not required",
        ):
            self.assertIn(token, guide)

    def test_authoritative_todo_points_to_rma193_requirements(self) -> None:
        todo = read(TODO)
        self.assertIn("## RMA-193 — Complete documentation", todo)
        for topic in (
            "Build instructions",
            "Android device requirements",
            "Model installation/import",
            "Provider configuration and BYOK warning",
            "Calibration workflow",
            "Fidelity-level explanation",
            "Camera Level 1 approximation and missing-pixel behavior",
            "Troubleshooting and diagnostic bundle instructions",
            "Privacy and data-flow explanation",
        ):
            self.assertIn(topic, todo)


if __name__ == "__main__":
    unittest.main()

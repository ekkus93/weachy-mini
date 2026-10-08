#!/usr/bin/env python3
"""Guard RMA-002 two-machine Unity/Android build evidence."""

from __future__ import annotations

import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
DOC = ROOT / "docs" / "validation" / "RMA_002_TWO_MACHINE_ANDROID_BUILD_VALIDATION_2026-10-07.md"
WORKFLOWS = ROOT / ".github" / "workflows"


class Rma002TwoMachineBuildEvidenceTests(unittest.TestCase):
    def test_validation_note_records_exact_head_and_runs(self) -> None:
        text = DOC.read_text(encoding="utf-8")
        for token in (
            "5ca67f7ac79f7bfadae3b4712dcac78ceaecdcb9",
            "37710449442",
            "37710449563",
            "37710449497",
            "37710449527",
            "37710449531",
            "37711532523",
            "37711532388",
            "supersedes the older",
            "second independently",
            "does not close any of the remaining release-gated physical rows",
        ):
            self.assertIn(token, text)

    def test_both_physical_workflows_have_build_and_verify_steps(self) -> None:
        workflow_text = "\n".join(
            path.read_text(encoding="utf-8") for path in sorted(WORKFLOWS.glob("*.yml"))
        )
        for token in (
            "Local Unity Android Validation",
            "Local Unity Android Device Validation No Artifacts",
            "Resolve local Unity and Android installations",
            "Prepare generated Reachy Unity presentation",
            "Stage production MuJoCo runtime for Unity Android",
            "Build ARM64 API-26 device APK",
            "Verify ARM64 API-26 device APK",
            "Pin one physical Android device",
        ):
            self.assertIn(token, workflow_text)

    def test_validation_note_does_not_claim_rma184_or_rma190_closure(self) -> None:
        text = DOC.read_text(encoding="utf-8")
        forbidden = (
            r"RMA-184.*complete",
            r"RMA-190.*complete",
            r"offline-speech.*complete",
            r"representative-device performance report exists.*closed",
        )
        for pattern in forbidden:
            self.assertIsNone(re.search(pattern, text, re.IGNORECASE | re.DOTALL))


if __name__ == "__main__":
    unittest.main()

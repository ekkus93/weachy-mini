#!/usr/bin/env python3
"""Guard RMA-002 two-machine Unity/Android build evidence."""

from __future__ import annotations

import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
DOC = ROOT / "docs" / "validation" / "RMA_002_TWO_MACHINE_ANDROID_BUILD_VALIDATION_2026-10-07.md"
WORKFLOWS = ROOT / ".github" / "workflows"


class Rma002TwoMachineBuildEvidenceTests(unittest.TestCase):
    def test_validation_note_records_same_host_and_open_gate(self) -> None:
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
            "same physical host",
            "RMA-002 must remain open",
            "113094904513",
            "113095216424",
            "does not close any of the remaining release-gated physical rows",
        ):
            self.assertIn(token, text)

    def test_authoritative_two_machine_gate_stays_open(self) -> None:
        todo = (ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md").read_text(encoding="utf-8")
        self.assertIn(
            "- [ ] Two clean developer environments can build the same minimal "
            "Android application.",
            todo,
        )

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

    def test_validation_note_does_not_claim_release_gate_closure(self) -> None:
        text = DOC.read_text(encoding="utf-8").lower()
        forbidden_phrases = (
            "rma-184 is complete",
            "rma-184 complete",
            "rma-190 is complete",
            "rma-190 complete",
            "offline-speech is complete",
            "offline speech is complete",
            "representative-device performance report exists is closed",
            "representative-device performance report exists: closed",
        )
        for phrase in forbidden_phrases:
            self.assertNotIn(phrase, text)


if __name__ == "__main__":
    unittest.main()

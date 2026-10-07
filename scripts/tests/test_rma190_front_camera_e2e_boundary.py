#!/usr/bin/env python3
"""RMA-190 front-camera transformed-frame evidence-boundary gate.

The RMA-190 core physical harness proves offline launch, full-model neutral
reset, and deterministic gesture replay. It must not be treated as proof of the
real-camera scenario by itself:

    front camera -> head rotation -> transformed Reachy-eye frame

RMA-154 remains useful supporting evidence for transformed-frame visual-servo
behavior, but it uses deterministic synthetic optics. This source-level contract
keeps that distinction explicit while also requiring the authoritative roadmap to
close the front-camera checkbox only with dedicated live front-camera physical
acceptance evidence.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
SPEC = ROOT / "docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md"
RMA190_VALIDATION = ROOT / "docs/validation/RMA_190_CORE_END_TO_END_VALIDATION_2026-09-29.md"
PERCEPTION_DRIVER = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyAndroidPerceptionDriver.cs"
RMA154_ACCEPTANCE = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma154VisualServoAcceptance.cs"
)
RMA154_FEEDBACK = (
    ROOT / "Assets/ReachyMini/Runtime/Application/ReachyRma154VisualServoAcceptance.Feedback.cs"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190FrontCameraEvidenceBoundaryTests(unittest.TestCase):
    def test_front_camera_scenario_is_closed_only_with_dedicated_evidence(self) -> None:
        todo = read(TODO)
        spec = read(SPEC)
        self.assertIn(
            "- [x] Front camera -> head rotation -> transformed Reachy-eye frame.",
            todo,
        )
        self.assertNotIn(
            "- [ ] Front camera -> head rotation -> transformed Reachy-eye frame.",
            todo,
        )
        for evidence in (
            "RMA-190 Front Camera End-to-End Android Acceptance",
            "37605466118",
            "11475530644",
            "859aa4ed9fc04030e5056511d027af83e0baa85a",
            "shipping front-camera path",
            "pinned a physical Android",
        ):
            self.assertIn(evidence, todo)
        for evidence in (
            "Passed: run `37605466118`, SHA `859aa4ed...`",
            "37605466118",
            "11475530644",
            "859aa4ed9fc04030e5056511d027af83e0baa85a",
            "The remaining six roadmap scenarios remain open",
        ):
            self.assertIn(evidence, spec)
        self.assertNotIn("physical evidence pending", spec)

    def test_core_validation_does_not_overclaim_camera_closure(self) -> None:
        validation = read(RMA190_VALIDATION)
        self.assertIn(
            "Front camera -> head rotation -> transformed Reachy-eye frame",
            validation,
        )
        self.assertIn("Not closed", validation)
        self.assertIn("dedicated live transformed-frame E2E assertion", validation)
        self.assertIn("real front-camera path", validation)
        self.assertIn("RMA-154", validation)
        self.assertIn("synthetic optics", validation)
        self.assertIn("cannot be represented as real front-camera evidence", validation)

    def test_rma154_acceptance_is_synthetic_supporting_evidence_only(self) -> None:
        acceptance = read(RMA154_ACCEPTANCE)
        feedback = read(RMA154_FEEDBACK)
        combined = acceptance + "\n" + feedback
        self.assertIn("SyntheticOpticalTargetFeedbackSource", combined)
        self.assertIn("rma154-synthetic-optical-target", combined)
        self.assertIn("Deterministic RMA-154 physical acceptance optics", combined)
        self.assertIn("ReachyProductionVisualServoFeedbackSource", combined)
        self.assertNotIn("front camera", combined.lower())

    def test_shipping_perception_driver_has_reusable_acceptance_capture_hook(self) -> None:
        driver = read(PERCEPTION_DRIVER)
        self.assertIn("CaptureFrameForAcceptanceAsync", driver)
        self.assertIn("allowWhilePaused: true", driver)
        self.assertIn("TryBuildFrame", driver)
        self.assertIn("ReachyCameraRelativeRotationCalculator.Calculate", driver)
        self.assertIn("ReachyCameraHomographyWarpPipeline", driver)
        self.assertIn("CreateOwnedTrackingFrame", driver)
        self.assertIn("RMA-190 physical acceptance", driver)


if __name__ == "__main__":
    unittest.main()

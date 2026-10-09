#!/usr/bin/env python3
"""RMA-194 deferred-feature and fidelity release-gate contracts."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"
SPEC = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md"
REVIEW = ROOT / "docs/RMA_194_DEFERRED_FEATURE_AND_FIDELITY_RELEASE_GATE_2026-09-25.md"
AUDIT = ROOT / "models/reachy-mini/model-parameter-audit.json"
SETTINGS_STATE = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsState.cs"
PROJECT_METADATA = ROOT / "Assets/ReachyMini/Runtime/Core/ProjectMetadata.cs"
MAIN_SCREEN_VALIDATION = ROOT / "docs/validation/RMA_081_MAIN_SCREEN_VALIDATION_2026-07-31.md"

PRODUCTION_ROOTS = (
    ROOT / "Assets/ReachyMini/Runtime",
    ROOT / "Assets/Plugins/Android",
    ROOT / "android-plugin/src/main",
    ROOT / "native",
    ROOT / "models",
)

FORBIDDEN_PRODUCTION_TOKENS = (
    "ARCore",
    "depth-assisted",
    "DepthAssisted",
    "Level2",
    "Level 2",
    "Level3",
    "Level 3",
    "persistent scene reconstruction",
    "SceneReconstruction",
    "movable observer",
    "MovableObserver",
    "observer camera orbit",
    "world anchoring",
    "WorldAnchoring",
    "Reachy-specific VLA",
    "VLA",
    "learned motor policy",
)

ALLOWED_TEXT_EXTENSIONS = {
    ".cs",
    ".java",
    ".kt",
    ".c",
    ".cc",
    ".cpp",
    ".h",
    ".hpp",
    ".json",
    ".shader",
    ".cginc",
    ".xml",
    ".gradle",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma194DeferredFeatureAndFidelityGate(unittest.TestCase):
    def test_future_milestones_remain_explicitly_deferred(self) -> None:
        todo = read(TODO)
        spec = read(SPEC)
        review = read(REVIEW)
        for token in (
            "Depth-assisted Level 2 reprojection",
            "Persistent Level 3 scene reconstruction",
            "AR placement of Reachy in a real room",
            "User-controlled movable observer camera",
            "Experimental Reachy-specific VLA/learned social-motion policy",
        ):
            self.assertIn(token, todo)
        for token in (
            "Level 2 depth-assisted camera reprojection",
            "Level 3 persistent 3D scene reconstruction",
            "AR placement and ARCore world anchoring",
            "User-controlled observer camera orbit",
            "VLA or end-to-end learned motor policy",
        ):
            self.assertIn(token, spec)
        self.assertIn("not selectable production modes", review)

    def test_production_roots_do_not_half_enable_deferred_features(self) -> None:
        findings: list[str] = []
        for root in PRODUCTION_ROOTS:
            for path in root.rglob("*"):
                if not path.is_file() or path.suffix not in ALLOWED_TEXT_EXTENSIONS:
                    continue
                try:
                    source = path.read_text(encoding="utf-8")
                except UnicodeDecodeError:
                    continue
                for token in FORBIDDEN_PRODUCTION_TOKENS:
                    if token in source:
                        findings.append(f"{path.relative_to(ROOT)}: {token}")
        self.assertEqual([], findings)

    def test_fixed_presentation_camera_contract_remains_documented(self) -> None:
        validation = read(MAIN_SCREEN_VALIDATION)
        for token in (
            "AcceptsUserNavigation == false",
            "fixed front / three-quarter Reachy presentation",
            "fallback",
            "alternate scene",
        ):
            self.assertIn(token, validation)

    def test_current_model_fidelity_is_geometric_and_uncalibrated(self) -> None:
        audit = json.loads(read(AUDIT))
        fidelity = audit["fidelity"]
        diagnostics = audit["diagnostics"]
        self.assertEqual("upstream_geometric_baseline", fidelity["profile_id"])
        self.assertEqual("geometric_baseline", fidelity["classification"])
        self.assertFalse(fidelity["calibrated"])
        self.assertFalse(fidelity["may_be_labeled_calibrated"])
        self.assertEqual("uncalibrated_upstream_baseline", fidelity["diagnostics_status"])
        self.assertEqual("geometric_baseline", diagnostics["classification"])
        self.assertFalse(diagnostics["calibrated"])

    def test_shipping_defaults_do_not_claim_calibrated_or_servo_fidelity(self) -> None:
        settings = read(SETTINGS_STATE)
        metadata = read(PROJECT_METADATA)
        self.assertIn("ReachySimulationFidelity.Standard", settings)
        self.assertIn('"Uncalibrated"', settings)
        self.assertIn(
            "public const SimulationFidelity InitialFidelity = SimulationFidelity.Unavailable;",
            metadata,
        )
        self.assertNotIn(
            "public const SimulationFidelity InitialFidelity = SimulationFidelity.UnitCalibratedTwin;",
            metadata,
        )
        self.assertNotIn(
            "public const SimulationFidelity InitialFidelity = SimulationFidelity.ServoFidelity;",
            metadata,
        )
        self.assertNotIn(
            "public const SimulationFidelity InitialFidelity = SimulationFidelity.DynamicBaseline;",
            metadata,
        )

    def test_review_documents_non_closure_boundary(self) -> None:
        review = read(REVIEW)
        for token in (
            "does **not** close the full RMA-194 release gate",
            "must not claim `calibrated twin`",
            "must not claim `calibrated twin`, `unit-calibrated`",
            "After this gate is committed to master and exact-head CI is healthy",
        ):
            self.assertIn(token, review)


if __name__ == "__main__":
    unittest.main()

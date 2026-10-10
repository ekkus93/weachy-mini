#!/usr/bin/env python3
"""RMA-194 release-acceptance status gate.

This gate prevents the repository from accidentally describing the current tree as
release-accepted while the authoritative TODO still has open physical,
end-to-end, privacy, license, documentation, and release criteria.
"""

from __future__ import annotations

import json
import re
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
            "- [ ] Android on-device ASR and offline TTS work where installed.",
            "- [ ] Representative-device performance report exists.",
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
            "- [x] App launches and runs offline on supported hardware.",
            "- [x] MuJoCo is authoritative and stable.",
            "- [x] Full closed-loop Reachy model is rendered from native state.",
            "- [x] Level 1 camera reprojection passes reference tests.",
            "- [x] Selected benchmark-backed local LLM works without blocking physics.",
            "- [x] OpenAI and compatible providers are independently configurable.",
            "- [x] Behavior planner validates all AI output.",
            "- [x] No silent provider, privacy, calibration, or kinematic fallbacks remain.",
            "- [x] Diagnostics identify active fidelity, providers, and failures.",
            "- [x] Licenses and attribution are complete.",
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

    def test_deferred_ar_xr_packages_are_not_in_shipping_unity_dependencies(self) -> None:
        dependencies = json.loads(read(ROOT / "Packages/manifest.json"))["dependencies"]
        deferred_packages = {
            "com.unity.xr.arfoundation",
            "com.unity.xr.arsubsystems",
            "com.unity.xr.arcore",
            "com.unity.xr.arkit",
            "com.unity.xr.openxr",
            "com.unity.xr.interaction.toolkit",
        }
        self.assertFalse(
            deferred_packages.intersection(dependencies),
            "Deferred AR/XR packages must not enter the shipping Unity manifest",
        )
        self.assertFalse(
            [
                name
                for name in dependencies
                if name.startswith("com.unity.xr.") or name == "com.unity.modules.xr"
            ],
            "Any Unity XR package, including future packages, is deferred",
        )

    def test_shipping_build_scenes_remain_fixed_and_no_deferred_components(self) -> None:
        settings = read(ROOT / "ProjectSettings/EditorBuildSettings.asset")
        scene_paths = [
            line.strip().removeprefix("path: ")
            for line in settings.splitlines()
            if line.strip().startswith("path: ")
        ]
        self.assertEqual(["Assets/ReachyMini/Scenes/Bootstrap.unity"], scene_paths)
        self.assertEqual(1, settings.count("- enabled: 1"))
        self.assertEqual(0, settings.count("- enabled: 0"))
        scene = read(ROOT / scene_paths[0])
        for forbidden in (
            "UnityEngine.XR",
            "ARSession",
            "ARPlaneManager",
            "AROcclusionManager",
            "ARAnchorManager",
            "ARCameraManager",
            "XRDepthSubsystem",
            "XRMeshSubsystem",
            "VLA",
        ):
            self.assertNotIn(forbidden, scene)

        preparation = read(ROOT / "scripts/prepare_reachy_unity_presentation.sh")
        self.assertIn("ReachyPresentationPipeline.BuildFromCommandLine", preparation)
        self.assertIn("ReachyMiniPresentation.prefab", preparation)
        self.assertIn("ReachyMiniPresentation.unity", preparation)

    def test_shipping_bootstrap_rejects_movable_presentation_camera(self) -> None:
        app = ROOT / "Assets/ReachyMini/Runtime/Application"
        bootstrap = read(app / "ReachyMainScreenBootstrap.cs")
        composition = read(app / "ReachySettingsApplicationCompositionProvider.cs")
        self.assertIn('"fixed_front_three_quarter"', bootstrap)
        self.assertIn("cameraMetadata.AcceptsUserNavigation", bootstrap)
        self.assertIn("ReachyProductionAuthoritativeRuntime", bootstrap)
        self.assertIn("ReachySettingsApplicationCompositionProvider", bootstrap)
        self.assertIn("requires the fixed presentation camera", composition)
        self.assertIn('"fixed-presentation-camera"', composition)
        self.assertNotIn("ARSession", bootstrap)
        self.assertNotIn("ARSession", composition)
        self.assertIn("existing.PresentationCamera != camera", bootstrap)
        self.assertIn("existing.GetComponent<ReachyApplicationHostBehaviour>() == null", bootstrap)
        self.assertIn(
            "existing.GetComponent<ReachySettingsApplicationCompositionProvider>() == null",
            bootstrap,
        )
        # An existing screen must not bypass the same camera/runtime policy.
        # Guard the ordering, not just the presence of the safety checks.
        self.assertLess(
            bootstrap.index("if (cameraMetadata == null ||"),
            bootstrap.index("ReachyMainScreen? existing ="),
            "Reusing an existing shell must not bypass fixed-camera validation",
        )
        self.assertLess(
            bootstrap.index("if (runtime == null)"),
            bootstrap.index("ReachyMainScreen? existing ="),
            "Reusing an existing shell must not bypass authoritative runtime validation",
        )

    def test_shipping_runtime_has_no_deferred_ar_depth_or_scene_entrypoints(self) -> None:
        # Scan shipping C# code, not merely the release guide. Level 1
        # reprojection is intentionally allowed; it is not depth reconstruction.
        root = ROOT / "Assets/ReachyMini/Runtime"
        self.assertTrue(root.is_dir())
        sources = sorted(root.rglob("*.cs"))
        self.assertTrue(sources, "Shipping runtime C# sources must be present")
        forbidden = (
            re.compile(
                r"\b(?:UnityEngine\.XR\.(?:ARFoundation|ARSubsystems|Interaction|OpenXR)"
                r"|Google\.ARCore)\b"
            ),
            re.compile(
                r"\b(?:AddComponent|GetComponent|FindAnyObjectByType)"
                r"\s*\(\s*typeof\s*\(\s*(?:ARSession|AROcclusionManager|ARPlaneManager"
                r"|ARAnchorManager|ARCameraManager|XRDepthSubsystem|XRMeshSubsystem)"
                r"\s*\)\s*\)"
            ),
            re.compile(
                r"(?m)^\s*using\s+"
                r"(?:UnityEngine\.XR\.(?:ARFoundation|ARSubsystems|Interaction|OpenXR)"
                r"|Google\.ARCore)(?:\.\w+)*\s*;"
            ),
            re.compile(
                r"\b(?:AddComponent|GetComponent|FindAnyObjectByType)"
                r"\s*<\s*(?:ARSession|AROcclusionManager|ARPlaneManager"
                r"|ARAnchorManager|ARCameraManager|XRDepthSubsystem"
                r"|XRMeshSubsystem)\s*>"
            ),
            re.compile(
                r"\bnew\s+(?:ARSession|AROcclusionManager|ARPlaneManager"
                r"|ARAnchorManager|ARCameraManager|XRDepthSubsystem"
                r"|XRMeshSubsystem)\s*\("
            ),
        )
        violations = [
            f"{source.relative_to(ROOT)}: {pattern.pattern}"
            for source in sources
            for pattern in forbidden
            if pattern.search(read(source))
        ]
        self.assertEqual(
            [],
            violations,
            "Deferred AR/XR/depth/scene components cannot be enabled in shipping runtime",
        )

    def test_model_fidelity_audit_is_uncalibrated_geometric_baseline(self) -> None:
        audit = json.loads(read(ROOT / "models/reachy-mini/model-parameter-audit.json"))
        fidelity = audit["fidelity"]
        diagnostics = audit["diagnostics"]
        self.assertEqual("upstream_geometric_baseline", fidelity["profile_id"])
        self.assertEqual("geometric_baseline", fidelity["classification"])
        self.assertFalse(fidelity["calibrated"])
        self.assertFalse(fidelity["may_be_labeled_calibrated"])
        self.assertEqual("uncalibrated_upstream_baseline", fidelity["diagnostics_status"])
        self.assertEqual("geometric_baseline", diagnostics["classification"])
        self.assertFalse(diagnostics["calibrated"])

    def test_shipping_fidelity_defaults_do_not_claim_physical_calibration(self) -> None:
        settings = read(ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsState.cs")
        metadata = read(ROOT / "Assets/ReachyMini/Runtime/Core/ProjectMetadata.cs")
        self.assertIn('"Uncalibrated"', settings)
        self.assertIn("ReachySimulationFidelity.Standard", settings)
        self.assertIn(
            "public const SimulationFidelity InitialFidelity = SimulationFidelity.Unavailable;",
            metadata,
        )

    def test_release_status_has_no_premature_fidelity_promotion(self) -> None:
        status = read(STATUS)
        self.assertIn("No final release label is assigned", status)
        self.assertIn("No calibrated twin label is allowed", status)
        self.assertIn("held-out physical evidence", status)
        self.assertIn("No final release tag", status)


if __name__ == "__main__":
    unittest.main()

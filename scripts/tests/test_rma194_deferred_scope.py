#!/usr/bin/env python3
"""RMA-194 fail-closed production exclusion for explicitly deferred features."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets/ReachyMini/Runtime"
ANDROID = ROOT / "Assets/Plugins/Android"
JAVA = ROOT / "android-plugin/src/main"
GUIDE = ROOT / "docs/ANDROID_RELEASE_GUIDE.md"

FORBIDDEN_PACKAGES = {
    "com.unity.xr.arfoundation",
    "com.unity.xr.arcore",
    "com.unity.xr.management",
    "com.unity.xr.interaction.toolkit",
}
FORBIDDEN_SYMBOLS = re.compile(
    r"\b(?:ARSession|ARPlaneManager|AROcclusionManager|ARCameraManager|"
    r"ARAnchorManager|ARTrackedImageManager|ARCore|DepthReprojection|"
    r"SceneReconstruction|ObserverOrbitController|ObserverPanController|"
    r"ObserverZoomController|VlaPolicy|VlaController)\b"
)
FORBIDDEN_IMPORTS = re.compile(
    r"(?m)^\s*(?:using|import)\s+"
    r"(?:UnityEngine\.XR\.ARFoundation|Google\.ARCore|"
    r"com\.google\.ar\.core)\b"
)


class Rma194DeferredProductionScope(unittest.TestCase):
    def test_shipping_package_dependencies_exclude_ar_xr(self) -> None:
        manifest = json.loads((ROOT / "Packages/manifest.json").read_text(encoding="utf-8"))
        dependencies = set(manifest["dependencies"])
        self.assertFalse(dependencies & FORBIDDEN_PACKAGES)

    def test_shipping_source_excludes_deferred_implementations(self) -> None:
        findings: list[str] = []
        for root in (RUNTIME, ANDROID, JAVA):
            self.assertTrue(root.is_dir(), f"Missing shipping source root: {root}")
            for path in root.rglob("*"):
                if path.suffix.lower() not in {".cs", ".java", ".kt"}:
                    continue
                source = path.read_text(encoding="utf-8")
                source = re.sub(r"(?m)^\s*//.*$", "", source)
                source = re.sub(r"/\*.*?\*/", "", source, flags=re.DOTALL)
                if FORBIDDEN_SYMBOLS.search(source) or FORBIDDEN_IMPORTS.search(source):
                    findings.append(str(path.relative_to(ROOT)))
        self.assertEqual([], findings, "Deferred production entry points detected")

    def test_android_manifests_exclude_arcore_requirements(self) -> None:
        manifests = list(ANDROID.rglob("AndroidManifest.xml"))
        self.assertTrue(manifests)
        for path in manifests:
            content = path.read_text(encoding="utf-8").lower()
            self.assertNotIn("com.google.ar.core", content, str(path))
            self.assertNotIn("android.hardware.camera.ar", content, str(path))

    def test_release_guide_does_not_advertise_deferred_features(self) -> None:
        guide = GUIDE.read_text(encoding="utf-8")
        self.assertIn("It is not depth reconstruction, AR scene reconstruction", guide)
        self.assertIn("or a movable observer camera", guide)
        self.assertIn("The release label must match the completion evidence", guide)


if __name__ == "__main__":
    unittest.main()

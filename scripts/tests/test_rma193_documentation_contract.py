#!/usr/bin/env python3
"""RMA-193 release-documentation regression contract."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "ANDROID_RELEASE_GUIDE.md"
README = ROOT / "README.md"


class Rma193DocumentationContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.guide = GUIDE.read_text(encoding="utf-8")
        cls.readme = README.read_text(encoding="utf-8")
        cls.guide_lower = cls.guide.lower()

    def test_readme_links_release_guide(self) -> None:
        self.assertIn("docs/ANDROID_RELEASE_GUIDE.md", self.readme)

    def test_required_release_sections_exist(self) -> None:
        for heading in (
            "## Build instructions",
            "## Android device requirements",
            "## Model installation/import",
            "## Provider configuration and BYOK warning",
            "## Calibration workflow",
            "## Fidelity-level explanation",
            "## Camera Level 1 approximation and missing-pixel behavior",
            "## Troubleshooting and diagnostic bundle instructions",
            "## Privacy and data-flow explanation",
        ):
            self.assertIn(heading, self.guide)

    def test_build_and_device_guidance_names_authoritative_paths(self) -> None:
        for token in (
            "toolchain.lock.json",
            "scripts/ci.sh --static-only",
            "scripts/build_unity_android.sh",
            "REACHY_ANDROID_SERIAL",
            "REACHY_PHYSICAL_DEVICE_SERIAL",
            "arm64-v8a",
            "API 26",
        ):
            self.assertIn(token, self.guide)

    def test_model_provider_calibration_privacy_fail_closed_language(self) -> None:
        for token in (
            "qwen3-0.6b-q4-k-m.local-llm.json",
            "not bundled",
            "bring-your-own-key",
            "Android Keystore",
            "privacy-boundary authorization",
            "fail closed",
            "unavailable/degraded",
            "placeholder constants",
        ):
            self.assertIn(token.lower(), self.guide_lower)

    def test_fidelity_camera_and_diagnostics_are_explicit(self) -> None:
        for token in (
            "Geometric baseline",
            "Dynamic baseline",
            "Servo-fidelity",
            "Calibrated twin",
            "Level 1",
            "missing, invalid, or out-of-view pixels",
            "redacted-by-default",
            "raw private media",
            "cloud API keys",
        ):
            self.assertIn(token.lower(), self.guide_lower)


if __name__ == "__main__":
    unittest.main()

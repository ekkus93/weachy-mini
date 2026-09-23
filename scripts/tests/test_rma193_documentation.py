#!/usr/bin/env python3
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "ANDROID_RELEASE_GUIDE.md"
README = ROOT / "README.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma193DocumentationContracts(unittest.TestCase):
    def test_release_guide_covers_every_rma193_topic(self) -> None:
        guide = read(GUIDE)
        for heading in (
            "## Build instructions",
            "## Android device requirements",
            "## Local model installation and import",
            "## Provider configuration and BYOK warning",
            "## Calibration workflow",
            "## Fidelity levels",
            "## Camera Level 1 approximation and missing pixels",
            "## Troubleshooting and diagnostic bundles",
            "## Privacy and data flow",
        ):
            self.assertIn(heading, guide)

    def test_release_guide_preserves_fail_closed_and_current_limitations(self) -> None:
        guide = read(GUIDE)
        for phrase in (
            "INSTALL**, **IMPORT**, **SELECT**, and **DELETE** as unavailable",
            "current in-app **Camera calibration** action is still unavailable",
            "must not silently switch a local workload to a network provider",
            "Pixels outside valid source coverage are **missing**, not invented",
            "credentials, raw audio, raw images/media, transcripts, and conversation text are excluded",
            "Android Keystore-backed secret store",
            "deterministic fixtures and estimated profiles do not qualify",
        ):
            self.assertIn(phrase, guide)

    def test_release_guide_links_authoritative_detail_documents(self) -> None:
        guide = read(GUIDE)
        for relative in (
            "BUILDING.md",
            "architecture/LOCAL_MODEL_PACKAGE_MANAGEMENT.md",
            "architecture/CALIBRATION_DATA_AND_CAPTURE.md",
            "architecture/CAMERA_VALID_COVERAGE_POLICY.md",
            "RMA_146_EXPLICIT_NO_FALLBACK_POLICY_SPEC_2026-08-11.md",
            "RMA_161_CREDENTIAL_LIFECYCLE_SPEC_2026-08-13.md",
            "RMA_170_STRUCTURED_LOGGING_SPEC_2026-08-14.md",
            "RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md",
        ):
            self.assertIn(relative, guide)
            self.assertTrue((GUIDE.parent / relative).exists(), relative)

    def test_readme_points_to_release_guide(self) -> None:
        self.assertIn("docs/ANDROID_RELEASE_GUIDE.md", read(README))


if __name__ == "__main__":
    unittest.main()

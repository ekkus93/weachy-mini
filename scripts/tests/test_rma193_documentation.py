#!/usr/bin/env python3
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "ANDROID_OPERATOR_GUIDE.md"
BUILDING = ROOT / "docs" / "BUILDING.md"
MANIFEST = ROOT / "models" / "manifests" / "qwen3-0.6b-q4-k-m.local-llm.json"

REQUIRED_LINKS = (
    "BUILDING.md",
    "architecture/LOCAL_MODEL_PACKAGE_MANAGEMENT.md",
    "architecture/LOCAL_LLM_MODEL_MANIFEST.md",
    "architecture/SETTINGS_ARCHITECTURE.md",
    "architecture/CALIBRATION_DATA_AND_CAPTURE.md",
    "architecture/PHYSICAL_CALIBRATION_AND_PROFILE_APPROVAL.md",
    "operations/RMA_074_PHYSICAL_CALIBRATION_RUNBOOK.md",
    "model-parameter-audit.md",
    "architecture/CAMERA_REPROJECTION_COORDINATE_SYSTEMS.md",
    "architecture/CAMERA_VALID_COVERAGE_POLICY.md",
    "RMA_171_DIAGNOSTICS_SCREEN_SPEC_2026-08-14.md",
    "RMA_172_DIAGNOSTIC_BUNDLE_EXPORT_SPEC_2026-08-14.md",
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma193DocumentationContracts(unittest.TestCase):
    def test_guide_covers_all_nine_roadmap_topics(self) -> None:
        guide = read(GUIDE)
        for heading in (
            "## Build instructions",
            "## Android device requirements",
            "## Model installation and import",
            "## Provider configuration and BYOK warning",
            "## Calibration workflow",
            "## Fidelity-level explanation",
            "## Camera Level 1 approximation and missing-pixel behavior",
            "## Troubleshooting and diagnostic bundle instructions",
            "## Privacy and data-flow explanation",
        ):
            self.assertIn(heading, guide)

    def test_authoritative_links_resolve(self) -> None:
        guide = read(GUIDE)
        for relative in REQUIRED_LINKS:
            self.assertIn(relative, guide)
            self.assertTrue(
                (GUIDE.parent / relative).resolve().is_file(),
                f"missing documentation target: {relative}",
            )

    def test_android_floor_is_not_confused_with_api26_feasibility_build(self) -> None:
        guide = read(GUIDE)
        building = read(BUILDING)
        self.assertIn("provisional API 31 minimum", building)
        self.assertIn("API 31", guide)
        self.assertIn("API 26", guide)
        self.assertIn("do **not** lower the normal application minimum", guide)

    def test_selected_model_documentation_matches_manifest_and_does_not_claim_bundling(self) -> None:
        manifest = json.loads(read(MANIFEST))
        guide = read(GUIDE)
        identity = manifest["identity"]
        self.assertIn(identity["display_name"], guide)
        self.assertIn(identity["license_id"], guide)
        self.assertIn("GGUF is not bundled", guide)
        self.assertIn("IMPORT — UNAVAILABLE", guide)
        self.assertIn("not** permission to\nsilently download", guide)

    def test_byok_privacy_and_keystore_warnings_are_explicit(self) -> None:
        guide = read(GUIDE)
        self.assertIn("bring-your-own-key (BYOK)", guide)
        self.assertIn("Android-Keystore-backed secret store", guide)
        self.assertIn("must not\nsilently substitute another network provider", guide)
        self.assertIn("There is no acceptable silent privacy fallback", guide)

    def test_physical_reachy_is_optional_but_calibration_claims_require_evidence(self) -> None:
        guide = read(GUIDE)
        self.assertIn("physical Reachy Mini is **not required**", guide)
        self.assertIn("optional extension", guide)
        self.assertIn("Never label a fixture, estimate, placeholder", guide)
        self.assertIn("uncalibrated upstream geometric baseline", guide)

    def test_level1_missing_pixels_and_coverage_are_fail_closed(self) -> None:
        guide = read(GUIDE)
        self.assertIn("rotation-only reprojection approximation", guide)
        self.assertIn("validity mask", guide)
        self.assertIn("must not reuse stale pixels", guide)
        self.assertIn("fabricated image content", guide)

    def test_diagnostics_export_is_bounded_redacted_and_user_triggered(self) -> None:
        guide = read(GUIDE)
        self.assertIn("EXPORT REDACTED BUNDLE", guide)
        self.assertIn("bounded and user-triggered", guide)
        self.assertIn("excludes credentials, raw audio, raw", guide)
        self.assertIn("conversation text", guide)


if __name__ == "__main__":
    unittest.main()

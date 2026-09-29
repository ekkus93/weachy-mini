import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
README = ROOT / "README.md"
BUILDING = ROOT / "docs/BUILDING.md"
OPERATOR_GUIDE = ROOT / "docs/ANDROID_RELEASE_OPERATOR_GUIDE.md"
ASSET_POLICY = ROOT / "docs/ASSET_POLICY.md"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma193DocumentationReviewTests(unittest.TestCase):
    def test_readme_links_authoritative_docs_and_release_hub(self) -> None:
        readme = read(README)
        for token in (
            "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md",
            "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md",
            "docs/BUILDING.md",
            "docs/ASSET_POLICY.md",
            "third_party/THIRD_PARTY_NOTICES.md",
            "third_party/inventory.json",
        ):
            self.assertIn(token, readme)
        self.assertIn("Android ARM64", readme)
        self.assertIn("IL2CPP", readme)

    def test_building_doc_has_reproducible_toolchain_and_commands(self) -> None:
        building = read(BUILDING)
        for token in (
            "toolchain.lock.json",
            "python3 scripts/verify_toolchain.py",
            "./scripts/ci.sh --static-only",
            "ruff check scripts",
            "shellcheck scripts/*.sh",
            "actionlint",
            "./scripts/build_native.sh",
            "dotnet run",
            "scripts/build_unity_android.sh development",
            "scripts/build_unity_android.sh release",
            "IL2CPP",
            "ARM64",
        ):
            self.assertIn(token, building)
        self.assertNotIn("dotnet test silently reports zero tests and is acceptable", building)
        self.assertIn("dotnet test` silently reports zero tests", building)

    def test_operator_guide_covers_every_rma193_topic(self) -> None:
        guide = read(OPERATOR_GUIDE)
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
        for token in (
            "conversation text",
            "camera frames",
            "Android Keystore-backed",
            "NoCalibration",
            "Geometric baseline",
            "Dynamic baseline",
            "Servo-fidelity",
            "Calibrated twin",
            "Level 1 camera reprojection",
            "validity mask",
            "EXPORT REDACTED BUNDLE",
            "raw media",
            "transcripts",
            "silent fallbacks",
        ):
            self.assertIn(token, guide)

    def test_virtual_release_docs_do_not_require_physical_reachy_hardware(self) -> None:
        guide = read(OPERATOR_GUIDE)
        todo = read(TODO)
        self.assertIn(
            "optional physical Reachy Mini hardware is not required",
            guide,
        )
        self.assertIn("lack of physical Reachy Mini hardware", todo)
        self.assertIn("must not become a release blocker", todo)
        self.assertNotIn("physical Reachy Mini hardware is required", guide)
        self.assertNotIn("requires physical Reachy Mini hardware", guide)

    def test_asset_policy_documents_noncommitted_sensitive_and_large_inputs(self) -> None:
        policy = read(ASSET_POLICY)
        for token in (
            "Reachy MJCF/mesh assets",
            "pinned source revision",
            "records provenance",
            "API keys, tokens, keystores",
            "model binaries",
            "raw camera frames",
            "microphone recordings",
            "raw calibration captures",
            "must fail visibly",
        ):
            self.assertIn(token, policy)


if __name__ == "__main__":
    unittest.main()

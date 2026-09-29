import json
import re
import unittest
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
THIRD_PARTY = ROOT / "third_party"

REQUIRED_INVENTORY_FIELDS = {
    "id",
    "name",
    "owner",
    "license",
    "source_url",
    "source_revision",
    "status",
    "modification_status",
    "redistribution_status",
    "required_notice",
}

EXPECTED_INVENTORY_IDS = {
    "unity-editor",
    "mujoco",
    "reachy-mini-software",
    "reachy-mini-hardware-assets",
    "llama-cpp",
    "local-llm-candidate",
    "androidx-camerax",
    "google-mlkit-face-detection",
    "google-mlkit-selfie-segmentation",
    "rma111-public-domain-face-fixture",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def load_inventory() -> dict[str, Any]:
    return json.loads(read(THIRD_PARTY / "inventory.json"))


class Rma192LicenseAttributionReviewTests(unittest.TestCase):
    def test_root_license_readme_notices_and_inventory_exist(self) -> None:
        license_text = read(ROOT / "LICENSE")
        readme = read(ROOT / "README.md")
        notices = read(THIRD_PARTY / "THIRD_PARTY_NOTICES.md")
        inventory = load_inventory()

        self.assertIn("Apache License", license_text)
        self.assertIn("Version 2.0", license_text)
        self.assertIn("third_party/THIRD_PARTY_NOTICES.md", readme)
        self.assertIn("third_party/inventory.json", readme)
        self.assertIn("Reachy-derived hardware/model assets", readme)
        self.assertIn("retain their own licenses", readme)
        self.assertEqual(1, inventory["schema_version"])
        self.assertIsInstance(inventory["entries"], list)
        self.assertGreaterEqual(len(inventory["entries"]), len(EXPECTED_INVENTORY_IDS))
        self.assertIn("complete license text", notices)
        self.assertIn("not endorsed", notices.lower())

    def test_inventory_entries_are_complete_unique_and_notice_bound(self) -> None:
        inventory = load_inventory()
        notices = read(THIRD_PARTY / "THIRD_PARTY_NOTICES.md")
        entries = inventory["entries"]
        ids = [entry["id"] for entry in entries]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertTrue(EXPECTED_INVENTORY_IDS.issubset(set(ids)))

        for entry in entries:
            missing = REQUIRED_INVENTORY_FIELDS - set(entry)
            self.assertEqual(set(), missing, entry)
            for field in REQUIRED_INVENTORY_FIELDS - {"source_revision"}:
                self.assertIsInstance(entry[field], str, entry["id"])
                self.assertTrue(entry[field].strip(), (entry["id"], field))
            if entry["status"] not in {"developer_tool", "candidate"}:
                anchor_candidates = {
                    entry["name"],
                    entry["id"],
                    entry["license"],
                    str(entry["source_revision"]),
                }
                self.assertTrue(
                    any(anchor and anchor in notices for anchor in anchor_candidates),
                    f"inventory entry lacks a repository notice anchor: {entry['id']}",
                )

    def test_reachy_derived_assets_remain_license_blocked_until_audit(self) -> None:
        inventory = {entry["id"]: entry for entry in load_inventory()["entries"]}
        reachy_assets = inventory["reachy-mini-hardware-assets"]
        notices = read(THIRD_PARTY / "THIRD_PARTY_NOTICES.md")
        asset_policy = read(ROOT / "docs/ASSET_POLICY.md")

        self.assertEqual("CC-BY-NC-SA", reachy_assets["license"])
        self.assertEqual(
            "blocked_pending_asset_audit",
            reachy_assets["redistribution_status"],
        )
        for token in ("Attribution", "noncommercial", "ShareAlike", "modification notice"):
            self.assertIn(token, reachy_assets["required_notice"])
        self.assertIn("noncommercial", notices.lower())
        self.assertIn("ShareAlike", notices)
        self.assertIn("not endorsed", notices.lower())
        self.assertIn("pinned source revision", asset_policy)
        self.assertIn("records provenance", asset_policy)
        self.assertIn("must fail visibly", asset_policy)

    def test_offline_app_notices_screen_is_wired_to_license_notices(self) -> None:
        settings_sections = read(APP / "ReachyMainScreen.SettingsSections.cs")
        app_sources = "\n".join(
            read(path)
            for path in sorted(APP.glob("*.cs"))
        )
        for token in (
            "DrawLicenseSettings",
            "OFFLINE NOTICES",
            "ReachyLicenseNotice",
            "GetLicenseNotices",
            "Component",
            "Attribution",
            "LicenseReference",
        ):
            self.assertIn(token, settings_sections)
        self.assertIn("License", app_sources)
        self.assertIn("Attribution", app_sources)

    def test_local_model_distribution_stays_blocked_until_selection_review(self) -> None:
        inventory = {entry["id"]: entry for entry in load_inventory()["entries"]}
        local_model = inventory["local-llm-candidate"]
        notices = read(THIRD_PARTY / "THIRD_PARTY_NOTICES.md")
        asset_policy = read(ROOT / "docs/ASSET_POLICY.md")
        self.assertEqual("Not selected", local_model["license"])
        self.assertEqual("candidate", local_model["status"])
        self.assertEqual("blocked_pending_selection", local_model["redistribution_status"])
        self.assertIn("no model is selected or distributed", notices.lower())
        self.assertIn("GGUF", asset_policy)
        self.assertIn("model binaries", asset_policy)
        self.assertIn("do not package candidate binaries", local_model["required_notice"].lower())

    def test_no_positive_endorsement_claims_are_present(self) -> None:
        scanned_text = "\n".join(
            read(path)
            for root in (ROOT / "README.md", THIRD_PARTY / "THIRD_PARTY_NOTICES.md")
            for path in ([root] if root.is_file() else sorted(root.glob("*.md")))
        ).lower()
        prohibited_patterns = [
            r"official\s+reachy\s+mini\s+app",
            r"endorsed\s+by\s+pollen",
            r"endorsed\s+by\s+hugging\s*face",
            r"endorsed\s+by\s+google",
            r"endorsed\s+by\s+openai",
        ]
        violations = [
            pattern for pattern in prohibited_patterns
            if re.search(pattern, scanned_text)
        ]
        self.assertEqual([], violations)
        self.assertIn("not endorsed", scanned_text)


if __name__ == "__main__":
    unittest.main()

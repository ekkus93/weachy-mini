#!/usr/bin/env python3
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INVENTORY = ROOT / "third_party" / "inventory.json"
NOTICES = ROOT / "third_party" / "THIRD_PARTY_NOTICES.md"
MODEL = ROOT / "models" / "manifests" / "qwen3-0.6b-q4-k-m.local-llm.json"
REACHY_LOCK = ROOT / "third_party" / "reachy-mini-source.lock.json"
LABELS = (
    ROOT
    / "Assets"
    / "ReachyMini"
    / "Runtime"
    / "Core"
    / "Application"
    / "ReachySettingsStateStore.Labels.cs"
)


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def entry(inventory: dict, entry_id: str) -> dict:
    matches = [item for item in inventory["entries"] if item["id"] == entry_id]
    if len(matches) != 1:
        raise AssertionError(f"expected one inventory entry {entry_id!r}, found {len(matches)}")
    return matches[0]


class Rma192LicenseReviewContracts(unittest.TestCase):
    def test_selected_model_inventory_matches_manifest(self) -> None:
        inventory = load(INVENTORY)
        manifest = load(MODEL)
        selected = entry(inventory, "selected-local-llm")
        identity = manifest["identity"]

        self.assertEqual(identity["display_name"], selected["name"])
        self.assertEqual(identity["license_id"], selected["license"])
        self.assertEqual(identity["source_uri"], selected["source_url"])
        self.assertEqual(identity["source_revision"], selected["source_revision"])
        self.assertEqual("not_redistributed", selected["redistribution_status"])
        self.assertIn("not vendored", selected["modification_status"])
        self.assertIn("does not authorize automatic download", selected["required_notice"])

    def test_reachy_asset_inventory_matches_pinned_source_and_records_modification(self) -> None:
        inventory = load(INVENTORY)
        lock = load(REACHY_LOCK)
        assets = entry(inventory, "reachy-mini-hardware-assets")

        self.assertEqual(lock["commit"], assets["source_revision"])
        self.assertEqual(lock["asset_license"], assets["license"])
        self.assertEqual("packaged", assets["status"])
        self.assertEqual(
            "requires_cc_by_nc_sa_compliance",
            assets["redistribution_status"],
        )
        self.assertIn("converted", assets["modification_status"])
        self.assertIn("Unity presentation assets", assets["modification_status"])
        for token in ("Attribution", "noncommercial", "ShareAlike", "modification"):
            self.assertIn(token.lower(), assets["required_notice"].lower())

    def test_repository_notices_describe_selected_model_and_reachy_modifications(self) -> None:
        notices = NOTICES.read_text(encoding="utf-8")
        self.assertIn("Qwen3 0.6B Q4_K_M", notices)
        self.assertIn("8e42d41f70cb6c571f58c3f31bd9287b372d97cc", notices)
        self.assertIn("Apache-2.0", notices)
        self.assertIn("not vendored or bundled", notices)
        self.assertIn("generated Unity presentation assets", notices)
        self.assertIn("ShareAlike", notices)

    def test_offline_app_notice_matches_selected_model_and_asset_obligations(self) -> None:
        labels = LABELS.read_text(encoding="utf-8")
        self.assertIn('"Qwen3 0.6B Q4_K_M"', labels)
        self.assertIn('"Qwen"', labels)
        self.assertIn("Apache-2.0", labels)
        self.assertIn("not bundled", labels)
        self.assertIn("generated Unity presentation assets", labels)
        self.assertIn("CC BY-NC-SA", labels)
        self.assertIn("ShareAlike", labels)

    def test_selected_model_manifest_does_not_imply_bundling_or_automatic_download(self) -> None:
        manifest = load(MODEL)
        self.assertFalse(manifest["runtime"]["requires_network_access"])
        self.assertEqual(
            "qwen3/qwen3-0.6b-q4_k_m.gguf",
            manifest["artifact"]["relative_path"],
        )
        notices = NOTICES.read_text(encoding="utf-8")
        self.assertIn("manifest does not authorize automatic download", notices)


if __name__ == "__main__":
    unittest.main()

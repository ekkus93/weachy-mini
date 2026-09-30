import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
INVENTORY_PATH = ROOT / "third_party" / "inventory.json"
NOTICES_PATH = ROOT / "third_party" / "THIRD_PARTY_NOTICES.md"
MODEL_MANIFEST_PATH = (
    ROOT / "models" / "manifests" / "qwen3-0.6b-q4-k-m.local-llm.json"
)


def _load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _inventory_entry(entry_id: str) -> dict:
    inventory = _load_json(INVENTORY_PATH)
    matches = [entry for entry in inventory["entries"] if entry["id"] == entry_id]
    assert len(matches) == 1, f"expected exactly one inventory entry for {entry_id}"
    return matches[0]


class Rma192LicenseReconciliationTests(unittest.TestCase):
    def test_selected_local_model_inventory_matches_authoritative_manifest(self) -> None:
        manifest = _load_json(MODEL_MANIFEST_PATH)
        identity = manifest["identity"]
        entry = _inventory_entry("local-llm-candidate")

        self.assertEqual(identity["display_name"], entry["name"])
        self.assertEqual(identity["license_id"], entry["license"])
        self.assertEqual(identity["source_uri"], entry["source_url"])
        self.assertEqual(identity["source_revision"], entry["source_revision"])
        self.assertEqual("selected_external_model", entry["status"])
        self.assertEqual(
            "not_redistributed_by_repository",
            entry["redistribution_status"],
        )
        self.assertIn("sha-256", entry["required_notice"].lower())
        self.assertIn("not", entry["redistribution_status"])

    def test_selected_local_model_notice_is_not_stale_candidate_copy(self) -> None:
        manifest = _load_json(MODEL_MANIFEST_PATH)
        identity = manifest["identity"]
        artifact = manifest["artifact"]
        notices = NOTICES_PATH.read_text(encoding="utf-8")

        self.assertIn(identity["display_name"], notices)
        self.assertIn(identity["source_revision"], notices)
        self.assertIn(identity["license_id"], notices)
        self.assertIn(artifact["sha256"], notices)
        self.assertNotIn("candidate only; no model is selected", notices)
        self.assertIn("not bundled", notices)

    def test_reachy_inventory_no_longer_claims_imported_assets_are_planned(self) -> None:
        software = _inventory_entry("reachy-mini-software")
        hardware = _inventory_entry("reachy-mini-hardware-assets")

        self.assertEqual("imported_from_pinned_source", software["status"])
        self.assertEqual("imported_from_pinned_source", hardware["status"])
        self.assertIn("provenance", software["required_notice"].lower())
        self.assertIn("provenance", hardware["required_notice"].lower())
        self.assertIn("attribution", hardware["required_notice"].lower())
        self.assertIn("sharealike", hardware["required_notice"].lower())


if __name__ == "__main__":
    unittest.main()

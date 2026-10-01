import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
INVENTORY_PATH = ROOT / "third_party" / "inventory.json"
NOTICES_PATH = ROOT / "third_party" / "THIRD_PARTY_NOTICES.md"
LABELS_PATH = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStateStore.Labels.cs"
SCREEN_PATH = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.SettingsSections.cs"
MODEL_MANIFEST_PATH = (
    ROOT / "models" / "manifests" / "qwen3-0.6b-q4-k-m.local-llm.json"
)
RMA012_CONTRACT = ROOT / "scripts" / "tests" / "test_rma012_licenses.py"


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
        self.assertEqual("not_redistributed_by_repository", entry["redistribution_status"])
        self.assertIn("sha-256", entry["required_notice"].lower())

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

    def test_reachy_inventory_records_import_provenance_and_sharealike(self) -> None:
        software = _inventory_entry("reachy-mini-software")
        hardware = _inventory_entry("reachy-mini-hardware-assets")

        self.assertEqual("imported_from_pinned_source", software["status"])
        self.assertEqual("imported_from_pinned_source", hardware["status"])
        self.assertIn("provenance", software["required_notice"].lower())
        self.assertIn("provenance", hardware["required_notice"].lower())
        self.assertIn("attribution", hardware["required_notice"].lower())
        self.assertIn("sharealike", hardware["required_notice"].lower())
        self.assertIn("noncommercial", hardware["required_notice"].lower())
        self.assertIn("modification notice", hardware["required_notice"].lower())

    def test_repository_and_offline_app_notices_cover_inventory_and_no_endorsement(self) -> None:
        inventory = _load_json(INVENTORY_PATH)
        labels = LABELS_PATH.read_text(encoding="utf-8")
        screen = SCREEN_PATH.read_text(encoding="utf-8")
        notices = NOTICES_PATH.read_text(encoding="utf-8")
        rma012 = RMA012_CONTRACT.read_text(encoding="utf-8")

        for entry in inventory["entries"]:
            self.assertIn(entry["name"], labels)
        for token in (
            "Unofficial community project",
            "not endorsed by Pollen Robotics",
            "free and noncommercial",
        ):
            self.assertIn(token, labels)
        self.assertIn("not endorsed", notices.lower())
        self.assertIn("free and noncommercial", notices.lower())
        self.assertIn("ReachySettingsStateStore.GetLicenseNotices()", screen)
        self.assertIn("test_in_app_notice_components_cover_inventory", rma012)
        self.assertIn("test_project_status_is_explicit_and_offline", rma012)


if __name__ == "__main__":
    unittest.main(verbosity=2)

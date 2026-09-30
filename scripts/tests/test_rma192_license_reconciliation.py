import json
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


def test_selected_local_model_inventory_matches_authoritative_manifest() -> None:
    manifest = _load_json(MODEL_MANIFEST_PATH)
    identity = manifest["identity"]
    artifact = manifest["artifact"]
    entry = _inventory_entry("local-llm-candidate")

    assert entry["name"] == identity["display_name"]
    assert entry["license"] == identity["license_id"]
    assert entry["source_url"] == identity["source_uri"]
    assert entry["source_revision"] == identity["source_revision"]
    assert entry["status"] == "selected_external_model"
    assert entry["redistribution_status"] == "not_redistributed_by_repository"
    assert artifact["sha256"] in entry["required_notice"]


def test_selected_local_model_notice_is_not_stale_candidate_copy() -> None:
    manifest = _load_json(MODEL_MANIFEST_PATH)
    identity = manifest["identity"]
    artifact = manifest["artifact"]
    notices = NOTICES_PATH.read_text(encoding="utf-8")

    assert identity["display_name"] in notices
    assert identity["source_revision"] in notices
    assert identity["license_id"] in notices
    assert artifact["sha256"] in notices
    assert "candidate only; no model is selected" not in notices
    assert "not bundled" in notices


def test_reachy_inventory_no_longer_claims_imported_assets_are_planned() -> None:
    software = _inventory_entry("reachy-mini-software")
    hardware = _inventory_entry("reachy-mini-hardware-assets")

    assert software["status"] == "imported_from_pinned_source"
    assert hardware["status"] == "imported_from_pinned_source"
    assert "provenance" in software["required_notice"].lower()
    assert "provenance" in hardware["required_notice"].lower()
    assert "attribution" in hardware["required_notice"].lower()
    assert "sharealike" in hardware["required_notice"].lower()

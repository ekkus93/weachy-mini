#!/usr/bin/env python3
"""RMA-192 repository-wide license and attribution reconciliation contracts."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INVENTORY = ROOT / "third_party/inventory.json"
NOTICES = ROOT / "third_party/THIRD_PARTY_NOTICES.md"
LABELS = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStateStore.Labels.cs"
LOCAL_MODEL_UI = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.SettingsSections.cs"
MODEL_MANIFEST = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"
REACHY_LOCK = ROOT / "third_party/reachy-mini-source.lock.json"
IMPORTER = ROOT / "scripts/import_reachy_assets.py"
PREPARER = ROOT / "scripts/prepare_reachy_unity_assets.py"
ASSET_POLICY = ROOT / "docs/ASSET_POLICY.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma192LicenseAttributionReview(unittest.TestCase):
    def setUp(self) -> None:
        self.inventory = json.loads(read(INVENTORY))
        self.entries = {entry["id"]: entry for entry in self.inventory["entries"]}

    def test_packaged_inventory_matches_current_runtime_state(self) -> None:
        for entry_id in (
            "mujoco",
            "reachy-mini-hardware-assets",
            "llama-cpp",
            "androidx-camerax",
            "google-mlkit-face-detection",
            "google-mlkit-selfie-segmentation",
        ):
            entry = self.entries[entry_id]
            self.assertEqual("packaged", entry["status"], entry_id)
            self.assertTrue(entry["source_revision"], entry_id)
            self.assertNotEqual("unknown", entry["redistribution_status"], entry_id)

        self.assertEqual(
            "deterministic_build_source",
            self.entries["reachy-mini-software"]["status"],
        )
        reachy_assets = self.entries["reachy-mini-hardware-assets"]
        self.assertEqual("CC-BY-NC-SA", reachy_assets["license"])
        self.assertIn("coordinate_converted_derivative", reachy_assets["modification_status"])
        self.assertIn("ShareAlike", reachy_assets["required_notice"])

    def test_generated_reachy_derivative_carries_attribution_and_license_evidence(self) -> None:
        lock = json.loads(read(REACHY_LOCK))
        self.assertEqual("CC-BY-NC-SA", lock["asset_license"])
        self.assertEqual("Apache-2.0", lock["software_license"])

        importer = read(IMPORTER)
        for token in (
            '"UPSTREAM_LICENSE"',
            '"ATTRIBUTION.md"',
            '"PROVENANCE.json"',
            '"content_modified": False',
            "Hardware/model asset license:",
        ):
            self.assertIn(token, importer)

        preparer = read(PREPARER)
        for token in (
            'EXPECTED_REACHY_ASSET_LICENSE = "CC-BY-NC-SA"',
            'checked_relative_file(source_root, "ATTRIBUTION.md")',
            'checked_relative_file(source_root, "UPSTREAM_LICENSE")',
            '"modified_derivative": True',
            "modified derivative",
            "noncommercial, ShareAlike, and modification-notice",
            'manifest["licensing"] = write_derivative_notices',
        ):
            self.assertIn(token, preparer)

    def test_offline_notices_cover_inventory_and_no_endorsement(self) -> None:
        labels = read(LABELS)
        notices = read(NOTICES)
        for entry in self.inventory["entries"]:
            self.assertIn(f'"{entry["name"]}"', labels, entry["id"])
        for token in (
            "coordinate-converted",
            "ShareAlike",
            "Unofficial community project",
            "not endorsed by Pollen Robotics",
        ):
            self.assertIn(token, labels)
        self.assertIn("free and noncommercial", labels.lower())
        self.assertIn("not endorsed", notices.lower())
        self.assertIn("free and noncommercial", notices.lower())

    def test_selected_model_inventory_is_bound_to_manifest_and_not_bundled(self) -> None:
        manifest = json.loads(read(MODEL_MANIFEST))
        selected = self.entries["local-llm-selected"]
        identity = manifest["identity"]
        artifact = manifest["artifact"]

        self.assertEqual(identity["display_name"], selected["name"])
        self.assertEqual(identity["source_uri"], selected["source_url"])
        self.assertEqual(identity["source_revision"], selected["source_revision"])
        self.assertEqual(identity["license_id"], selected["license"])
        self.assertEqual("selected_not_bundled", selected["status"])
        self.assertIn("not committed or bundled", selected["required_notice"].lower())

        self.assertEqual(396704416, artifact["file_size_bytes"])
        self.assertEqual(
            "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e",
            artifact["sha256"],
        )
        notices = read(NOTICES)
        self.assertIn(identity["source_revision"], notices)
        self.assertIn(artifact["sha256"], notices)
        self.assertIn("Apache-2.0", notices)
        self.assertNotIn("candidate only; no model is selected", notices)

    def test_shipping_model_acquisition_remains_explicit_and_no_binary_is_committed(self) -> None:
        ui = read(LOCAL_MODEL_UI)
        for token in (
            "INSTALL — UNAVAILABLE",
            "IMPORT — UNAVAILABLE",
            "SELECT — UNAVAILABLE",
            "DELETE — UNAVAILABLE",
        ):
            self.assertIn(token, ui)
        policy = read(ASSET_POLICY)
        self.assertIn(
            "GGUF, safetensors, ONNX, TFLite, or other model binaries",
            policy,
        )


if __name__ == "__main__":
    unittest.main()

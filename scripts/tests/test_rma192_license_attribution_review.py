#!/usr/bin/env python3
"""RMA-192 reproducible license and attribution release-review gate."""

from __future__ import annotations

import json
import unittest
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
INVENTORY = ROOT / "third_party/inventory.json"
NOTICES = ROOT / "third_party/THIRD_PARTY_NOTICES.md"
RMA082 = ROOT / "managed/ReachyMini.Application.Tests/Rma082SettingsStateTests.cs"
MODEL_MANIFEST = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"

PACKAGED_STATUSES = {
    "vendored",
    "downloaded",
    "packaged",
    "packaged_dependency",
    "packaged_beta_dependency",
    "packaged_test_fixture",
}
REACHY_TERMS = ("reachy", "pollen", "hardware", "model asset", "mjcf")


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def load_json(path: Path) -> Any:
    return json.loads(read(path))


def flatten_strings(value: Any) -> list[str]:
    strings: list[str] = []
    if isinstance(value, str):
        strings.append(value)
    elif isinstance(value, dict):
        for nested in value.values():
            strings.extend(flatten_strings(nested))
    elif isinstance(value, list):
        for nested in value:
            strings.extend(flatten_strings(nested))
    return strings


class Rma192LicenseAttributionReview(unittest.TestCase):
    def test_packaged_dependencies_are_inventory_backed_and_noticeable(self) -> None:
        inventory = load_json(INVENTORY)
        notices = read(NOTICES).lower()
        entries = inventory["entries"]
        packaged = [entry for entry in entries if entry.get("status") in PACKAGED_STATUSES]
        self.assertGreater(len(packaged), 0)
        for entry in packaged:
            with self.subTest(entry=entry["id"]):
                self.assertTrue(entry.get("source_revision"))
                self.assertNotEqual("unknown", entry.get("redistribution_status"))
                self.assertTrue(entry.get("required_notice"))
                self.assertIn(entry["name"].lower().split()[0], notices)
                license_token = entry["license"].lower().split()[0].split("-")[0]
                self.assertIn(license_token, notices)

    def test_repository_and_app_notice_sets_cover_core_release_obligations(self) -> None:
        notices = read(NOTICES)
        managed_tests = read(RMA082)
        for token in (
            "MuJoCo",
            "Apache-2.0",
            "Reachy Mini hardware and model assets",
            "CC BY-NC-SA",
            "Pollen",
            "Unofficial",
            "not endorsed",
            "free and noncommercial",
        ):
            self.assertIn(token, notices + managed_tests)
        self.assertIn("Equal(12, notices.Length", managed_tests)
        self.assertIn("FindNotice(notices, \"MuJoCo\")", managed_tests)
        self.assertIn(
            "FindNotice(\n                notices,\n                \"Reachy Mini hardware and model assets\")",
            managed_tests,
        )

    def test_modified_reachy_derived_assets_keep_share_alike_attribution(self) -> None:
        inventory = load_json(INVENTORY)
        reachy_entries = []
        for entry in inventory["entries"]:
            haystack = " ".join(
                str(entry.get(field, ""))
                for field in ("id", "name", "owner", "license", "required_notice")
            ).lower()
            if any(term in haystack for term in REACHY_TERMS):
                reachy_entries.append(entry)
        self.assertGreater(len(reachy_entries), 0)
        for entry in reachy_entries:
            with self.subTest(entry=entry["id"]):
                combined = " ".join(flatten_strings(entry)).lower()
                if "cc" in str(entry.get("license", "")).lower():
                    self.assertIn("cc-by-nc-sa", combined)
                    self.assertIn("sharealike", combined)
                    self.assertIn("noncommercial", combined)
                self.assertIn("pollen", combined)
                self.assertNotEqual("unknown", entry.get("modification_status"))
                self.assertTrue(entry.get("required_notice"))

    def test_no_endorsement_claim_is_part_of_the_offline_app_notice(self) -> None:
        managed_tests = read(RMA082)
        for token in (
            "not endorsed",
            "Pollen Robotics",
            "Hugging Face",
            "Google DeepMind",
            "Unity",
            "Google",
            "OpenAI",
        ):
            self.assertIn(token, managed_tests)
        lower = managed_tests.lower()
        self.assertNotIn("official pollen robotics app", lower)
        self.assertNotIn("endorsed and sponsored", lower)

    def test_selected_local_model_has_distribution_identity_and_license_notice(self) -> None:
        manifest = load_json(MODEL_MANIFEST)
        text = "\n".join(flatten_strings(manifest)).lower()
        for token in ("qwen", "apache-2.0", "gguf"):
            self.assertIn(token, text)
        self.assertRegex(text, r"[0-9a-f]{64}")
        guide = read(ROOT / "docs/ANDROID_RELEASE_OPERATOR_GUIDE.md")
        self.assertIn("qwen3-0.6b-q4-k-m.local-llm.json", guide.lower())
        self.assertIn("SHA-256", guide)
        self.assertIn("rejects arbitrary unvalidated paths", guide)


if __name__ == "__main__":
    unittest.main()

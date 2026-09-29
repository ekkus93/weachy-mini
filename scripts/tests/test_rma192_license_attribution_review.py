#!/usr/bin/env python3
"""RMA-192 reproducible license and attribution release-review gate."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
INVENTORY = ROOT / "third_party/inventory.json"
NOTICES = ROOT / "third_party/THIRD_PARTY_NOTICES.md"
RMA082 = ROOT / "managed/ReachyMini.Application.Tests/Rma082SettingsStateTests.cs"
MODEL_MANIFEST = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"
ASSET_POLICY = ROOT / "docs/ASSET_POLICY.md"

PACKAGED_STATUSES = {
    "vendored",
    "downloaded",
    "packaged",
    "packaged_dependency",
    "packaged_beta_dependency",
    "packaged_test_fixture",
}
REACHY_TERMS = ("reachy", "pollen", "hardware", "model asset", "mjcf")
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

    def test_inventory_entries_are_complete_unique_and_notice_bound(self) -> None:
        inventory = load_json(INVENTORY)
        notices = read(NOTICES)
        entries = inventory["entries"]
        ids = [entry["id"] for entry in entries]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertTrue(EXPECTED_INVENTORY_IDS.issubset(set(ids)))

        for entry in entries:
            with self.subTest(entry=entry["id"]):
                missing = REQUIRED_INVENTORY_FIELDS - set(entry)
                self.assertEqual(set(), missing)
                for field in REQUIRED_INVENTORY_FIELDS - {"source_revision"}:
                    self.assertIsInstance(entry[field], str)
                    self.assertTrue(entry[field].strip(), field)
                if entry["status"] not in {"developer_tool", "candidate"}:
                    anchor_candidates = {
                        entry["name"],
                        entry["id"],
                        entry["license"],
                        str(entry["source_revision"]),
                    }
                    self.assertTrue(
                        any(anchor and anchor in notices for anchor in anchor_candidates),
                        "inventory entry lacks a repository notice anchor",
                    )

    def test_reachy_asset_policy_keeps_redistribution_blocked_until_audit(self) -> None:
        inventory = {entry["id"]: entry for entry in load_json(INVENTORY)["entries"]}
        reachy_assets = inventory["reachy-mini-hardware-assets"]
        notices = read(NOTICES)
        asset_policy = read(ASSET_POLICY)

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

    def test_offline_app_notices_screen_remains_wired(self) -> None:
        settings_sections = read(APP / "ReachyMainScreen.SettingsSections.cs")
        app_sources = "\n".join(read(path) for path in sorted(APP.glob("*.cs")))
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

    def test_candidate_model_binaries_remain_blocked_until_explicit_selection(self) -> None:
        inventory = {entry["id"]: entry for entry in load_json(INVENTORY)["entries"]}
        local_model = inventory["local-llm-candidate"]
        notices = read(NOTICES)
        asset_policy = read(ASSET_POLICY)
        self.assertEqual("candidate", local_model["status"])
        self.assertEqual("blocked_pending_selection", local_model["redistribution_status"])
        self.assertIn("no model is selected or distributed", notices.lower())
        self.assertIn("GGUF", asset_policy)
        self.assertIn("model binaries", asset_policy)
        self.assertIn("do not package candidate binaries", local_model["required_notice"].lower())

    def test_repository_notices_do_not_introduce_positive_endorsement_claims(self) -> None:
        scanned_text = "\n".join(
            read(path)
            for root in (ROOT / "README.md", NOTICES)
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

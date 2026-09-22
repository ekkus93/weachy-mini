#!/usr/bin/env python3
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LABELS = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStateStore.Labels.cs"
SCREEN = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.SettingsSections.cs"
NOTICES = ROOT / "third_party/THIRD_PARTY_NOTICES.md"
INVENTORY = ROOT / "third_party/inventory.json"


class Rma012LicenseAttributionContracts(unittest.TestCase):
    def test_in_app_notice_components_cover_inventory(self) -> None:
        source = LABELS.read_text(encoding="utf-8")
        components = set(
            re.findall(
                r'new ReachyLicenseNotice\(\s*"([^"]+)"',
                source,
                flags=re.MULTILINE,
            )
        )
        inventory = json.loads(INVENTORY.read_text(encoding="utf-8"))
        inventory_names = {entry["name"] for entry in inventory["entries"]}
        self.assertEqual(10, len(inventory_names))
        self.assertTrue(
            inventory_names.issubset(components),
            f"Runtime license UI is missing inventory notices: {sorted(inventory_names - components)}",
        )

    def test_project_status_is_explicit_and_offline(self) -> None:
        labels = LABELS.read_text(encoding="utf-8")
        screen = SCREEN.read_text(encoding="utf-8")
        notices = NOTICES.read_text(encoding="utf-8")
        for token in (
            "Unofficial community project",
            "not endorsed by Pollen Robotics",
            "Hugging Face",
            "Google DeepMind",
            "Unity",
            "Google",
            "OpenAI",
            "free and noncommercial",
        ):
            self.assertIn(token, labels)
        self.assertIn("unofficial", notices.lower())
        self.assertIn("free and noncommercial", notices.lower())
        self.assertIn("ReachySettingsStateStore.GetLicenseNotices()", screen)
        self.assertNotIn("http", screen[screen.index("private void DrawLicenseSettings") :])

    def test_fixed_panel_paginates_complete_notice_list(self) -> None:
        screen = SCREEN.read_text(encoding="utf-8")
        self.assertIn("LicenseNoticesPerPage = 4", screen)
        self.assertIn("licenseNoticePage", screen)
        self.assertIn('"PREVIOUS"', screen)
        self.assertIn('"NEXT"', screen)
        self.assertIn("Mathf.CeilToInt", screen)
        self.assertIn("Mathf.Clamp", screen)


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""RMA-191: no raw provider or lifecycle exception text in UI health."""

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class HealthExceptionRedactionTests(unittest.TestCase):
    def test_application_lifecycle_health_only_discloses_exception_classes(self) -> None:
        service = read(CORE / "Application/ReachyApplicationServiceBase.cs")
        host = read(CORE / "Application/ReachyApplicationHost.cs")
        for source in (service, host):
            self.assertNotIn("exception.Message", source)
            self.assertIn("exception.GetType().Name", source)
        self.assertNotIn("contractException.Message", host)
        self.assertIn("contractException.GetType().Name", host)

    def test_speech_capability_health_never_republishes_provider_details(self) -> None:
        speech = read(APP / "ReachyAndroidSpeechCapabilityApplicationService.cs")
        on_device = read(CORE / "Speech/AndroidOnDeviceAsrProvider.Readiness.cs")
        self.assertNotIn("exception.Message", speech)
        self.assertNotIn("exception.Message", on_device)
        self.assertIn("exception.GetType().Name", on_device)
        for provider in ("onDeviceAsr", "systemAsr", "offlineTts"):
            self.assertIn(provider + ".State", speech)
            self.assertNotIn(provider + ".Diagnostic", speech)


if __name__ == "__main__":
    unittest.main()

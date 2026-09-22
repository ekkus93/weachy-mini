#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/ReachyMini/Runtime/Application" / "ReachyRma190CoreEndToEndAcceptance.cs"


class Rma190MainThreadMetadataContract(unittest.TestCase):
    def test_device_metadata_is_captured_before_async_acceptance(self) -> None:
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("string deviceModel = SystemInfo.deviceModel;", source)
        self.assertIn("int androidApiLevel = TryReadApiLevel();", source)
        self.assertIn(
            "RunAcceptanceAsync(\n"
            "                        deviceModel,\n"
            "                        androidApiLevel,",
            source,
        )
        run_start = source.index("private static async Task<AcceptanceReport> RunAcceptanceAsync")
        run_body = source[run_start:]
        self.assertNotIn("SystemInfo.deviceModel", run_body)
        self.assertNotIn("android_api_level = ReadApiLevel()", run_body)


if __name__ == "__main__":
    unittest.main()

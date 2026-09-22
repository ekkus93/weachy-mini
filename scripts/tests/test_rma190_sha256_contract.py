#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (
    ROOT
    / "Assets/ReachyMini/Runtime/Application"
    / "ReachyRma190CoreEndToEndAcceptance.Android.cs"
)


class Rma190Sha256Contract(unittest.TestCase):
    def test_core_acceptance_uses_bcl_sha256_and_keeps_android_helpers(self) -> None:
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn(
            "using CryptoSha256 = System.Security.Cryptography.SHA256;",
            source,
        )
        self.assertIn("CryptoSha256.Create()", source)
        self.assertNotIn("using SHA256 sha256 = SHA256.Create()", source)
        for helper in (
            "private static bool ReadBooleanLaunchExtra",
            "private static int ReadApiLevel",
            "private static void DeleteIfPresent",
            "private static void WriteAtomically",
            "private static string Bound",
        ):
            self.assertIn(helper, source)
        self.assertGreaterEqual(source.count("#if UNITY_ANDROID && !UNITY_EDITOR"), 3)
        self.assertTrue(source.rstrip().endswith("}\n}"))


if __name__ == "__main__":
    unittest.main()

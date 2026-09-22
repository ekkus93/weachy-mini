#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (
    ROOT
    / "Assets/ReachyMini/Runtime/Application"
    / "ReachyRma190CoreEndToEndAcceptance.Android.cs"
)


class Rma190AndroidHelperContract(unittest.TestCase):
    def test_helper_is_complete_and_uses_bcl_sha256(self) -> None:
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn(
            "using CryptoSha256 = System.Security.Cryptography.SHA256;",
            source,
        )
        self.assertIn("CryptoSha256.Create()", source)
        for symbol in (
            "ReadBooleanLaunchExtra",
            "ReadApiLevel",
            "TryReadApiLevel",
            "DeleteIfPresent",
            "WriteAtomically",
            "private static string Bound",
        ):
            self.assertIn(symbol, source)


if __name__ == "__main__":
    unittest.main()

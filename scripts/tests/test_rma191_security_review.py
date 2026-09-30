#!/usr/bin/env python3
"""RMA-191 repository-wide security-review regression contract.

This suite does not replace physical Android credential/lifecycle acceptance. It
keeps the release-review invariants mechanically tied to shipping source so the
final RMA-191 review cannot silently drift into prose-only assertions.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def source_files(*suffixes: str) -> list[Path]:
    ignored = {".git", "Library", "Temp", "obj", "bin", "build", "Builds"}
    result: list[Path] = []
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in suffixes:
            continue
        if any(part in ignored for part in path.parts):
            continue
        result.append(path)
    return result


def joined(paths: list[Path]) -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="strict") for path in paths)


class Rma191SecurityReviewTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.production = joined(
            [
                path
                for path in source_files(".cs", ".java", ".kt", ".xml", ".cpp", ".c", ".h", ".hpp")
                if "Tests" not in path.parts and "tests" not in path.parts
            ]
        )
        cls.all_source = joined(source_files(".cs", ".java", ".kt", ".xml", ".cpp", ".c", ".h", ".hpp"))
        cls.production_lower = cls.production.lower()
        cls.all_source_lower = cls.all_source.lower()

    def assertAnyTextPresent(self, haystack: str, *needles: str) -> None:
        if not any(needle.lower() in haystack.lower() for needle in needles):
            self.fail(f"none of the expected security terms were present: {needles!r}")

    def test_no_obvious_embedded_openai_secret(self) -> None:
        token = re.compile(r"(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{24,}")
        self.assertIsNone(token.search(self.production))

    def test_android_keystore_secret_store_is_fail_closed(self) -> None:
        self.assertIn("ReachyAndroidProviderSecretStore", self.production)
        self.assertIn("AndroidKeyStore", self.production)
        self.assertIn("AES/GCM/NoPadding", self.production)
        self.assertIn("ReachyProviderCredentialLifecycle", self.production)
        self.assertAnyTextPresent(
            self.production,
            "KeyPermanentlyInvalidatedException",
            "PermanentlyInvalidated",
            "invalidated",
        )

    def test_cloud_boundary_requires_explicit_privacy_authorization(self) -> None:
        self.assertIn("EvaluateProviderSwitch", self.production)
        self.assertIn("ConfirmPrivacyBoundaryChange", self.production)
        self.assertIn("ReachyAuthorizedProviderSwitch", self.production)
        self.assertIn("ReachyFallbackPolicyPersistenceStore", self.production)
        self.assertIn("AllowNetworkProviderSwitch", self.production)

    def test_network_transport_has_explicit_cleartext_policy(self) -> None:
        self.assertIn("IsTrustedLocalNetworkHost", self.production)
        self.assertIn("ReachyProviderTlsMode.RequireHttps", self.production)
        self.assertIn("ReachyProviderTlsMode.TrustedLocalNetworkCleartext", self.production)
        self.assertAnyTextPresent(self.production, "http://", "UriSchemeHttp")

    def test_diagnostics_and_logs_keep_sensitive_data_out_by_default(self) -> None:
        for contract in (
            "ReachyDiagnosticBundleExporter",
            "ReachyDiagnosticBundle",
            "ReachyDiagnosticRecordBuffer",
        ):
            self.assertIn(contract, self.production)
        self.assertAnyTextPresent(self.production, "RedactedOnly", "redact", "redacted")
        self.assertIn("credential", self.production_lower)
        self.assertAnyTextPresent(
            self.production_lower,
            "raw media",
            "raw_media",
            "private media",
            "private/raw-media",
            "raw audio",
            "raw images",
            "transcript",
        )

    def test_private_media_temporary_storage_has_delete_and_purge_contract(self) -> None:
        self.assertIn("ReachyPrivateMediaTemporaryFileStore", self.production)
        self.assertIn("PurgeAbandonedFiles", self.production)
        self.assertIn("Delete", self.production)
        self.assertIn("IsPersistentMediaRetentionAllowed", self.production)
        self.assertIn("RecordingEnabled", self.production)
        self.assertIn("MediaExportEnabled", self.production)

    def test_native_boundary_retains_bounded_copy_and_lifetime_failure_coverage(self) -> None:
        for contract in (
            "REACHY_SIM_STATUS_HANDLE_BUSY",
            "reachy_sim_copy_state",
            "reachy_sim_destroy",
            "reachy_sim_last_error",
        ):
            self.assertIn(contract, self.all_source)
        self.assertIn("stale", self.all_source_lower)
        self.assertAnyTextPresent(
            self.all_source_lower,
            "undersized",
            "buffer too small",
            "insufficient buffer",
            "buffer length",
            "capacity",
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)

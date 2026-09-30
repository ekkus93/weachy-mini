#!/usr/bin/env python3
"""RMA-191 repository-wide security-review regression contract.

RMA-191 is a release-review gate, not a duplicate implementation of the
security/privacy subsystems.  Bind it to the established subsystem regression
contracts and add a small repository-wide embedded-secret scan.  Physical
Android credential/lifecycle evidence remains owned by the device acceptance
workflows and is not fabricated here.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts" / "tests"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="strict")


class Rma191SecurityReviewTests(unittest.TestCase):
    def test_no_obvious_embedded_openai_secret_in_production_source(self) -> None:
        token = re.compile(r"(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{24,}")
        ignored = {".git", "Library", "Temp", "obj", "bin", "build", "Builds", "Tests", "tests"}
        suffixes = {".cs", ".java", ".kt", ".xml", ".cpp", ".c", ".h", ".hpp"}
        matches: list[str] = []
        for path in ROOT.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in suffixes:
                continue
            if any(part in ignored for part in path.parts):
                continue
            if token.search(read(path)):
                matches.append(str(path.relative_to(ROOT)))
        self.assertEqual([], matches, f"token-shaped embedded secrets found: {matches}")

    def test_keystore_review_is_owned_by_rma161_contract(self) -> None:
        contract = read(SCRIPTS / "test_rma161_credential_lifecycle.py")
        for token in (
            'private static final String KEYSTORE = "AndroidKeyStore";',
            'private static final String CIPHER = "AES/GCM/NoPadding";',
            "KeyPermanentlyInvalidatedException",
            "encrypted credential records require explicit deletion before replacement",
            "RedactedProviderExportNeverContainsCredentialValuesOrReferences",
        ):
            self.assertIn(token, contract)

    def test_network_and_provider_boundary_review_is_fail_closed(self) -> None:
        engine = read(
            ROOT
            / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderFallbackPolicyEngine.cs"
        )
        cloud = read(
            ROOT
            / "Assets/ReachyMini/Runtime/Application/ReachyCloudLlmCredentialCoordinator.cs"
        )
        composition = read(SCRIPTS / "test_rma195_application_composition.py")
        for token in (
            "EvaluateProviderSwitch",
            "ConfirmPrivacyBoundaryChange",
            "RequiresPrivacyConfirmation",
            "AllowNetworkProviderSwitch",
            "target-provider-not-authorized",
        ):
            self.assertIn(token, engine)
        for token in (
            "TrustedLocalNetworkCleartext",
            "RequireHttps",
            "IsTrustedLocalNetworkHost",
            "authorization revoked. Generation requests will fail closed",
        ):
            self.assertIn(token, cloud)
        self.assertIn(
            "test_llm_execution_does_not_cross_provider_boundary_on_failure",
            composition,
        )

    def test_logging_and_diagnostic_review_is_owned_by_rma170_and_rma172_contracts(self) -> None:
        logging_contract = read(SCRIPTS / "test_rma170_structured_logging.py")
        bundle_contract = read(SCRIPTS / "test_rma172_diagnostic_bundle_export.py")
        for token in (
            'RedactedValue = "[redacted]"',
            '"authorization"',
            '"cookie"',
            "RedactUrl",
        ):
            self.assertIn(token, logging_contract)
        for token in (
            "RequireRedactedOnlySelection",
            "Sensitive diagnostic export is not implemented",
            "credentials, raw audio, raw images, raw media, transcripts, conversation text",
            "DefaultDeniedDataClasses",
        ):
            self.assertIn(token, bundle_contract)

    def test_private_media_cleanup_review_is_owned_by_rma162_contract(self) -> None:
        contract = read(SCRIPTS / "test_rma162_private_media_retention.py")
        for token in (
            "PurgeAbandonedFiles",
            "File.Delete",
            "Application.temporaryCachePath",
            "Application.persistentDataPath",
            "RecordingEnabled => false",
            "MediaExportEnabled => false",
        ):
            self.assertIn(token, contract)

    def test_native_buffer_and_lifetime_review_binds_to_native_contracts(self) -> None:
        native_files = [
            path
            for path in ROOT.rglob("*")
            if path.is_file() and path.suffix.lower() in {".c", ".cpp", ".h", ".hpp"}
        ]
        native = "\n".join(read(path) for path in native_files)
        for token in (
            "REACHY_SIM_STATUS_HANDLE_BUSY",
            "reachy_sim_copy_state",
            "reachy_sim_destroy",
            "reachy_sim_last_error",
        ):
            self.assertIn(token, native)
        lowered = native.lower()
        self.assertIn("stale", lowered)
        self.assertTrue(
            any(token in lowered for token in ("undersized", "buffer too small", "capacity")),
            "native source must retain an explicit bounded-buffer failure contract",
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)

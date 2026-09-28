#!/usr/bin/env python3
"""RMA-191 repository-wide privacy and security review contracts."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"
NATIVE = ROOT / "native/reachy_sim"

TEXT_SUFFIXES = {
    ".cs",
    ".java",
    ".json",
    ".md",
    ".py",
    ".sh",
    ".xml",
    ".yaml",
    ".yml",
    ".txt",
    ".h",
    ".hpp",
    ".c",
    ".cc",
    ".cpp",
}
EXCLUDED_PARTS = {
    ".git",
    ".gradle",
    ".idea",
    ".pytest_cache",
    "Library",
    "Temp",
    "Obj",
    "build",
    "Build",
    "Logs",
}
SECRET_PATTERNS = {
    "openai_secret_key": re.compile(r"sk-[A-Za-z0-9_-]{20,}"),
    "google_api_key": re.compile(r"AIza[0-9A-Za-z_-]{20,}"),
    "github_token": re.compile(r"gh[pousr]_[A-Za-z0-9_]{20,}"),
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def text_files() -> list[Path]:
    files: list[Path] = []
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix not in TEXT_SUFFIXES:
            continue
        if any(part in EXCLUDED_PARTS for part in path.relative_to(ROOT).parts):
            continue
        files.append(path)
    return files


def find_one(pattern: str) -> Path:
    matches = sorted(ROOT.glob(pattern))
    if not matches:
        raise AssertionError(f"No repository file matched {pattern!r}")
    if len(matches) != 1:
        raise AssertionError(f"Expected one match for {pattern!r}, found {matches!r}")
    return matches[0]


class Rma191PrivacySecurityReviewContractTests(unittest.TestCase):
    def test_repository_does_not_contain_obvious_embedded_service_keys(self) -> None:
        findings: list[str] = []
        for path in text_files():
            text = path.read_text(encoding="utf-8", errors="ignore")
            for name, pattern in SECRET_PATTERNS.items():
                if pattern.search(text):
                    findings.append(f"{name}: {path.relative_to(ROOT)}")
        self.assertEqual([], findings)

    def test_provider_secrets_use_android_keystore_and_are_not_logged(self) -> None:
        bridge_path = find_one("**/ReachyProviderSecretBridge.java")
        bridge = read(bridge_path)
        lifecycle = read(APP / "ReachyProviderCredentialLifecycle.cs")
        llm_ui = read(APP / "ReachyMainScreen.CloudLlmCredentials.cs")
        settings = read(APP / "ReachyMainScreen.SettingsSections.cs")
        self.assertIn("AndroidKeyStore", bridge)
        self.assertIn("AES/GCM/NoPadding", bridge)
        self.assertIn("KeyPermanentlyInvalidatedException", bridge)
        self.assertIn("DeleteSecret", lifecycle)
        self.assertIn("cloudLlmApiKeyDraft = string.Empty", llm_ui)
        self.assertIn("GUI.PasswordField", settings)
        self.assertNotIn("Debug.Log(cloudLlmApiKeyDraft", settings + llm_ui)
        self.assertNotIn("Log.d(\"", bridge)

    def test_network_cloud_providers_require_explicit_authorization_and_disclosure(self) -> None:
        settings = read(APP / "ReachyMainScreen.SettingsSections.cs")
        llm = read(APP / "ReachyCloudLlmCredentialCoordinator.cs")
        vlm = read(APP / "ReachyCloudVlmCredentialCoordinator.cs")
        profile = read(CORE / "Providers/ReachyProviderProfile.cs")
        self.assertIn("requires explicit authorization", settings)
        self.assertIn("sends your conversation text", settings)
        self.assertIn("sends camera frames", settings)
        self.assertIn("GrantAuthorization", llm)
        self.assertIn("GrantAuthorization", vlm)
        self.assertIn("RevokeAuthorization", llm)
        self.assertIn("RevokeAuthorization", vlm)
        self.assertIn("IsTrustedLocalNetworkHost", profile)
        self.assertIn("http://", profile)
        self.assertIn("https://", profile)

    def test_logs_and_diagnostic_bundles_redact_private_content_by_default(self) -> None:
        diagnostics = read(CORE / "Diagnostics/ReachyDiagnosticBundleExporter.cs")
        logging = read(CORE / "Diagnostics/ReachyDiagnosticRedactor.cs")
        retention = read(CORE / "Application/ReachyPrivateMediaRetentionPolicy.cs")
        self.assertIn("RedactedOnly", diagnostics)
        self.assertIn("Exclude", diagnostics)
        self.assertIn("Credential", diagnostics)
        self.assertIn("RawMedia", diagnostics)
        self.assertIn("Redact", logging)
        self.assertIn("PersistentMediaRetentionUnavailableReason", retention)
        self.assertIn("return false", retention)

    def test_temporary_private_media_files_are_deleted_or_swept(self) -> None:
        store_path = find_one("**/ReachyPrivateMediaTemporaryFileStore.cs")
        store = read(store_path)
        self.assertIn("Dispose", store)
        self.assertIn("Delete", store)
        self.assertIn("PurgeAbandonedFiles", store)
        self.assertIn("persistent", store.lower())
        self.assertIn("protected", store.lower())

    def test_native_abi_uses_bounded_buffers_and_explicit_lifetimes(self) -> None:
        header = read(NATIVE / "include/reachy_sim.h")
        runtime = read(NATIVE / "src/reachy_sim.cpp")
        self.assertIn("uint32_t abi_version", header)
        self.assertIn("uint32_t struct_size", header)
        self.assertIn("error_buffer", header)
        self.assertIn("byte_capacity", header)
        self.assertIn("required_size", header)
        self.assertIn("reachy_sim_destroy", header)
        self.assertIn("last_error", header)
        self.assertIn("stale", runtime.lower())
        self.assertIn("generation", runtime.lower())


if __name__ == "__main__":
    unittest.main()

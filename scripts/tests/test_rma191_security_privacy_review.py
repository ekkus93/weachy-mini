#!/usr/bin/env python3
"""RMA-191 reproducible security and privacy release-review gate."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

SECRET_BRIDGE = ROOT / "Assets/Plugins/Android/ReachyProviderSecurity.androidlib/src/main/java/com/ekkus93/weachy/providers/ReachyProviderSecretBridge.java"
PROVIDER_CONFIG = ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderConfiguration.cs"
PROVIDER_SELECTION = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachyProviderSelection.cs"
SETTINGS_TEXT = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStateStore.DerivedText.cs"
SETTINGS_UI = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyMainScreen.SettingsSections.cs"
REDACTOR = ROOT / "Assets/ReachyMini/Runtime/Core/Diagnostics/ReachyDiagnosticRedactor.cs"
EXPORTER = ROOT / "Assets/ReachyMini/Runtime/Core/Diagnostics/ReachyDiagnosticBundleExporter.cs"
MEDIA_STORE = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachyPrivateMediaTemporaryFileStore.cs"
MEDIA_STORAGE = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyPrivateMediaStorage.cs"
SAFE_HANDLE = ROOT / "Assets/ReachyMini/Runtime/Interop/ReachySimSafeHandle.cs"
SESSION = ROOT / "Assets/ReachyMini/Runtime/Interop/ReachySimSession.cs"
LIFECYCLE = ROOT / "managed/ReachyMini.Core.Tests/Program.Rma031SessionLifecycleTests.cs"
RMA082 = ROOT / "managed/ReachyMini.Application.Tests/Rma082SettingsStateTests.cs"
SETTINGS_ENUMS = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsEnums.cs"
RMA170 = ROOT / "managed/ReachyMini.Core.Tests/Rma170StructuredDiagnosticsContractTests.cs"
RMA172 = ROOT / "managed/ReachyMini.Core.Tests/Rma172DiagnosticBundleExportContractTests.cs"
RMA162 = ROOT / "managed/ReachyMini.Application.Tests/Rma162PrivateMediaRetentionTests.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma191SecurityPrivacyReview(unittest.TestCase):
    def test_no_embedded_production_credentials(self) -> None:
        roots = (
            ROOT / "Assets/ReachyMini/Runtime",
            ROOT / "Assets/Plugins/Android",
            ROOT / ".github",
            ROOT / "ProjectSettings",
        )
        patterns = {
            "OpenAI-style API key": re.compile(r"\bsk-[A-Za-z0-9_-]{20,}\b"),
            "Google API key": re.compile(r"\bAIza[0-9A-Za-z_-]{30,}\b"),
            "GitHub token": re.compile(r"\bgh(?:p|o|u|s|r)_[A-Za-z0-9]{20,}\b"),
            "Slack token": re.compile(r"\bxox[baprs]-[A-Za-z0-9-]{20,}\b"),
            "PEM private key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
        }
        findings: list[str] = []
        text_suffixes = {
            ".cs", ".java", ".kt", ".xml", ".json", ".yaml", ".yml",
            ".md", ".txt", ".properties", ".gradle", ".sh", ".py",
        }
        for root in roots:
            if not root.exists():
                continue
            for path in root.rglob("*"):
                if not path.is_file() or path.suffix.lower() not in text_suffixes:
                    continue
                try:
                    content = path.read_text(encoding="utf-8")
                except UnicodeDecodeError:
                    continue
                for label, pattern in patterns.items():
                    if pattern.search(content):
                        findings.append(f"{label}: {path.relative_to(ROOT)}")
        self.assertEqual([], findings, "Embedded production credentials found: " + "; ".join(findings))

    def test_android_keystore_storage_is_authenticated_and_non_plaintext(self) -> None:
        source = read(SECRET_BRIDGE)
        for token in (
            'private static final String KEYSTORE = "AndroidKeyStore";',
            'private static final String CIPHER = "AES/GCM/NoPadding";',
            ".setRandomizedEncryptionRequired(true)",
            "new GCMParameterSpec(GCM_TAG_BITS, iv)",
            "cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8))",
            "Context.MODE_PRIVATE",
            "clear(iv);",
            "clear(ciphertext);",
            "requireDebuggableApplication(context);",
        ):
            self.assertIn(token, source)
        self.assertIn("putString(ivKey(reference)", source)
        self.assertIn("putString(\n                                ciphertextKey(reference)", source)
        self.assertNotIn("putString(reference,", source)

    def test_android_and_provider_network_policy_does_not_enable_global_cleartext(self) -> None:
        manifests = list((ROOT / "Assets/Plugins/Android").rglob("AndroidManifest.xml"))
        self.assertTrue(manifests)
        for manifest in manifests:
            source = read(manifest)
            self.assertNotRegex(source, r'usesCleartextTraffic\s*=\s*["\']true["\']')

        config = read(PROVIDER_CONFIG)
        for token in (
            "ReachyProviderTlsMode.RequireHttps",
            "ReachyProviderTlsMode.TrustedLocalNetworkCleartext",
            "Provider profiles require HTTPS unless the explicit trusted-local-network cleartext mode is selected.",
            "Cleartext provider profiles are restricted to trusted local-network hosts.",
            "ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost(baseUri)",
            "LOCAL DEVELOPMENT ONLY",
        ):
            self.assertIn(token, config)

    def test_cloud_bound_audio_image_and_text_are_disclosed_in_visible_settings(self) -> None:
        selection = read(PROVIDER_SELECTION)
        derived = read(SETTINGS_TEXT)
        ui = read(SETTINGS_UI)
        tests = read(RMA082)
        settings_enums = read(SETTINGS_ENUMS)
        self.assertIn("public bool SendsDataOffDevice", selection)
        self.assertIn("ReachyProviderExecution.AndroidService", selection)
        self.assertIn("ReachyProviderExecution.Cloud", selection)
        self.assertIn("provider.SendsDataOffDevice", derived)
        self.assertIn("Cloud/network-bound selections", derived)
        self.assertIn("current.PrivacyCloudSummary", ui)
        for kind in ("Asr", "Tts", "Llm", "Vlm"):
            self.assertRegex(settings_enums, rf"\b{kind}\s*=|\b{kind}\s*,")
        self.assertIn("Android off-device indicator", tests)
        self.assertIn("cloud LLM network requirement", tests)

    def test_diagnostics_redact_secrets_private_text_audio_images_and_media(self) -> None:
        redactor = read(REDACTOR)
        exporter = read(EXPORTER)
        structured_tests = read(RMA170)
        bundle_tests = read(RMA172)
        for data_class in (
            "Secret",
            "PrivateText",
            "RawAudio",
            "RawImage",
            "RawMedia",
        ):
            self.assertIn(f"ReachyDiagnosticDataClass.{data_class}", redactor)
        for marker in ("authorization", "api-key", "token", "password="):
            self.assertIn(marker, redactor.lower())
        self.assertIn("ReachyDiagnosticRedactor.Redact", exporter)
        self.assertIn("ReachyDiagnosticRedactor.RedactedValue", structured_tests)
        self.assertIn("ReachyDiagnosticRedactor.RedactedValue", bundle_tests)

    def test_temporary_private_media_is_deleted_and_abandoned_files_are_purged(self) -> None:
        store = read(MEDIA_STORE)
        storage = read(MEDIA_STORAGE)
        tests = read(RMA162)
        self.assertIn("File.Delete(currentPath);", store)
        self.assertIn("public int PurgeAbandonedFiles()", store)
        self.assertIn("File.Delete(path);", store)
        self.assertIn("store.PurgeAbandonedFiles()", storage)
        self.assertIn("temporary media deleted on lease disposal", tests)
        self.assertIn("abandoned media purge count", tests)

    def test_native_handles_buffers_and_session_lifetimes_have_bounded_ownership(self) -> None:
        safe_handle = read(SAFE_HANDLE)
        session = read(SESSION)
        lifecycle = read(LIFECYCLE)
        self.assertIn("SafeHandleZeroOrMinusOneIsInvalid", safe_handle)
        self.assertIn("NativeReachySim.Destroy(Token)", safe_handle)
        self.assertIn("SetHandleAsInvalid();", safe_handle)
        self.assertIn("Marshal.AllocHGlobal", session)
        self.assertIn("Marshal.FreeHGlobal", session)
        self.assertIn("finally", session)
        self.assertIn("for (int iteration = 0; iteration < 1000; ++iteration)", lifecycle)
        self.assertIn("AssertThrows<ObjectDisposedException>", lifecycle)
        self.assertIn("session.Dispose();", lifecycle)


if __name__ == "__main__":
    unittest.main()

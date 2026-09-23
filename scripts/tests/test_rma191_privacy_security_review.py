#!/usr/bin/env python3
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets" / "ReachyMini" / "Runtime"
PLUGINS = ROOT / "Assets" / "Plugins" / "Android"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma191PrivacySecurityReview(unittest.TestCase):
    def test_production_sources_contain_no_embedded_private_credentials(self) -> None:
        patterns = {
            "OpenAI-style secret": re.compile(r"\bsk-[A-Za-z0-9_-]{20,}"),
            "Google API key": re.compile(r"\bAIza[0-9A-Za-z_-]{20,}"),
            "AWS access key": re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
            "private key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
        }
        suffixes = {".cs", ".java", ".kt", ".xml", ".c", ".h", ".cpp", ".inc"}
        hits: list[str] = []
        for base in (
            RUNTIME,
            PLUGINS,
            ROOT / "native" / "reachy_sim",
            ROOT / "native" / "llama_runtime" / "src",
        ):
            for path in base.rglob("*"):
                if not path.is_file() or path.suffix.lower() not in suffixes:
                    continue
                source = path.read_text(encoding="utf-8", errors="ignore")
                for label, pattern in patterns.items():
                    if pattern.search(source):
                        hits.append(f"{path.relative_to(ROOT)}: {label}")
        self.assertEqual([], hits)

    def test_android_provider_secrets_are_keystore_backed_and_authenticated(self) -> None:
        bridge = read(
            PLUGINS
            / "ReachyProviderSecurity.androidlib"
            / "src/main/java/com/ekkus93/weachy/providers/ReachyProviderSecretBridge.java"
        )
        for token in (
            "KeyStore.getInstance(KEYSTORE)",
            '"AndroidKeyStore"',
            "AES/GCM/NoPadding",
            "setRandomizedEncryptionRequired(true)",
            "cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8))",
            "Base64.encodeToString(ciphertext, Base64.NO_WRAP)",
            "clear(ciphertext)",
        ):
            self.assertIn(token, bridge)
        self.assertNotIn("putString(reference,", bridge)

    def test_provider_network_policy_is_https_by_default_and_cleartext_is_lan_scoped(self) -> None:
        config = read(RUNTIME / "Core/Providers/ReachyProviderConfiguration.cs")
        transport = read(RUNTIME / "Core/Providers/ReachySharedHttpTransport.Helpers.cs")
        for token in (
            "ReachyProviderTlsMode.RequireHttps",
            "ReachyProviderTlsMode.TrustedLocalNetworkCleartext",
            "ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost(baseUri)",
            "Provider profiles require HTTPS unless the explicit trusted-local-network cleartext mode is selected.",
        ):
            self.assertIn(token, config)
        for token in (
            "AllowAutoRedirect = false",
            "uri.Host",
            "profile.BaseUri.Host",
            "uri.Port != profile.BaseUri.Port",
            "IsTrustedLocalNetworkHost(uri)",
        ):
            self.assertIn(token, transport)

    def test_cloud_bound_text_and_images_require_visible_disclosure_and_authorization(self) -> None:
        ui = read(RUNTIME / "Application/ReachyMainScreen.SettingsSections.cs")
        llm_ui = read(RUNTIME / "Application/ReachyMainScreen.CloudLlmCredentials.cs")
        llm = read(RUNTIME / "Application/ReachyLocalLlmProviderApplicationService.cs")
        vlm = read(RUNTIME / "Application/ReachyAndroidPerceptionApplicationService.CloudVlm.cs")
        selection = read(RUNTIME / "Core/Application/ReachyProviderSelection.cs")
        privacy = read(RUNTIME / "Core/Application/ReachySettingsStateStore.DerivedText.cs")
        for token in (
            "Enabling cloud LLM sends your conversation text to the base URL",
            "Nothing is sent off-device until you explicitly authorize it.",
            "requires explicit authorization below before any camera frame",
            "Enabling cloud VLM sends camera frames to the base URL above.",
        ):
            self.assertIn(token, ui)
        self.assertIn("ReachyProviderPrivacyBoundaryClassifier.DescribeBoundary", llm_ui)
        self.assertIn("ConfirmPrivacyBoundaryChange", llm)
        self.assertIn("ConfirmPrivacyBoundaryChange", vlm)
        self.assertIn("SendsDataOffDevice", selection)
        self.assertIn("Cloud/network-bound selections", privacy)

    def test_logs_and_exported_diagnostics_are_redacted_by_data_class(self) -> None:
        redactor = read(RUNTIME / "Core/Diagnostics/ReachyDiagnosticRedactor.cs")
        logger = read(RUNTIME / "Core/Diagnostics/ReachyDiagnosticLogger.cs")
        exporter = read(RUNTIME / "Core/Diagnostics/ReachyDiagnosticBundleExporter.cs")
        for token in (
            "ReachyDiagnosticDataClass.Secret",
            "ReachyDiagnosticDataClass.PrivateText",
            "ReachyDiagnosticDataClass.RawAudio",
            "ReachyDiagnosticDataClass.RawImage",
            "ReachyDiagnosticDataClass.RawMedia",
            '"authorization"',
            '"api_key="',
            '"password="',
        ):
            self.assertIn(token, redactor)
        self.assertIn("ReachyDiagnosticRedactor.Redact", logger)
        self.assertIn("redaction_policy", exporter)
        self.assertIn("non-redacted user selections fail closed", exporter)

    def test_private_media_is_nonpersistent_by_default_and_temp_files_are_deleted(self) -> None:
        policy = read(RUNTIME / "Core/Application/ReachyPrivateMediaRetentionPolicy.cs")
        store = read(RUNTIME / "Core/Application/ReachyPrivateMediaTemporaryFileStore.cs")
        storage = read(RUNTIME / "Application/ReachyPrivateMediaStorage.cs")
        asr = read(RUNTIME / "Core/Providers/ReachyOpenAiCompatibleAsrProvider.Core.cs")
        for token in (
            "RecordingEnabled => false",
            "MediaExportEnabled => false",
            "RawCameraFrame",
            "MicrophoneAudio",
            "CloudRequestMedia",
        ):
            self.assertIn(token, policy)
        self.assertIn("Application.temporaryCachePath", storage)
        self.assertNotIn("Application.persistentDataPath", storage)
        self.assertIn("File.Delete(currentPath)", store)
        self.assertIn("PurgeAbandonedFiles", store)
        self.assertIn("Array.Clear(multipartBody, 0, multipartBody.Length)", asr)

    def test_native_boundary_uses_safe_handles_bounded_buffers_and_lifecycle_stress(self) -> None:
        handle = read(RUNTIME / "Interop/ReachySimSafeHandle.cs")
        session = read(RUNTIME / "Interop/ReachySimSession.cs")
        lifecycle = read(ROOT / "managed/ReachyMini.Core.Tests/Program.Rma031SessionLifecycleTests.cs")
        concurrency = read(ROOT / "native/reachy_sim/tests/reachy_sim_concurrency_test.inc")
        self.assertIn("SafeHandleZeroOrMinusOneIsInvalid", handle)
        self.assertIn("NativeReachySim.Destroy(Token)", handle)
        self.assertIn("lock (gate)", session)
        self.assertIn("NativeReachySimStatus.BufferTooSmall", session)
        self.assertIn("requiredSizeValue > int.MaxValue", session)
        self.assertIn("Marshal.AllocHGlobal(requiredSize)", session)
        self.assertIn("Marshal.FreeHGlobal(buffer)", session)
        self.assertIn("for (int iteration = 0; iteration < 1000; ++iteration)", lifecycle)
        self.assertIn("REACHY_SIM_STATUS_BUFFER_TOO_SMALL", concurrency)


if __name__ == "__main__":
    unittest.main()

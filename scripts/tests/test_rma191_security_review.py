#!/usr/bin/env python3
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets" / "ReachyMini" / "Runtime"
ANDROID_SECURITY = (
    ROOT
    / "Assets"
    / "Plugins"
    / "Android"
    / "ReachyProviderSecurity.androidlib"
    / "src"
    / "main"
    / "java"
    / "com"
    / "ekkus93"
    / "weachy"
    / "providers"
    / "ReachyProviderSecretBridge.java"
)

TEXT_SUFFIXES = {
    ".cs",
    ".java",
    ".kt",
    ".xml",
    ".gradle",
    ".json",
    ".md",
    ".py",
    ".sh",
    ".yml",
    ".yaml",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma191SecurityReviewContracts(unittest.TestCase):
    def test_no_embedded_private_keys_or_openai_style_live_keys(self) -> None:
        private_key_marker = "BEGIN " + "PRIVATE KEY"
        openai_style = re.compile(r"\b" + "sk" + r"-[A-Za-z0-9_-]{20,}\b")
        offenders = []
        for root in (ROOT / "Assets", ROOT / "managed", ROOT / "scripts"):
            for path in root.rglob("*"):
                if not path.is_file() or path.suffix.lower() not in TEXT_SUFFIXES:
                    continue
                try:
                    source = read(path)
                except UnicodeDecodeError:
                    continue
                if private_key_marker in source or openai_style.search(source):
                    offenders.append(str(path.relative_to(ROOT)))
        self.assertEqual([], offenders)

    def test_provider_secrets_are_keystore_backed_and_aead_encrypted(self) -> None:
        source = read(ANDROID_SECURITY)
        self.assertIn('"AndroidKeyStore"', source)
        self.assertIn('"AES/GCM/NoPadding"', source)
        self.assertIn("KeyGenerator.getInstance(", source)
        self.assertIn("KeyStore.getInstance(KEYSTORE)", source)
        self.assertIn("setUserAuthenticationRequired(false)", source)

        managed = read(
            RUNTIME
            / "Application"
            / "ReachyAndroidProviderSecretStore.cs"
        )
        self.assertIn("ReachyProviderSecretBridge", managed)
        self.assertNotIn("PlayerPrefs", managed)

    def test_provider_transport_is_https_or_explicit_trusted_local_only(self) -> None:
        config = read(
            RUNTIME
            / "Core"
            / "Providers"
            / "ReachyProviderConfiguration.cs"
        )
        transport = read(
            RUNTIME
            / "Core"
            / "Providers"
            / "ReachySharedHttpTransport.Helpers.cs"
        )
        endpoint_security = read(
            RUNTIME
            / "Core"
            / "Security"
            / "ReachyImportedContentSecurity.cs"
        )
        self.assertIn("ReachyProviderTlsMode.RequireHttps", config)
        self.assertIn("ReachyProviderTlsMode.TrustedLocalNetworkCleartext", config)
        self.assertIn("IsTrustedLocalNetworkHost(baseUri)", config)
        self.assertIn("uri.Host", transport)
        self.assertIn("profile.BaseUri.Host", transport)
        self.assertIn("uri.Port != profile.BaseUri.Port", transport)
        self.assertIn("Uri.UriSchemeHttps", transport)
        self.assertIn("IsTrustedLocalNetworkHost(uri)", transport)
        self.assertIn("DNS rebinding", endpoint_security)

    def test_cloud_text_and_image_paths_require_explicit_authorization(self) -> None:
        ui = read(
            RUNTIME
            / "Application"
            / "ReachyMainScreen.SettingsSections.cs"
        )
        policy = read(
            RUNTIME
            / "Core"
            / "Providers"
            / "ReachyProviderFallbackPolicyEngine.cs"
        )
        self.assertIn(
            "requires explicit authorization below before any request leaves the device",
            ui,
        )
        self.assertIn(
            "requires explicit authorization below before any camera frame",
            ui,
        )
        self.assertIn("Nothing is sent off-device until you explicitly authorize it.", ui)
        self.assertIn("privacy-boundary-confirmation-required", policy)
        self.assertIn("AllowNetworkProviderSwitch", policy)

    def test_diagnostics_redact_secret_private_text_and_raw_media(self) -> None:
        redactor = read(
            RUNTIME / "Core" / "Diagnostics" / "ReachyDiagnosticRedactor.cs"
        )
        for data_class in (
            "Secret",
            "PrivateText",
            "RawAudio",
            "RawImage",
            "RawMedia",
            "Header",
            "Url",
        ):
            self.assertIn(f"ReachyDiagnosticDataClass.{data_class}", redactor)
        self.assertIn('"authorization"', redactor)
        self.assertIn('"api-key"', redactor)
        self.assertIn('"token="', redactor)

    def test_private_media_is_nonpersistent_and_temp_files_are_deleted(self) -> None:
        policy = read(
            RUNTIME
            / "Core"
            / "Application"
            / "ReachyPrivateMediaRetentionPolicy.cs"
        )
        store = read(
            RUNTIME
            / "Core"
            / "Application"
            / "ReachyPrivateMediaTemporaryFileStore.cs"
        )
        self.assertIn("public static bool RecordingEnabled => false;", policy)
        self.assertIn("public static bool MediaExportEnabled => false;", policy)
        self.assertIn("return false;", policy)
        self.assertIn("File.Delete(path);", store)
        self.assertIn("public void Dispose()", store)
        self.assertIn("PurgeAbandonedFiles()", store)

    def test_native_handles_and_unmanaged_buffers_have_bounded_lifetimes(self) -> None:
        safe_handle = read(RUNTIME / "Interop" / "ReachySimSafeHandle.cs")
        reader = read(
            RUNTIME / "Interop" / "ReachySimAuthoritativeStateReader.cs"
        )
        session = read(RUNTIME / "Interop" / "ReachySimSession.cs")
        self.assertIn("SafeHandleZeroOrMinusOneIsInvalid", safe_handle)
        self.assertIn("protected override bool ReleaseHandle()", safe_handle)
        self.assertIn("NativeReachySim.Destroy(Token)", safe_handle)
        self.assertIn("Marshal.FreeHGlobal(buffer);", reader)
        self.assertIn("buffer = IntPtr.Zero;", reader)
        self.assertIn("GC.SuppressFinalize(this);", reader)
        self.assertIn("activeHandle.Dispose();", session)
        self.assertIn("handle = null;", session)
        self.assertIn("GC.SuppressFinalize(this);", session)


if __name__ == "__main__":
    unittest.main()

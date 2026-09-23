#!/usr/bin/env python3
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REVIEW = ROOT / "docs" / "RMA_191_PRIVACY_SECURITY_REVIEW_2026-09-23.md"

PRODUCTION_SECRET_SCAN_ROOTS = (
    ROOT / "Assets" / "ReachyMini" / "Runtime",
    ROOT
    / "Assets"
    / "Plugins"
    / "Android"
    / "ReachyProviderSecurity.androidlib"
    / "src"
    / "main",
    ROOT / "android-plugin" / "src" / "main",
    ROOT / "native" / "reachy_sim" / "src",
    ROOT / "native" / "llama_runtime" / "src",
)

SECRET_PATTERNS = (
    re.compile(r"sk-[A-Za-z0-9_-]{16,}"),
    re.compile(r"AKIA[0-9A-Z]{16}"),
    re.compile(r"AIza[0-9A-Za-z_-]{20,}"),
    re.compile(r"gh[pousr]_[A-Za-z0-9]{20,}"),
    re.compile(r"xox[baprs]-[A-Za-z0-9-]{20,}"),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma191PrivacySecurityReviewContracts(unittest.TestCase):
    def test_production_roots_contain_no_high_confidence_embedded_secrets(self) -> None:
        findings: list[str] = []
        for root in PRODUCTION_SECRET_SCAN_ROOTS:
            for path in root.rglob("*"):
                if not path.is_file():
                    continue
                try:
                    source = path.read_text(encoding="utf-8")
                except UnicodeDecodeError:
                    continue
                for pattern in SECRET_PATTERNS:
                    if pattern.search(source):
                        findings.append(str(path.relative_to(ROOT)))
                        break
        self.assertEqual([], findings)

    def test_android_provider_secrets_use_keystore_authenticated_encryption(self) -> None:
        bridge = read(
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
        for anchor in (
            '"AndroidKeyStore"',
            "KeyProperties.KEY_ALGORITHM_AES",
            "KeyProperties.BLOCK_MODE_GCM",
            "KeyProperties.ENCRYPTION_PADDING_NONE",
            ".setRandomizedEncryptionRequired(true)",
            "cipher.updateAAD(reference.getBytes(StandardCharsets.UTF_8))",
        ):
            self.assertIn(anchor, bridge)

        managed = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Application"
            / "ReachyAndroidProviderSecretStore.cs"
        )
        self.assertIn("IReachyProviderSecretStore", managed)
        self.assertIn("ReachyProviderSecretBridge", managed)

    def test_application_network_policy_rejects_public_cleartext(self) -> None:
        endpoint_security = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Security"
            / "ReachyImportedContentSecurity.cs"
        )
        self.assertIn("RequirePublicHttpsUri", endpoint_security)
        self.assertIn("IsTrustedLocalNetworkHost", endpoint_security)
        self.assertIn("IsPublicInternetHost", endpoint_security)

        transport = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Providers"
            / "ReachySharedHttpTransport.Helpers.cs"
        )
        self.assertIn("ReachyProviderTlsMode.RequireHttps", transport)
        self.assertIn("Uri.UriSchemeHttps", transport)
        self.assertIn("Uri.UriSchemeHttp", transport)
        self.assertIn("IsTrustedLocalNetworkHost(uri)", transport)

        profile = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Providers"
            / "ReachyProviderConfiguration.cs"
        )
        self.assertIn("TrustedLocalNetworkCleartext", profile)
        self.assertIn("LOCAL DEVELOPMENT ONLY", profile)

    def test_platform_network_policy_gap_is_explicit_not_hidden(self) -> None:
        review = read(REVIEW)
        self.assertIn(
            "**Status: OPEN — platform policy must be reconciled before this checkbox closes.**",
            review,
        )
        manifests = (
            ROOT / "android-plugin" / "src" / "main" / "AndroidManifest.xml",
            ROOT
            / "Assets"
            / "Plugins"
            / "Android"
            / "ReachyProviderSecurity.androidlib"
            / "AndroidManifest.xml",
        )
        for manifest in manifests:
            source = read(manifest)
            self.assertNotIn('android:usesCleartextTraffic="true"', source)

    def test_cloud_boundary_requires_explicit_confirmation_and_disclosure(self) -> None:
        fallback = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Providers"
            / "ReachyProviderFallbackPolicyEngine.cs"
        )
        self.assertIn("ConfirmPrivacyBoundaryChange", fallback)
        self.assertIn("ChangesPrivacyBoundary", fallback)

        llm = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Application"
            / "ReachyLocalLlmProviderApplicationService.cs"
        )
        vlm = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Application"
            / "ReachyAndroidPerceptionApplicationService.CloudVlm.cs"
        )
        self.assertIn("ConfirmPrivacyBoundaryChange", llm)
        self.assertIn("ConfirmPrivacyBoundaryChange", vlm)

        speech = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Speech"
            / "SpeechProviderPrimitives.cs"
        )
        self.assertIn("RequiresNetworkDisclosure => MayUseNetwork", speech)

    def test_diagnostics_redact_secret_private_and_url_sensitive_data(self) -> None:
        redactor = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Diagnostics"
            / "ReachyDiagnosticRedactor.cs"
        )
        self.assertIn("ReachyDiagnosticDataClass.Secret", redactor)
        self.assertIn("ReachyDiagnosticDataClass.PrivateText", redactor)

        contract = read(
            ROOT
            / "managed"
            / "ReachyMini.Core.Tests"
            / "Rma170StructuredDiagnosticsContractTests.cs"
        )
        self.assertIn("SecretAndPrivateFieldsAreRedacted", contract)
        self.assertIn("UrlCredentialsAndQueryAreNotRetained", contract)

    def test_temporary_private_media_is_delete_on_dispose_and_purgeable(self) -> None:
        store = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Core"
            / "Application"
            / "ReachyPrivateMediaTemporaryFileStore.cs"
        )
        self.assertIn("File.Delete", store)
        self.assertIn("PurgeAbandonedFiles", store)

        contract = read(
            ROOT
            / "managed"
            / "ReachyMini.Application.Tests"
            / "Rma162PrivateMediaRetentionTests.cs"
        )
        self.assertIn("TemporaryMediaLeaseDeletesPromptly", contract)
        self.assertIn("AbandonedTemporaryMediaIsPurged", contract)

    def test_native_lifetime_uses_safe_handles_leases_and_explicit_release(self) -> None:
        safe_handle = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Interop"
            / "ReachySimSafeHandle.cs"
        )
        self.assertIn("SafeHandleZeroOrMinusOneIsInvalid", safe_handle)
        self.assertIn("ReleaseHandle", safe_handle)

        reader = read(
            ROOT
            / "Assets"
            / "ReachyMini"
            / "Runtime"
            / "Interop"
            / "ReachySimAuthoritativeStateReader.cs"
        )
        self.assertIn("Marshal.FreeHGlobal", reader)
        self.assertIn("disposed", reader)

        native = read(
            ROOT
            / "native"
            / "reachy_sim"
            / "tests"
            / "reachy_sim_concurrency_test.inc"
        )
        self.assertIn("state copy after lease release", native)
        self.assertIn("destroy primary after lease release", native)

        llama = read(
            ROOT
            / "native"
            / "llama_runtime"
            / "include"
            / "reachy_llama.h"
        )
        self.assertIn("reachy_llama_generation_release", llama)

    def test_review_documents_all_seven_roadmap_controls(self) -> None:
        review = read(REVIEW)
        for heading in (
            "## 1. Embedded credentials",
            "## 2. Android Keystore",
            "## 3. Network security configuration",
            "## 4. Provider disclosure before cloud-bound data",
            "## 5. Logs and diagnostics redaction",
            "## 6. Temporary private-media cleanup",
            "## 7. Native buffer and lifetime safety",
        ):
            self.assertIn(heading, review)


if __name__ == "__main__":
    unittest.main()

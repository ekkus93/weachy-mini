import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
CORE = ROOT / "Assets/ReachyMini/Runtime/Core"

SOURCE_SUFFIXES = {".cs", ".java", ".kt", ".xml", ".json", ".yml", ".yaml"}
SOURCE_ROOTS = (
    ROOT / "Assets/ReachyMini/Runtime",
    ROOT / "Assets/Plugins/Android",
    ROOT / "android-plugin/src",
    ROOT / "managed",
    ROOT / "scripts",
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def source_files() -> list[Path]:
    files: list[Path] = []
    for root in SOURCE_ROOTS:
        if not root.exists():
            continue
        files.extend(
            path
            for path in root.rglob("*")
            if path.is_file() and path.suffix in SOURCE_SUFFIXES
        )
    return sorted(files)


class Rma191PrivacySecurityReviewTests(unittest.TestCase):
    def test_no_embedded_provider_api_keys_in_runtime_sources(self) -> None:
        patterns = {
            "openai_style": re.compile(r"\bsk-[A-Za-z0-9_-]{20,}\b"),
            "google_style": re.compile(r"\bAIza[0-9A-Za-z_-]{30,}\b"),
            "github_pat": re.compile(r"\bgithub_pat_[0-9A-Za-z_]{40,}\b"),
        }
        matches: list[str] = []
        for path in source_files():
            text = read(path)
            for name, pattern in patterns.items():
                if pattern.search(text):
                    matches.append(f"{name}: {path.relative_to(ROOT)}")
        self.assertEqual([], matches)

    def test_provider_credentials_use_android_keystore_bridge(self) -> None:
        store = read(APP / "ReachyAndroidProviderSecretStore.cs")
        bridges = sorted(ROOT.glob("**/ReachyProviderSecretBridge.java"))
        self.assertEqual(1, len(bridges), [str(path) for path in bridges])
        bridge = read(bridges[0])
        self.assertIn("ReachyProviderSecretBridge", store)
        self.assertIn("IReachyProviderSecretStore", store)
        self.assertIn("MaximumSecretBytes", store)
        self.assertIn("AndroidKeyStore", bridge)
        self.assertIn("AES/GCM", bridge)
        self.assertIn("KeyPermanentlyInvalidatedException", bridge)
        self.assertIn("delete(", bridge)

    def test_provider_network_security_is_fail_closed(self) -> None:
        config = read(CORE / "Providers/ReachyProviderConfiguration.cs")
        llm = read(APP / "ReachyCloudLlmCredentialCoordinator.cs")
        vlm = read(APP / "ReachyCloudVlmCredentialCoordinator.cs")
        for token in (
            "ReachyProviderTlsMode.RequireHttps",
            "ReachyProviderTlsMode.TrustedLocalNetworkCleartext",
            "Provider profiles require HTTPS",
            "Cleartext provider profiles are restricted to trusted local-network hosts",
            "ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost",
            "Provider base URLs cannot contain user-info, query strings, or fragments",
        ):
            self.assertIn(token, config)
        for text in (llm, vlm):
            self.assertIn("TrustedLocalNetworkCleartext", text)
            self.assertIn("RequireHttps", text)
            self.assertIn("local-development cleartext", text)
        self.assertNotIn("DangerousAcceptAnyServerCertificateValidator", config)
        self.assertNotIn("ServerCertificateCustomValidationCallback", config)

    def test_cloud_provider_disclosure_precedes_authorization(self) -> None:
        llm = read(APP / "ReachyCloudLlmCredentialCoordinator.cs")
        vlm = read(APP / "ReachyCloudVlmCredentialCoordinator.cs")
        self.assertIn("Save an API key before authorizing cloud generation", llm)
        self.assertIn("conversation text may now be sent", llm)
        self.assertIn("Generation requests will fail closed", llm)
        self.assertIn("Save an API key before authorizing cloud analysis", vlm)
        self.assertIn("camera frames may now be sent", vlm)
        self.assertIn("Analysis requests will fail closed", vlm)

    def test_diagnostics_and_temporary_media_are_redacted_or_deleted(self) -> None:
        policy = read(CORE / "Application/ReachyPrivateMediaRetentionPolicy.cs")
        temp_store = read(CORE / "Application/ReachyPrivateMediaTemporaryFileStore.cs")
        bundle_sources = "\n".join(
            read(path) for path in sorted(ROOT.glob("**/*DiagnosticBundle*.cs"))
        )
        self.assertIn("RecordingEnabled => false", policy)
        self.assertIn("MediaExportEnabled => false", policy)
        self.assertIn("IsPersistentMediaRetentionAllowed", policy)
        self.assertIn("return false", policy)
        self.assertIn("IDisposable", temp_store)
        self.assertIn("File.Delete", temp_store)
        self.assertIn("PurgeAbandonedFiles", temp_store)
        self.assertIn("RedactedOnly", bundle_sources)
        self.assertIn("manifest.json", bundle_sources)
        self.assertIn("logs.jsonl", bundle_sources)
        self.assertIn("credential", bundle_sources.lower())
        self.assertIn("raw", bundle_sources.lower())

    def test_native_buffer_and_handle_lifetime_contracts_are_present(self) -> None:
        contract = read(ROOT / "native/reachy_sim/tests/reachy_sim_contract_test_base.inc")
        concurrency = read(ROOT / "native/reachy_sim/tests/reachy_sim_concurrency_test.inc")
        self.assertIn("ReachySimHandle layout changed", contract)
        self.assertIn("stale handle after destroy", contract)
        self.assertIn("operation after close", contract)
        self.assertIn("invalid state copy leaves required size unchanged", concurrency)
        self.assertIn("busy state copy leaves output unchanged", concurrency)
        self.assertIn("busy snapshot copy leaves output unchanged", concurrency)


if __name__ == "__main__":
    unittest.main()

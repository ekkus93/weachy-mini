#!/usr/bin/env python3
"""RMA-191 network-security reconciliation contracts."""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOC = ROOT / "docs/RMA_191_NETWORK_SECURITY_RECONCILIATION_2026-09-25.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma191NetworkSecurityReconciliationTests(unittest.TestCase):
    def test_document_records_platform_application_split_policy(self) -> None:
        doc = read(DOC)
        for token in (
            "no checked-in Android manifest grants blanket",
            "RequirePublicHttpsUri",
            "ReachyProviderTlsMode.RequireHttps",
            "TrustedLocalNetworkCleartext",
            "IsTrustedLocalNetworkHost",
            "disables automatic redirects",
            "privacy-boundary confirmation",
            "Android Network Security Config is a static manifest resource",
        ):
            self.assertIn(token, doc)

    def test_no_checked_in_manifest_grants_blanket_cleartext(self) -> None:
        manifest_roots = (
            ROOT / "android-plugin" / "src" / "main",
            ROOT / "Assets" / "Plugins" / "Android",
        )
        manifests = [path for root in manifest_roots for path in root.rglob("AndroidManifest.xml")]
        self.assertTrue(manifests)
        for manifest in manifests:
            source = read(manifest)
            self.assertNotIn('android:usesCleartextTraffic="true"', source)

    def test_application_policy_rejects_public_cleartext_and_redirect_weakening(self) -> None:
        endpoint_security = read(
            ROOT / "Assets/ReachyMini/Runtime/Core/Security/ReachyImportedContentSecurity.cs"
        )
        provider_config = read(
            ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderConfiguration.cs"
        )
        transport = read(
            ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachySharedHttpTransport.Helpers.cs"
        )
        fallback = read(
            ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderFallbackPolicyEngine.cs"
        )
        for token in (
            "RequirePublicHttpsUri",
            "IsTrustedLocalNetworkHost",
            "IsPublicInternetHost",
        ):
            self.assertIn(token, endpoint_security)
        for token in (
            "ReachyProviderTlsMode.RequireHttps",
            "ReachyProviderTlsMode.TrustedLocalNetworkCleartext",
            "Cleartext provider profiles are restricted to trusted local-network hosts.",
        ):
            self.assertIn(token, provider_config)
        for token in (
            "AllowAutoRedirect = false",
            "uri.Host",
            "profile.BaseUri.Host",
            "uri.Port != profile.BaseUri.Port",
            "IsTrustedLocalNetworkHost(uri)",
        ):
            self.assertIn(token, transport)
        self.assertIn("ConfirmPrivacyBoundaryChange", fallback)


if __name__ == "__main__":
    unittest.main()

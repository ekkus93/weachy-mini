#!/usr/bin/env python3
"""Compatibility guard for the canonical RMA-191 security/privacy gate.

The comprehensive release review lives in test_rma191_security_privacy_review.py
and is also exercised by the dedicated RMA-191 workflow. Keep this small guard
so the older filename cannot silently diverge into a second security policy.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CANONICAL = ROOT / "scripts/tests/test_rma191_security_privacy_review.py"
DEDICATED_WORKFLOW = ROOT / ".github/workflows/rma191-security-privacy-review.yml"
RELEASE_WORKFLOW = ROOT / ".github/workflows/rma-release-review-contracts.yml"


class Rma191SecurityReviewCompatibilityTests(unittest.TestCase):
    def test_release_review_uses_canonical_security_privacy_gate(self) -> None:
        canonical = CANONICAL.read_text(encoding="utf-8")
        dedicated = DEDICATED_WORKFLOW.read_text(encoding="utf-8")
        release = RELEASE_WORKFLOW.read_text(encoding="utf-8")
        for token in (
            "test_no_embedded_production_credentials",
            "test_android_keystore_storage_is_authenticated_and_non_plaintext",
            "test_android_and_provider_network_policy_does_not_enable_global_cleartext",
            "test_cloud_bound_audio_image_and_text_are_disclosed_in_visible_settings",
            "test_diagnostics_redact_secrets_private_text_audio_images_and_media",
            "test_temporary_private_media_is_deleted_and_abandoned_files_are_purged",
            "test_native_handles_buffers_and_session_lifetimes_have_bounded_ownership",
        ):
            self.assertIn(token, canonical)
        self.assertIn("test_rma191_security_privacy_review.py", dedicated)
        self.assertIn("test_rma191_security_privacy_review.py", release)


if __name__ == "__main__":
    unittest.main(verbosity=2)

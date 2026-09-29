# RMA-191 Security Review Regression Contract — 2026-09-29

## Purpose

RMA-191 is a release review, not a one-time prose exercise. The permanent regression contract at `scripts/tests/test_rma191_security_review.py` ties the final review to shipping source invariants and existing subsystem evidence.

The contract intentionally does **not** mark any RMA-191 roadmap checkbox complete by itself. Physical Android Keystore/device-lock behavior, actual packaged network configuration, provider disclosure UX, diagnostic export behavior, temporary-media cleanup, and native lifetime/buffer safety retain their subsystem-specific acceptance evidence requirements.

## Mechanically guarded invariants

The suite rejects an obvious token-shaped OpenAI secret literal in production source; requires the Android Keystore secret implementation to retain `AndroidKeyStore`, AES-GCM, permanent-key-invalidation handling, and credential lifecycle integration; requires cloud-bound provider switching to retain explicit fallback-policy evaluation and privacy-boundary confirmation; requires explicit trusted-local cleartext policy rather than an unrestricted cleartext fallback; binds diagnostic export to redacted-only/default-sensitive-data exclusion contracts; binds private-media temporary storage to delete/purge and no-retention contracts; and binds native release review to the versioned ABI's bounded-copy, stale-handle, busy-handle, destruction, and copied-error contracts.

## Qualification rule

This file and the source gate are implementation evidence only until an exact-head static validation actually executes `scripts/tests/test_rma191_security_review.py`. Hosted CI/bootstrap failures or a Ralph local-validation transport error are infrastructure outcomes and must not be represented as a passing security review.

The final RMA-191 roadmap reconciliation must additionally cite the relevant physical/security acceptance evidence for each checkbox. No checkbox should be closed solely because source strings exist.

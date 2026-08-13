# RMA-161 Credential Lifecycle — Implementation Checkpoint

**Task:** RMA-161 — Implement credential lifecycle  
**Date:** 2026-08-13  
**Status:** Core implementation checkpoint; physical Android acceptance pending

## Scope implemented

This checkpoint extends the existing RMA-140 Android Keystore-backed provider secret boundary instead of introducing a second credential store.

- `ReachyProviderCredentialLifecycle` gives create, update, read, delete, and provider-removal operations distinct semantics.
- Provider removal identifies credential and secret-header references that are exclusive to the removed profile; references still used by another profile are retained.
- Provider removal snapshots only the orphaned credential bytes needed for rollback, zeroes those temporary arrays, and restores both profile metadata and credential material if deletion fails.
- The Android bridge continues to store only AES-GCM ciphertext and IV metadata in private `SharedPreferences`; the encryption key remains in `AndroidKeyStore`.
- Keystore loss or invalidation fails closed while encrypted records remain. The bridge does not silently generate a replacement key over unreadable ciphertext.
- Deleting the last encrypted record removes the shared Keystore alias so a later explicit credential creation can start from a clean key lifecycle.
- Normal credential keys explicitly do not require per-use user authentication. Lock-state and key-presence probes exist only for acceptance evidence.
- A key-invalidation test hook is restricted to debuggable application builds.
- Provider exports continue to expose only redacted configuration state, not credential values or credential references.

## Tests added

`Assets/ReachyMini/Tests/Editor/ReachyProviderCredentialLifecycleTests.cs` covers:

1. distinct create/update/read/delete behavior;
2. shared credential references surviving deletion of one provider;
3. exclusive credential references being removed with a provider;
4. transactional rollback when credential deletion fails;
5. redacted export excluding both full secret values and secret-reference identifiers.

`scripts/tests/test_rma161_credential_lifecycle.py` locks the Android Keystore/AES-GCM boundary, no-plaintext persistence, fail-closed key-loss behavior, debuggable-only invalidation hook, managed cleanup ownership, zeroing, and redaction fixtures.

## Local validation available in this sandbox

The sandbox does not provide Unity, `dotnet`, `csc`, `mcs`, or an Android SDK platform jar, so managed/Unity execution and a real Android Gradle compile are not claimed here.

The Java bridge was compiled against minimal Android API stubs using:

```text
javac -Xlint:all -Werror
```

That compile passed.

The new Python regression passed `python3 -m py_compile` and a focused repository-shaped invocation with **5/5 tests passing**.

The new C#, Java, and Python source files were scanned for line length and obvious secret-to-string/logging paths; no production credential lifecycle source converts secret bytes to strings or logs them.

## Remaining RMA-161 acceptance work

RMA-161 remains open until physical Android evidence proves:

- credential reads survive an ordinary device lock/unlock transition under the selected non-auth-gated key policy;
- simulated Keystore alias loss fails closed while encrypted records remain and requires explicit record deletion before replacement;
- app-data clearing removes credential records;
- device evidence/log exports do not contain the full test credential.

A dedicated physical acceptance harness and workflow artifact are the next Ralph-loop checkpoint.

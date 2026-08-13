# RMA-161 Credential Lifecycle — Validation

**Task:** RMA-161 — Implement credential lifecycle  
**Date:** 2026-08-13  
**Status:** Implementation and physical gate committed; redundant reinstall blocker fixed; acceptance rerun pending

## Scope implemented

RMA-161 extends the existing RMA-140 Android Keystore-backed provider secret boundary instead of introducing a second credential store.

- `ReachyProviderCredentialLifecycle` gives create, update, read, delete, and provider-removal operations distinct semantics.
- Provider removal identifies credential and secret-header references that are exclusive to the removed profile; references still used by another profile are retained.
- Provider removal snapshots only the orphaned credential bytes needed for rollback, zeroes those temporary arrays, and restores both profile metadata and credential material if deletion fails.
- The Android bridge continues to store only AES-GCM ciphertext and IV metadata in private `SharedPreferences`; the encryption key remains in `AndroidKeyStore`.
- Keystore loss or invalidation fails closed while encrypted records remain. The bridge does not silently generate a replacement key over unreadable ciphertext.
- Deleting the last encrypted record removes the shared Keystore alias so a later explicit credential creation can start from a clean key lifecycle.
- Normal credential keys explicitly do not require per-use user authentication. Lock-state and key-presence probes exist only for acceptance evidence.
- A key-invalidation test hook is restricted to debuggable application builds.
- Provider exports continue to expose only redacted configuration state, not credential values or credential references.

## Managed tests added

`Assets/ReachyMini/Tests/Editor/ReachyProviderCredentialLifecycleTests.cs` covers:

1. distinct create/update/read/delete behavior;
2. shared credential references surviving deletion of one provider;
3. exclusive credential references being removed with a provider;
4. transactional rollback when credential deletion fails;
5. redacted export excluding both full secret values and secret-reference identifiers.

## Static regressions

`scripts/tests/test_rma161_credential_lifecycle.py` locks the Android Keystore/AES-GCM boundary, no-plaintext persistence, fail-closed key-loss behavior, debuggable-only invalidation hook, managed cleanup ownership, zeroing, and redaction fixtures.

`scripts/tests/test_rma161_credential_physical_acceptance.py` locks the four-phase Android acceptance contract, real keyguard transition checks, real app-data clearing, text-evidence secret scanning, and exact-SHA APK reuse in the dedicated physical workflow. It also rejects any reintroduction of the redundant `adb install -r -g` path and requires fail-closed installed-APK SHA-256 provenance.

## Physical Android gate

`Assets/ReachyMini/Runtime/Application/ReachyRma161CredentialAcceptance.cs` and `scripts/run_rma161_credential_acceptance_android.sh` implement four explicit phases:

1. `prepare` — create, read, update, and reread a synthetic credential through the production Android Keystore store;
2. `verify-after-lock` — after the shell proves the device entered and left keyguard, read the same updated credential again;
3. `invalidate` — delete the Keystore alias through a debuggable-build-only test hook, require read and update to fail closed while ciphertext remains, explicitly delete the unreadable record, recreate a clean key, exercise real provider-deletion credential cleanup, and leave a credential for app-data-clear testing;
4. `verify-cleared` — after a real `adb shell pm clear`, require every test credential record to be absent and prove a fresh create/read/delete cycle still works.

The shell captures phase JSON, logcat, screenshots, UI hierarchy dumps, keyguard evidence, APK/report hashes, and device metadata. It rejects any full synthetic credential marker appearing in collected text evidence. Reports intentionally contain only booleans and generic failure type names, never secret values.

The dedicated `.github/workflows/rma161-credential-lifecycle.yml` runs only after `Local Unity Android Validation` succeeds. It checks out that exact source SHA and downloads the exact device APK artifact from the successful upstream run instead of rebuilding or substituting a different APK.

The first physical child run reached a redundant `adb install -r -g` in the RMA-161 shell and then consumed the 30-minute job timeout before any credential phase began. The parent workflow had already installed and validated the exact APK, so the second install added no acceptance value. The shell now resolves the installed package's unique `base.apk`, streams its bytes over ADB, computes its SHA-256, and requires it to match the downloaded upstream artifact exactly. Missing, unreadable, ambiguous, or mismatched installed APK state fails closed. On success it records both hashes, the installed path, and `reinstall_skipped=true` in `installed-apk-provenance.txt` and proceeds without another package-manager install.

## Local validation available in this sandbox

The sandbox does not provide Unity, `dotnet`, `csc`, `mcs`, ShellCheck, actionlint, or an Android SDK platform jar, so those exact gates are not claimed locally.

The original RMA-161 implementation validation completed successfully with the available Java/Python/shell checks documented for the implementation checkpoint. The reinstall-blocker correction was additionally validated with:

```text
python3 -m py_compile           # updated physical-gate regression
python3 -m unittest             # new exact installed-APK reuse regression: 1/1 passed
bash -n                         # updated physical Android acceptance script
fake-ADB provenance harness     # matching bytes accepted; mismatched bytes rejected
```

The modified shell and Python files were also checked against the repository's 100-character source-line convention; no violation was found.

## Remaining validation before closeout

RMA-161 remains open until the dedicated physical workflow reruns on the corrected script and produces a passing exact-SHA artifact proving the lock transition, fail-closed key loss, provider deletion, app-data clear, evidence redaction, and installed-APK provenance behavior. The redundant reinstall blocker was removed by commit `6d6a3c903b2d4b9312caef5db5fc33c3e058949c`; the static no-reinstall/provenance contract was added by `ac48371553822c7886c8b93ef58d17fb1baf5994`.

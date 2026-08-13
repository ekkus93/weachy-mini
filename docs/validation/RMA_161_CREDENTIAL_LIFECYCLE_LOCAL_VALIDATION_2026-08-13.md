# RMA-161 Credential Lifecycle — Validation

**Task:** RMA-161 — Implement credential lifecycle  
**Date:** 2026-08-13  
**Status:** Implementation and physical gate committed; acceptance execution pending

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

`scripts/tests/test_rma161_credential_physical_acceptance.py` locks the four-phase Android acceptance contract, real keyguard transition checks, real app-data clearing, text-evidence secret scanning, and exact-SHA APK reuse in the dedicated physical workflow.

## Physical Android gate

`Assets/ReachyMini/Runtime/Application/ReachyRma161CredentialAcceptance.cs` and `scripts/run_rma161_credential_acceptance_android.sh` implement four explicit phases:

1. `prepare` — create, read, update, and reread a synthetic credential through the production Android Keystore store;
2. `verify-after-lock` — after the shell proves the device entered and left keyguard, read the same updated credential again;
3. `invalidate` — delete the Keystore alias through a debuggable-build-only test hook, require read and update to fail closed while ciphertext remains, explicitly delete the unreadable record, recreate a clean key, exercise real provider-deletion credential cleanup, and leave a credential for app-data-clear testing;
4. `verify-cleared` — after a real `adb shell pm clear`, require every test credential record to be absent and prove a fresh create/read/delete cycle still works.

The shell captures phase JSON, logcat, screenshots, UI hierarchy dumps, keyguard evidence, APK/report hashes, and device metadata. It rejects any full synthetic credential marker appearing in collected text evidence. Reports intentionally contain only booleans and generic failure type names, never secret values.

The dedicated `.github/workflows/rma161-credential-lifecycle.yml` runs only after `Local Unity Android Validation` succeeds. It checks out that exact source SHA and downloads the exact device APK artifact from the successful upstream run instead of rebuilding or substituting a different APK.

## Local validation available in this sandbox

The sandbox does not provide Unity, `dotnet`, `csc`, `mcs`, ShellCheck, actionlint, or an Android SDK platform jar, so those exact gates are not claimed locally.

Available checks completed successfully:

```text
javac -Xlint:all -Werror        # Android bridge against minimal Android API stubs
python3 -m py_compile           # both RMA-161 Python regressions
python3 -m unittest             # 5/5 core static tests
python3 -m unittest             # 3/3 physical-gate static tests
bash -n                         # physical Android acceptance script
```

A YAML parser also accepted the dedicated workflow structure. C#, Java, shell, and Python additions were scanned for repository line-length conventions; no newly added source line exceeds 100 characters except where the repository format does not impose that bound.

## Remaining validation before closeout

RMA-161 remains open until repository CI executes the new managed/static contracts and the dedicated physical workflow produces a passing exact-SHA artifact proving the lock transition, fail-closed key loss, provider deletion, app-data clear, and evidence redaction behavior.

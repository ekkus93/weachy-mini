# RMA-161 Credential Lifecycle — Validation

**Task:** RMA-161 — Implement credential lifecycle  
**Date:** 2026-08-13  
**Status:** Implementation committed; manual physical closeout still required

## Scope implemented

RMA-161 extends the existing RMA-140 Android Keystore-backed provider secret boundary instead of introducing a second credential store.

- `ReachyProviderCredentialLifecycle` gives create, update, read, delete, and provider-removal operations distinct semantics.
- Provider removal retains credential references still shared by another profile and removes only orphaned references.
- Provider removal snapshots only rollback material, zeroes temporary arrays, and restores profile metadata plus credential bytes if deletion fails.
- The Android bridge stores AES-GCM ciphertext and IV metadata in private `SharedPreferences`; the key remains in `AndroidKeyStore`.
- Keystore loss or invalidation fails closed while encrypted records remain; unreadable ciphertext is never silently overwritten by a replacement key.
- Deleting the final encrypted record removes the shared Keystore alias so an explicit later create starts a fresh lifecycle.
- Normal credentials do not require per-use user authentication. Lock/key-presence probes and key invalidation are acceptance-only; invalidation is restricted to debuggable builds.
- Provider diagnostics and exports remain redacted and do not expose full secret values or secret-reference identifiers.

## Managed and static coverage

`Assets/ReachyMini/Tests/Editor/ReachyProviderCredentialLifecycleTests.cs` covers distinct CRUD semantics, shared-reference preservation, provider-delete cleanup, transactional rollback, and redacted export behavior.

`scripts/tests/test_rma161_credential_lifecycle.py` locks the Keystore/AES-GCM boundary, no-plaintext persistence, fail-closed key loss, debuggable-only invalidation, cleanup ownership, zeroing, and redaction fixtures.

`scripts/tests/test_rma161_credential_physical_acceptance.py` locks the four-phase Android acceptance contract, exact installed-APK provenance, real keyguard evidence, real app-data clear, full-secret text-evidence scanning, fail-closed device preparation, the manual exact-SHA workflow contract, and separation of the terminal lock test from routine physical validation.

## Physical Android gate

`Assets/ReachyMini/Runtime/Application/ReachyRma161CredentialAcceptance.cs` and `scripts/run_rma161_credential_acceptance_android.sh` implement four phases:

1. `prepare` — create/read/update/read a synthetic credential through the production Android Keystore store;
2. `verify-after-lock` — prove the same credential remains readable after the shell establishes a real secure-keyguard state;
3. `invalidate` — remove the Keystore alias through the debuggable test hook, require read/update to fail closed while ciphertext remains, explicitly delete unreadable state, recreate a clean key, exercise provider-deletion cleanup, and leave a credential for app-data-clear verification;
4. `verify-cleared` — after a real `pm clear`, require test credential records to be absent and prove a fresh create/read/delete cycle still works.

The shell captures JSON reports, logcat, screenshots, UI hierarchy dumps, keyguard evidence, device metadata, and APK/report hashes. It rejects any full synthetic credential marker found in collected text evidence.

The physical script also resolves the installed package's unique `base.apk`, streams and hashes it, and requires an exact SHA-256 match with the downloaded validated APK. It does not perform a redundant reinstall.

## Runner-state findings and corrections

One physical run proved `prepare` succeeded, then exposed that the dedicated Android device has a real PIN lock. Android correctly refused to bypass that credential. The RMA-161 locked phases were therefore changed to execute while secure keyguard remains active instead of requiring CI to know or type a PIN.

A later child run waited long enough for the shared runner phone to relock before RMA-161 began. That revealed a fail-open bug in `scripts/android_device_acceptance_foreground.sh`: after unsuccessful preparation attempts it returned success. Commit `b6bbb6782ae3daf7820471fa32c734e450166d1b` corrected that behavior so a blocking secure keyguard now fails closed with explicit diagnostics.

Embedding the RMA-161 lock test into every normal `Local Unity Android Validation` push was subsequently found to be operationally wrong. A successful RMA-161 run intentionally leaves the phone in a secure locked state, which makes the next routine camera job fail before RMA-090. Routine authoritative rendering therefore no longer invokes RMA-161.

`.github/workflows/rma161-credential-lifecycle.yml` is now an explicit `workflow_dispatch` closeout workflow. It requires:

- `validated_sha` — the exact source SHA already accepted by `Local Unity Android Validation`;
- `validated_run_id` — the successful parent run containing `local-unity-device-apk-<sha>`.

The workflow checks out that exact SHA, downloads that exact run's APK, verifies `git rev-parse HEAD`, then runs the physical RMA-161 script on the self-hosted Android runner. This keeps the destructive lock test deliberate and one-time instead of coupling it to every push.

Routine physical acceptance now parks the dedicated runner awake after cleanup by leaving `svc power stayon true` and waking the display. This does not bypass keyguard. It only prevents an already-unlocked dedicated test phone from auto-sleeping and relocking between ordinary CI runs.

## Local validation boundary

The sandbox does not provide Unity, `dotnet`, Ruff, ShellCheck, actionlint, or a physical Android device, so those exact gates are not claimed locally. Earlier RMA-161 checkpoints passed the available Java/Python/shell checks, including the fake-ADB exact-APK provenance harness. Current changes are covered by the permanent Python contract plus shell/YAML syntax checks available to repository CI.

## Remaining validation before closeout

RMA-161 remains open until the manual exact-SHA physical workflow passes all four phases and produces complete provenance/redaction evidence. Because that acceptance deliberately establishes a secure keyguard state, the dedicated phone must be manually unlocked once after the closeout run before routine physical CI resumes. No device PIN is stored, typed, or bypassed by CI.

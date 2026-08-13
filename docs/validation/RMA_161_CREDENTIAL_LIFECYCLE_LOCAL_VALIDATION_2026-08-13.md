# RMA-161 Credential Lifecycle — Validation

**Task:** RMA-161 — Implement credential lifecycle  
**Date:** 2026-08-13  
**Status:** Implementation committed; inline physical rerun and evidence verification pending

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

`scripts/tests/test_rma161_credential_physical_acceptance.py` locks the four-phase Android acceptance contract, real keyguard transition checks, real app-data clearing, text-evidence secret scanning, exact-SHA APK reuse, fail-closed foreground preparation, execution before the physical-device wrapper releases the phone, and exact-parent evidence handoff. It rejects attempts to dismiss or type a device credential into the locked test path and rejects a second self-hosted RMA-161 child run.

## Physical Android gate

`Assets/ReachyMini/Runtime/Application/ReachyRma161CredentialAcceptance.cs` and `scripts/run_rma161_credential_acceptance_android.sh` implement four explicit phases:

1. `prepare` — create, read, update, and reread a synthetic credential through the production Android Keystore store while the runner device is normally unlocked;
2. `verify-after-lock` — after the shell proves the device entered keyguard, read the same updated credential while keyguard remains actively showing and unoccluded;
3. `invalidate` — while the secure keyguard is still active, delete the Keystore alias through a debuggable-build-only test hook, require read and update to fail closed while ciphertext remains, explicitly delete the unreadable record, recreate a clean key, exercise real provider-deletion credential cleanup, and leave a credential for app-data-clear testing;
4. `verify-cleared` — after a real `adb shell pm clear`, while keyguard remains active, require every test credential record to be absent and prove a fresh create/read/delete cycle still works.

The shell captures phase JSON, logcat, screenshots, UI hierarchy dumps, keyguard evidence, APK/report hashes, and device metadata. It rejects any full synthetic credential marker appearing in collected text evidence. Reports intentionally contain only booleans and generic failure type names, never secret values.

The physical script now executes from `scripts/run_unity_authoritative_rendering_acceptance_android.sh` after authoritative rendering succeeds but before that wrapper's exit trap restores/releases the physical device. Its evidence is written under `rma161-credential-report/` inside the authoritative physical report artifact. The dependent `.github/workflows/rma161-credential-lifecycle.yml` no longer reacquires the Android runner; after an exact successful `Local Unity Android Validation` run it uses `ubuntu-latest` to download that run's authoritative artifact and require the embedded RMA-161 reports and provenance evidence.

## Physical-gate corrections

The first physical child run reached a redundant `adb install -r -g` in the RMA-161 shell and then consumed the 30-minute job timeout before any credential phase began. The parent workflow had already installed and validated the exact APK, so the second install added no acceptance value. The shell now resolves the installed package's unique `base.apk`, streams its bytes over ADB, computes its SHA-256, and requires it to match the downloaded upstream artifact exactly. Missing, unreadable, ambiguous, or mismatched installed APK state fails closed. On success it records both hashes, the installed path, and `reinstall_skipped=true` in `installed-apk-provenance.txt` and proceeds without another package-manager install.

A later physical run on exact SHA `93b8098528112665982162c8c2368163c0a494f2` proved that `prepare` passed completely: credential create/read/update/read succeeded and the Keystore key was present. The subsequent lock step then failed because the self-hosted runner phone has an actual PIN credential. The captured failure screenshot showed the Android `Enter PIN to unlock` keypad, while `dumpsys activity activities` retained `mKeyguardShowing=true` and `mOccluded=false`. `wm dismiss-keyguard` correctly did not bypass that credential.

RMA-161 does not require CI to know or weaken the runner's PIN. Commit `809a9c5be19cb5452ad28557de38c0e4a74f2b26` changed the physical contract so the locked phases execute behind the still-active keyguard. Each locked phase requires `mKeyguardShowing=true` and `mOccluded=false` before and after the application work. This directly tests the production key configured with `setUserAuthenticationRequired(false)` under a real secure device lock. The script contains no PIN input path and does not attempt to dismiss keyguard after the lock transition.

The next exact physical child run, GitHub Actions run `31745381576` on SHA `6122524e3e8b8832b4593219705d665289b079d4`, failed before `prepare` evidence with `Unity application exited before RMA-161 phase prepare evidence.` The child had waited roughly 30 minutes for the self-hosted `kawa` runner after the successful parent validation. Its artifact `9199734454` proved the installed APK still matched the exact parent artifact, but by job start the phone had independently relocked behind its PIN. The foreground helper also had a fail-open defect: after three unsuccessful attempts to clear a blocking keyguard, `prepare_device()` fell off the end and returned success.

Commit `b6bbb6782ae3daf7820471fa32c734e450166d1b` fixes that helper defect by returning failure with explicit power/keyguard diagnostics whenever an unoccluded PIN, pattern, or password keyguard remains. Commit `56204e299d733952e9ad2aef5be3495a1d619147` removes the queue-time race from RMA-161 itself by invoking the credential acceptance from the already-running authoritative physical wrapper before that wrapper releases the device. Commit `f651cb91ae3f53d5f068dcf443c4ee8b2c9a1cc6` changes the dependent RMA-161 workflow into a hosted exact-parent evidence check instead of a second physical-device job.

An abandoned headless-receiver experiment was not adopted. The remaining receiver source was neutralized to an inert no-op after repository automation rejected its deletion in the same change sequence; it has no intent filter and performs no credential or application operation. It is not part of the RMA-161 acceptance path.

## Local validation available in this sandbox

The sandbox does not provide Unity, `dotnet`, `csc`, `mcs`, Ruff, ShellCheck, actionlint, or an Android SDK platform jar, so those exact gates are not claimed locally.

The original RMA-161 implementation validation completed successfully with the available Java/Python/shell checks documented for the implementation checkpoint. The exact-installed-APK correction was additionally validated with Python syntax/static checks, `bash -n`, and a fake-ADB provenance harness before publication. The current queue/relock correction is validated structurally by the permanent Python regression and shell syntax checks available in this environment; the actual lock/Keystore behavior remains intentionally assigned to the physical runner.

## Remaining validation before closeout

RMA-161 remains open until `Local Unity Android Validation` executes the embedded RMA-161 physical gate successfully and the dependent hosted RMA-161 workflow confirms the exact parent artifact contains the complete phase/provenance evidence. No device PIN is required or authorized for this acceptance path.

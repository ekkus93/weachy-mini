# RMA-161 Credential Lifecycle

## Status

Implementation specification for RMA-161. This task owns provider credential lifecycle on top of the existing RMA-140 provider-profile and Android secret-store boundary. It does not move secret values into settings or profile JSON.

## Credential ownership

Credential bytes are owned only by `IReachyProviderSecretStore`. Provider profiles retain stable secret references; RMA-160 general settings retain only non-secret provider-profile selection identifiers.

The Android production store delegates to `ReachyProviderSecretBridge`, which uses an AES-GCM key held by `AndroidKeyStore` and persists only the per-record initialization vector and authenticated ciphertext in private application `SharedPreferences`.

## Lifecycle contract

RMA-161 distinguishes operations that the low-level upsert-capable store previously exposed through `Put`:

- create requires the reference to be absent;
- update requires the reference to exist;
- read returns bounded credential bytes to the caller, which owns clearing them after use;
- delete is idempotent and returns whether a record existed;
- provider removal deletes only credential references no longer used by any surviving provider profile.

Provider removal is transactional across profile metadata and orphaned credential material. Orphan bytes are temporarily retained only for rollback and are zeroed on every completion path. If secret deletion fails, the profile and any already-deleted credentials are restored; an incomplete rollback becomes an explicit aggregate failure.

## Keystore invalidation contract

Encrypted records and their Keystore key are treated as one integrity domain.

If the key is missing, unrecoverable, or permanently invalidated while encrypted records still exist, reads and updates fail closed with an RMA-161 key-unavailable diagnostic. The bridge does not silently create a replacement key that would make existing ciphertext permanently unreadable while appearing healthy.

Explicit deletion remains possible without decrypting a record. When the final encrypted record is deleted, the shared Keystore alias is removed. A later explicit create may then generate a new key.

The production key does not require per-use user authentication. Ordinary screen lock/unlock therefore must not itself become an implicit credential-unavailable state. A debuggable-build-only key deletion hook exists solely to exercise the otherwise difficult-to-induce invalidation/loss recovery path in physical acceptance.

## Privacy contract

Secret bytes are never persisted as strings, included in profile exports, logged by credential lifecycle code, or exposed in diagnostics. Provider diagnostics disclose only whether a credential is configured. Temporary managed rollback arrays and temporary Java encryption buffers are cleared when no longer needed.

RMA-161 physical evidence must use a synthetic bounded test credential and assert that its full marker is absent from collected log/evidence text.

## App-data clearing

Provider ciphertext metadata is stored in application-private data, so an app-data clear removes the credential records. Physical acceptance must prove that the application observes no configured credential after a real `pm clear` cycle before RMA-161 is closed.

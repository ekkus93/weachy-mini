# RMA-191 Privacy and Security Review — 2026-09-23

This review records the repository evidence for the RMA-191 release gate. It is
paired with `scripts/tests/test_rma191_privacy_security_review.py`, which turns
the reviewed invariants into a repeatable static contract.

No RMA-191 roadmap checkbox should be reconciled merely because this document
exists. The exact review head must pass CI, and the Android platform network
policy item below remains explicitly open.

## 1. Embedded credentials

**Reviewed control:** no production API keys, bearer tokens, private keys, or
other high-confidence credential material are committed in the production
source roots.

The regression contract scans the runtime, Android provider-security plugin,
Android plugin source, and native source trees for high-confidence secret
patterns. Test fixtures and documentation are deliberately outside that
production-only scan.

Provider profiles persist only secret *references*. Secret bytes are supplied
through `IReachyProviderSecretStore`; the production Android implementation is
`ReachyAndroidProviderSecretStore`.

## 2. Android Keystore

**Reviewed control:** provider credentials use an Android-Keystore-backed AES
key and do not store plaintext credentials in SharedPreferences.

`ReachyProviderSecretBridge.java` uses:

- `AndroidKeyStore`;
- AES/GCM authenticated encryption;
- randomized encryption;
- the secret reference as additional authenticated data;
- bounded ciphertext/IV persistence;
- explicit byte-array clearing on temporary plaintext/ciphertext buffers;
- key deletion after the final stored credential is removed.

The managed `ReachyAndroidProviderSecretStore` is the production bridge used
by cloud provider credential lifecycle code.

## 3. Network security configuration

**Status: OPEN — platform policy must be reconciled before this checkbox closes.**

The application-level network policy is fail-closed and reviewed:

- public remote model URLs require public HTTPS;
- provider profiles using `RequireHttps` remain HTTPS-only after redirects;
- cleartext provider endpoints are accepted only when the endpoint is an
  unambiguously trusted local-network host;
- local-network and cloud targets still cross a privacy/network boundary and
  require explicit fallback-policy authorization.

However, no checked-in Android manifest currently declares an explicit
`android:networkSecurityConfig` or `android:usesCleartextTraffic` policy.
That is intentionally not papered over in this review. The product also
intentionally supports trusted LAN HTTP provider endpoints, so a blanket
cleartext prohibition would conflict with existing product behavior, while a
blanket cleartext allowance would weaken platform defense-in-depth.

Before the RMA-191 network-security checkbox closes, the merged Android
application policy must explicitly reconcile that platform behavior with the
existing trusted-LAN endpoint policy.

## 4. Provider disclosure before cloud-bound data

**Reviewed control:** network/privacy-boundary changes require explicit policy
authorization instead of silently switching providers.

`ReachyProviderFallbackPolicyEngine` requires a matching one-time privacy
boundary confirmation for cross-boundary provider switches. LLM and VLM
shipping composition paths invoke that confirmation before creating cloud
provider traffic. Speech descriptors expose `RequiresNetworkDisclosure` when
network use is possible.

The settings UI also labels the configured cloud/local-network privacy
boundary rather than presenting all compatible endpoints as equivalent.

## 5. Logs and diagnostics redaction

**Reviewed control:** secrets and private text are redacted before structured
diagnostic records are emitted or exported.

`ReachyDiagnosticRedactor` removes secret/private values and strips URL user
information, query strings, and fragments. RMA-170 contract tests verify those
behaviors.

## 6. Temporary private-media cleanup

**Reviewed control:** microphone/image media is not persistently retained by
default, and temporary media is deleted promptly.

`ReachyPrivateMediaRetentionPolicy` defaults persistent recording/export to
disabled. `ReachyPrivateMediaTemporaryFileStore` owns temporary media under a
dedicated cache root, deletes leased files on disposal, and can purge abandoned
files. RMA-162 application tests exercise both lease deletion and abandoned-file
purge behavior.

## 7. Native buffer and lifetime safety

**Reviewed control:** managed/native ownership boundaries use explicit lifetime
objects and bounded buffers.

The simulation handle is wrapped by `ReachySimSafeHandle`. The authoritative
state reader owns and frees its unmanaged buffer and rejects use after
disposal. Native simulation concurrency tests cover output-buffer sizing,
operation leases, state copies after lease release, and destruction only after
leases complete.

The local-LLM ABI exposes explicit generation release and the managed runtime
owns the cancel/drain/release lifecycle. Existing local-LLM tests cover cleanup
and OOM paths.

## Remaining action

The only review item this pass intentionally leaves unresolved is the explicit
Android platform network-security configuration. The other six controls are
captured by the repository contract added with this review, but roadmap
reconciliation still waits for exact-head CI evidence.

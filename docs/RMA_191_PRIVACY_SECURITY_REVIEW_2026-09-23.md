# RMA-191 Privacy and Security Review

Date: 2026-09-23

This review records the current implementation evidence for the seven
RMA-191 release-security checks. It is intentionally not used to mark the
roadmap complete until the exact branch head has executed in CI and the
review is merged to `master`.

## Review scope

The review covers shipping runtime code, Android provider-secret storage,
provider transport policy, privacy-boundary authorization, diagnostics,
temporary private media, and native simulation lifetime management.

## Findings

### No embedded keys

No private-key blocks or OpenAI-style live API-key tokens are expected in
tracked runtime, managed-test, or script sources. The accompanying
`scripts/tests/test_rma191_security_review.py` performs the bounded
repository scan on every static test run. Provider credentials are stored by
reference and are not serialized into durable provider profiles.

### Android Keystore usage

`ReachyAndroidProviderSecretStore` delegates storage to
`ReachyProviderSecretBridge`. The Android bridge obtains an AES key from
`AndroidKeyStore` and encrypts secret material with
`AES/GCM/NoPadding`. The managed secret store does not use Unity
`PlayerPrefs` for provider credentials.

### Network security configuration

The project intentionally supports two transport modes rather than a blanket
Android cleartext ban:

- public/remote provider endpoints require HTTPS;
- explicit trusted-local cleartext is limited to loopback, private/link-local
  literals, and local-only DNS names.

`ReachyProviderProfile` validates the selected TLS mode,
`ReachyNetworkEndpointSecurity` rejects untrusted public HTTP targets, and
`ReachySharedHttpTransport` revalidates scheme, host, and port for the final
request URI. This preserves supported local Ollama/llama-server workflows
while failing closed for public cleartext endpoints and redirect changes.

### Provider disclosure before off-device text/image traffic

Cloud LLM and cloud VLM settings display the destination boundary and state
that text or camera frames leave the device only after explicit
authorization. Network-bound provider switches additionally pass through
`ReachyProviderFallbackPolicyEngine`, which requires network authorization
and a privacy-boundary confirmation when the boundary changes.

Speech remains separately covered by the RMA-120/RMA-125 provider contracts;
no cloud speech fallback is silently enabled by this review.

### Logs and diagnostics redaction

`ReachyDiagnosticRedactor` redacts fields classified as secrets, private
text, raw audio, raw images, raw media, sensitive headers, and URL
credentials/query material. Potential bearer/API-key/token/password markers
are also bounded/redacted before diagnostic export.

### Temporary media cleanup

Persistent recording/export of raw camera, microphone, and cloud-request
media is disabled until an explicit consent flow exists.
`ReachyPrivateMediaTemporaryFileStore` uses application-private cache
storage, deletes leased files on dispose, deletes partial files on creation
failure, and provides abandoned-file purge.

### Native buffer and lifetime safety

Native simulation handles use `SafeHandleZeroOrMinusOneIsInvalid` and
destroy the native token exactly through the safe-handle release path.
`ReachySimAuthoritativeStateReader` frees its unmanaged buffer on constructor
failure and disposal and zeros the pointer. `ReachySimSession` serializes
handle access, explicitly disposes the safe handle, nulls its owner reference,
and suppresses finalization after deterministic cleanup.

## Qualification

The static regression suite is:

`scripts/tests/test_rma191_security_review.py`

RMA-191 roadmap checkboxes must remain open until this exact implementation
has passed the repository's authoritative CI and the evidence is reconciled
on current `master`.

# RMA-191 privacy and security review — 2026-09-23

## Scope

This review closes the repository-level privacy/security audit for the initial Android digital-twin release. It does not replace provider endpoint security, Android permission acceptance, or physical-device qualification owned by their respective RMAs. The review ties the seven RMA-191 checklist items to existing production controls and adds `scripts/tests/test_rma191_privacy_security_review.py` as a drift guard.

## Findings

### No embedded keys

Production C#/Java/native sources contain no recognized OpenAI-style secret, Google API key, AWS access key, or private-key material. Provider credentials remain references in non-secret profiles and are supplied at runtime. The RMA-191 regression test scans production source roots for these private-credential signatures.

### Android Keystore usage

RMA-161 already physically qualified the credential lifecycle. `ReachyProviderSecretBridge` stores an AES key in `AndroidKeyStore`, encrypts values with AES-GCM using randomized IVs and per-reference AAD, persists only IV/ciphertext, and clears transient ciphertext buffers. Missing or invalidated keys fail closed rather than silently replacing a key while ciphertext remains.

### Network security configuration

Provider profiles default to `RequireHttps`. Cleartext is available only through the explicitly named `TrustedLocalNetworkCleartext` mode and only for hosts accepted by `ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost`. The shared HTTP transport disables automatic redirects and independently rechecks final scheme, host, and port so an accepted profile cannot be redirected to a different or weaker endpoint. This application-layer policy is required because the product intentionally supports user-selected trusted-LAN OpenAI-compatible endpoints; a blanket Android cleartext ban would break that explicit feature.

### Disclosure before cloud-bound payloads

The shipping cloud LLM settings disclose that conversation text leaves the device and require explicit authorization before use. The cloud VLM settings similarly disclose camera-frame transmission and require explicit authorization. Both services route network-boundary changes through `ReachyProviderFallbackPolicyEngine.ConfirmPrivacyBoundaryChange`. Generic provider selections are classified by `SendsDataOffDevice` and surfaced in the Privacy summary. ASR/TTS cloud adapters exist as core adapters, but their generic cloud preferences remain unavailable in the shipping settings composition, so the release cannot silently activate a cloud speech path.

### Logs and diagnostics redaction

RMA-170/RMA-172 already established typed diagnostic data classes and bundle admission policy. Secret, private text, raw audio, raw image, raw media, credential-bearing headers, and common secret-marker text are redacted centrally before sinks. Diagnostic bundle export re-applies the redaction policy and fails closed for non-redacted user selections.

### Temporary media cleanup

RMA-162 defaults raw camera, microphone, and cloud-request media retention to disabled. Temporary media uses `Application.temporaryCachePath`; disposable leases delete files and abandoned files can be purged. Cloud ASR constructs its bounded multipart body in memory and clears the byte array in `finally`, avoiding persistent temporary audio for that provider path.

### Native buffer and lifetime safety

The managed simulation boundary owns native sessions through `SafeHandleZeroOrMinusOneIsInvalid`, serializes session operations behind a gate, uses size-query/bounded-allocation patterns for native buffers, validates returned sizes, and frees unmanaged allocations in `finally`. Existing managed lifecycle tests perform 1,000 create/use/dispose cycles; native concurrency tests cover bounded buffer behavior including `BUFFER_TOO_SMALL` paths.

## Regression evidence

The umbrella contract is supplemented by the already-merged regression suites for RMA-161, RMA-162, RMA-163, RMA-170, and RMA-172. On the local master snapshot used for this review, all of those Python source contracts passed before this document was authored. Exact-head repository CI remains authoritative before merge.

## Residual risks and non-claims

- The explicit trusted-LAN cleartext mode is intentionally less secure than HTTPS and remains visibly labeled for local development; the runtime restricts it to trusted local hosts.
- This review does not claim that arbitrary third-party provider endpoints are trustworthy. It verifies transport/profile policy, disclosure, credential handling, and fail-closed boundaries.
- Physical Android permission behavior remains separately qualified under RMA-190/RMA-090.
- Physical credential invalidation evidence is inherited from completed RMA-161; this review does not fabricate a new device run.

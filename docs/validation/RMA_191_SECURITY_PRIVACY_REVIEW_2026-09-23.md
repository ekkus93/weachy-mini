# RMA-191 Security and Privacy Release Review — 2026-09-23

## Scope

This review closes the release-level RMA-191 audit by tying each checklist item to
production controls plus executable regression coverage. It does not introduce a new
security subsystem; it verifies the controls implemented by RMA-030/031, RMA-140,
RMA-146, RMA-161/162/163, and RMA-170/172 remain present together at the release
boundary.

The permanent regression gate is
`scripts/tests/test_rma191_security_privacy_review.py`, with hosted orchestration in
`.github/workflows/rma191-security-privacy-review.yml`.

## Findings

### No embedded keys

The RMA-191 gate scans production runtime sources, Android plugins, GitHub workflow
text, and ProjectSettings for common OpenAI, Google, GitHub, Slack, and PEM private-key
literal formats. Provider profiles persist only secret references; cloud credential
entry clears the in-memory draft after a save attempt.

Result: no embedded production credential is required or permitted by the release
configuration.

### Android Keystore usage

`ReachyProviderSecretBridge.java` stores provider secret material using
`AndroidKeyStore` AES/GCM keys, randomized encryption, a per-record IV, GCM
authentication, and the secret reference as AAD. Stored preferences contain encrypted
IV/ciphertext material rather than the plaintext credential. RMA-161 additionally owns
the physical lock/invalidation/app-data-clear acceptance evidence.

Result: BYOK secrets are Keystore-backed and are not persisted as plaintext settings.

### Network security configuration

Provider profiles default to `ReachyProviderTlsMode.RequireHttps`; HTTP is rejected
unless the user explicitly selects the trusted-local-development mode and the endpoint
passes `ReachyNetworkEndpointSecurity.IsTrustedLocalNetworkHost`. That exceptional
mode carries the persistent `LOCAL DEVELOPMENT ONLY` warning.

The release review also fails if an Android plugin manifest opts the application into
global `usesCleartextTraffic="true"`. The shared provider transport contains no
certificate-validation bypass hook, and TLS/certificate failures remain typed failures.

This is intentionally layered rather than a blanket global cleartext enablement: the
application policy is HTTPS-by-default, with only the already-specified explicit local
development exception.

### Provider disclosure before cloud-bound audio/image/text

The settings UI explicitly states that enabling Cloud LLM sends conversation text to
the configured endpoint and that enabling Cloud VLM sends camera frames to the
configured endpoint; each requires an explicit AUTHORIZE action. The settings state
also exposes the active privacy/off-device summary.

The current shipping conversation speech path does not select a cloud audio provider:
ASR resolves the Android on-device recognizer first and then the Android system speech
service, while TTS requires an exact installed offline Android voice. The release gate
locks those properties and fails if an OpenAI speech adapter is silently wired into
that shipping turn path.

Result: text/image cloud paths carry explicit visible disclosure and authorization, and
the shipping audio path does not silently cross into a cloud provider.

### Logs and diagnostics redaction

`ReachyDiagnosticRedactor` redacts Secret, PrivateText, RawAudio, RawImage, and
RawMedia data classes, credential-bearing headers, and common token/password markers.
The diagnostic bundle exporter routes admitted fields through that redactor. Existing
RMA-170 and RMA-172 managed contracts verify the same default-deny/redaction behavior.

Result: release diagnostics do not intentionally export secrets or raw private media.

### Temporary media cleanup

`ReachyPrivateMediaTemporaryFileStore` writes only below the application-private
cache root, deletes leased media on disposal, deletes partial output on creation
failure, and provides abandoned-file purging used by the application storage service.
RMA-162 tests verify disposal and abandoned-file cleanup.

Result: temporary private media has bounded app-private lifetime rather than persistent
retention by default.

### Native buffer and lifetime safety

The managed interop boundary owns native simulation handles with `SafeHandle`, releases
them through `NativeReachySim.Destroy`, invalidates released handles, frees manually
allocated buffers in `finally`, and rejects use after disposal.

The native contract/concurrency suites additionally cover stale handles, double destroy,
undersized/invalid state-copy requests, and the requirement that caller-owned size/output
buffers remain unchanged on invalid or busy operations. The session lifecycle suite
also exercises repeated create/dispose cycles.

Result: the release boundary has explicit ownership, size validation, stale-handle
rejection, and failure-path buffer invariants.

## Validation

Local source-contract validation performed during this review:

```text
python3 scripts/tests/test_rma191_security_privacy_review.py
.......
----------------------------------------------------------------------
Ran 7 tests
OK

python3 -m py_compile scripts/tests/test_rma191_security_privacy_review.py
# exit 0
```

Additional sandbox validation on 2026-09-24, using the uploaded `master`/branch
checkout after confirming the branch still compared cleanly against current `master`:

```text
python3 scripts/tests/test_rma161_credential_lifecycle.py
# exit 0
python3 scripts/tests/test_rma162_private_media_retention.py
# exit 0
python3 scripts/tests/test_rma170_structured_logging.py
# exit 0
python3 scripts/tests/test_rma172_diagnostic_bundle_export.py
# exit 0
python3 scripts/tests/test_rma191_security_privacy_review.py
# exit 0
cmake -S . -B build/rma191-native-local -G Ninja -DBUILD_TESTING=ON
# exit 0
cmake --build build/rma191-native-local --target reachy_sim_managed_contract --parallel
# exit 0
```

The sandbox did not have `dotnet`, so the managed Application/Core/RemoteVlm
assemblies still require hosted or developer-machine qualification before merge.

The dedicated hosted workflow additionally runs the RMA-161, RMA-162, RMA-170, and
RMA-172 static contracts, managed application/privacy tests, Remote VLM redaction
contracts, a native managed-contract build, and managed/native lifetime tests.

At review time, GitHub Actions is failing jobs before any step starts (zero-step jobs
with unavailable logs). That infrastructure failure is not treated as source failure;
the branch remains subject to exact-head hosted qualification before merge when Actions
execution is available.

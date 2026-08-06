# RMA-114 Local VLM Extension Point Validation

**Task:** RMA-114 — Implement local VLM extension point
**Date:** 2026-08-06
**Status:** Complete
**Accepted implementation SHA:** `1a1488229526cb5abfa03e321bf05bfb0d798ed9`

## Scope

RMA-114 establishes a versioned, optional local vision-language-model extension
point. It does not integrate a model, inference runtime, downloader, package
installer, benchmark result, or user-facing model selector. Provider execution,
owned transformed-frame lifetime, timeouts, cancellation, provider epochs, and
structured errors remain owned by the existing RMA-110 provider contracts and
executor. Scheduling remains owned by RMA-113.

Permanent implementation and validation surfaces:

- `Assets/ReachyMini/Runtime/Core/Perception/ReachyLocalVisionLanguageContracts.cs`
- `models/manifests/local-vlm-manifest.schema.json`
- `models/manifests/README.md`
- `managed/ReachyMini.LocalVlm.Tests/Program.cs`
- `managed/ReachyMini.LocalVlm.Tests/ReachyMini.LocalVlm.Tests.csproj`
- `managed/ReachyMini.LocalVlm.Tests/README.md`
- `docs/architecture/LOCAL_VLM_EXTENSION_POINT.md`
- `.github/workflows/rma114-local-vlm-extension.yml`

## Release policy

`LocalVlmReleasePolicy` is fail closed:

- a local VLM is not required for the first release;
- automatic model download is disabled;
- automatic provider fallback is disabled;
- candidate sub-1B-class benchmarking is deferred until physics and LLM
  performance are stable.

No local model artifact, tokenizer, vision projection, runtime library, or
benchmark claim is committed under `models/manifests`. RMA-114 therefore makes
no claim that an Android phone can currently run a useful VLM within the
product's latency, memory, thermal, or power budgets.

## Adapter contract

`ILocalVisionLanguageAdapter` accepts a validated manifest and an explicit local
provider configuration. It may return only the exact on-device
`IVisionLanguageProvider` represented by that manifest. The returned descriptor
must match the manifest identity and remain `ProviderLocality.OnDevice` with no
network requirement.

The adapter boundary does not provide:

- a URL or download API;
- a remote provider transport;
- a provider search order;
- a fallback callback;
- an implicit default model;
- an API key or credential surface;
- an artifact verification implementation.

Artifact verification remains an explicit caller precondition. The
configuration records whether the caller completed integrity verification, but
RMA-114 performs no filesystem or content-resolver I/O and does not convert that
precondition into an unverified success path.

## Manifest contract

Schema version 1 records these bounded sections:

1. `identity` — provider ID, display name, model family/version, and manifest
   identity;
2. `runtime` — exact runtime ID/version, architecture, ABI, quantization, and
   context/image limits;
3. `limits` — concurrency, timeout, memory, storage, and thermal-policy limits;
4. `distribution` — HTTPS provenance, revision, license, redistribution and
   acknowledgement policy, with network use fixed to false;
5. `capabilities` — captioning, visual question answering, object description,
   structured output, and cancellation support;
6. `artifacts` — safe relative path, lowercase SHA-256, and positive byte size.

The managed validator rejects missing text, malformed identifiers, unsafe paths,
uppercase or malformed hashes, non-positive sizes, duplicate artifact paths,
more than 64 artifacts, artifact totals exceeding declared storage, unsupported
schema versions, network-dependent runtimes, missing cancellation, and manifests
without a semantic capability.

The Draft 2020-12 JSON Schema mirrors the managed shape and uses closed objects
for every section. CI parses the schema and the contract suite checks required
sections, artifact fields, hash patterns, policy constants, and source/schema
parity.

## Local-root hardening

A post-green source audit rejected the original permissive root check. It
accepted relative paths and could treat UNC paths or remote-host file URIs as
local, contradicting the no-network extension contract.

The accepted validator permits only:

- absolute Unix filesystem paths;
- absolute Windows drive paths;
- hostless, credential-free file URIs;
- authority-bearing, credential-free Android content URIs without a port.

It rejects:

- relative filesystem paths;
- backslash or slash UNC/network shares;
- remote-host file URIs;
- file URI credentials;
- content URIs without an authority;
- content URI credentials or ports;
- HTTP, HTTPS, FTP, and other network schemes.

The permanent harness contains explicit regression cases for each rejected root
class. Separator checks use character code 92 in the cross-platform contract and
tests to avoid cross-language escaping ambiguity in the validation path.

## Honest unavailable provider

`UnavailableLocalVisionLanguageAdapter` is the permanent first-release-safe
implementation. Its descriptor advertises on-device locality but no operational
capability. Availability and invocation return typed unavailable results; a
pre-cancelled token returns cancellation; disposal is deterministic and
idempotent. The stub does not inspect image pixels, invoke a runtime, read a
model, download content, open a network connection, or choose another provider.

## Deterministic contract coverage

The 45-case managed harness covers:

- release-policy constants and first-release optionality;
- valid manifest construction and immutable bounded snapshots;
- every required identity, runtime, limit, distribution, capability, and
  artifact field;
- safe relative manifest artifact paths and lowercase hashes;
- duplicate, traversal, rooted, URI, oversized, and malformed artifact inputs;
- local-root acceptance and network/relative/UNC rejection;
- unavailable-stub descriptor, availability, invocation, cancellation, and
  disposal behavior;
- exact provider identity/locality/capability matching;
- rejection of unverified artifacts, null providers, fallback identities, and
  network-dependent providers;
- schema/source parity and absence of model payloads from the manifest
  directory.

The harness does not load a model, invoke an inference runtime, inspect an image,
or access a network service.

## Rejected candidates and corrections

### Initial clean implementation

Implementation commit `797709e1c64a9cdd531263e338146ccc2054cfa4`
passed the dedicated RMA-114 gate, but hosted static CI rejected two ShellCheck
SC2155 findings in evidence-variable declarations. That SHA was not accepted.
The workflow was repaired by separating assignment from export without changing
runtime or contract behavior.

### Local-root trust gap

The first independently green extension contract still accepted relative roots
and did not explicitly reject UNC or remote-host file locations. That behavior
was rejected during source audit. The accepted source hardened local-root
validation and added deterministic regression cases before promotion to
`master`.

### Accepted implementation

The accepted implementation SHA is
`1a1488229526cb5abfa03e321bf05bfb0d798ed9`. Its combined commit statuses are:

- `RMA-114 Local VLM Extension`: success;
- `Local Unity Android Validation`: success.

## Dedicated RMA-114 evidence

Permanent workflow:

- run: `31100461712`;
- job: `92612496688`;
- conclusion: success;
- managed-core warnings-as-errors build: success;
- local-VLM contracts: 45 passed;
- JSON Schema parse: success;
- exact-SHA evidence upload: success;
- final status publication: success.

Artifact:

- ID: `8967238772`;
- name:
  `rma114-local-vlm-extension-evidence-1a1488229526cb5abfa03e321bf05bfb0d798ed9`;
- digest:
  `sha256:788c17c20cce1f55f1ca05bccc86651620b4ebcb4efdead4cb059b94ddcb60bf`.

The exact report records:

```json
{
  "artifact_integrity_required": true,
  "automatic_model_download": false,
  "automatic_provider_fallback": false,
  "candidate_benchmarking_deferred": true,
  "commit_sha": "1a1488229526cb5abfa03e321bf05bfb0d798ed9",
  "contract_case_count": 45,
  "exact_on_device_provider_identity": true,
  "implementation": "optional_local_vlm_extension_point",
  "local_vlm_required_for_first_release": false,
  "manifest_schema_version": 1,
  "model_payload_bundled": false,
  "network_dependent_local_runtime": false,
  "status": "passed",
  "unavailable_stub": true
}
```

Exact source digests:

- local-VLM managed contract:
  `cd749e83f24eb19e065a035fa0dcf6fa6b82263a25d3b8d0d4f8be216dcc55eb`;
- deterministic harness:
  `99c20d70f4c6c6ad085a412ec6766c25da997e005993badc73facc5b446df167`;
- harness project:
  `777b9216652309ae32158ee4c93994d7e04116f8917c0f98738e109d6dfd4645`;
- harness README:
  `476b9b47ca3937bff057e5fe51ed1c3bd5b20e365486ea7c8e277d63cc972557`;
- manifest schema:
  `580efdb3e088d881545ab9e65aa8a3f11aad5d8c5ead55794bb748885d2e6d3f`;
- manifest README:
  `44bbdc99f96ca75b12689454cf3e8291d4aedf818435a693ac94834a54daa032`;
- architecture document:
  `d7dc2968301657e52eb3f2062f2fa7f3b064a09a7610203e761017d33a214651`.

## Hosted CI evidence

Hosted CI run `31100461578` passed on the accepted implementation SHA:

- static repository checks and workflow lint;
- managed warnings-as-errors and native lifecycle tests;
- native warnings-as-errors;
- ASan/UBSan tests;
- Android lint, Java warnings, and tests;
- pinned Reachy/MuJoCo model validation and trace generation.

## Unity and physical Android evidence

Self-hosted workflow:

- run: `31100461740`;
- job: `92612565252`;
- conclusion: success;
- device: LGE LG-H872;
- Android: 8.0.0 / API 26;
- ABI: arm64-v8a;
- serial: `LGH87250967ab9`.

The exact run passed:

- `129/129` Unity EditMode tests;
- `1/1` Unity PlayMode test;
- ARM64/API-26 IL2CPP APK build and verification;
- RMA-090 camera discovery;
- RMA-091 camera acquisition, switching, rotation, and lifecycle;
- RMA-092 physical Vulkan texture conversion and stale-buffer rejection;
- RMA-111 bundled face/person tracking;
- RMA-022 native lifecycle validation;
- authoritative MuJoCo-driven rendering;
- every evidence upload;
- APK upload;
- final commit-status publication.

Physical artifacts:

| Evidence | Artifact ID | Digest |
|---|---:|---|
| Unity tests | `8967308895` | `sha256:3a33b3ea5109a80765b4a3cc850eabd4052fe4d5113257064c47db298791a50e` |
| RMA-090 | `8967385694` | `sha256:5c23db7703abeeb57f71d96c511a956eba78683d0c81cd923bf87580f23e3306` |
| RMA-091 | `8967427342` | `sha256:feac2abf8484c39decd6314a3f32543517306154dc541204fbf0a1c494f6c147` |
| RMA-092 | `8967475904` | `sha256:c5c7b0b802db452672b337607113eea235b0a8c1a93a1cdae8cabe7b952d7dd4` |
| RMA-111 | `8967492951` | `sha256:98f133220196c7dc593407431e634263c4c9cffab419c4f025dd941a94227f58` |
| Lifecycle | `8967524158` | `sha256:ca06fd1100b22b98bfa31a3eade9619241963d87a54278f3ed32e7d384df7f83` |
| Rendering | `8967540427` | `sha256:6bfdf89c185035b6096d2a88d74a915184f7d8d922e2445694c489466735bfff` |
| APK | `8967560493` | `sha256:733b6c09dab7eb1be1b335a523d3c069cc94d1977775d564da8fb9ff9a4da8ba` |

RMA-111 recorded stable `face-000001` and `person-000001` identities,
invalid-center suppression, zero VLM invocations, and no runtime model download.
This proves that adding the optional local-VLM extension did not make basic
tracking dependent on a VLM.

Authoritative rendering recorded 17 moved bodies, all six Stewart links moving,
body yaw, head, and both antennas moving, valid renderer structure, and
`hidden_kinematic_fallback=false`.

The authoritative gate reused the installed APK only after candidate,
pre-install, and final installed digests all matched:

`909ee9a65bf3182838b6650860ab63af575a90823898ad632eb0cf94dc204edc`

Install status and launch status were both zero, and the evidence records
`installed_apk_matches_candidate=true`.

## Final evidence addendum

The first complete documentation boundary was
`b4177d337537e5bc0baf3c5bdee3252022f08298`. It passed every permanent RMA-114
and repository acceptance gate without changing the accepted implementation.

### Dedicated local-VLM gate

- run: `31102008463`;
- job: `92617588082`;
- conclusion: success;
- warnings-as-errors managed-core build: success;
- local-VLM contracts: 45 passed;
- manifest-schema parse: success;
- evidence upload and final status publication: success;
- artifact ID: `8967872322`;
- artifact digest:
  `sha256:de4dbd81c797b07bfe00602abf86c01f48ab985877e92d18937c51ba91e806b2`.

The final exact report preserved the accepted implementation properties:

```json
{
  "artifact_integrity_required": true,
  "automatic_model_download": false,
  "automatic_provider_fallback": false,
  "candidate_benchmarking_deferred": true,
  "commit_sha": "b4177d337537e5bc0baf3c5bdee3252022f08298",
  "contract_case_count": 45,
  "exact_on_device_provider_identity": true,
  "implementation": "optional_local_vlm_extension_point",
  "local_vlm_required_for_first_release": false,
  "manifest_schema_version": 1,
  "model_payload_bundled": false,
  "network_dependent_local_runtime": false,
  "status": "passed",
  "unavailable_stub": true
}
```

### Hosted CI

Hosted CI run `31102008708` passed on the same exact SHA. All five jobs were
successful:

- static repository checks, actionlint, Ruff, and ShellCheck;
- managed warnings-as-errors and native lifecycle tests;
- native warnings-as-errors and ASan/UBSan tests;
- Android lint, Java warnings, and tests;
- pinned Reachy/MuJoCo source, topology, parameter, compilation, step, and
  reference-trace validation.

### Final Unity and physical Android validation

Self-hosted run `31102008833`, job `92617589982`, completed successfully on the
LG-H872 running Android 8.0.0/API 26/arm64-v8a. It passed:

- `129/129` Unity EditMode tests;
- `1/1` Unity PlayMode test;
- ARM64/API-26 IL2CPP APK build and verification;
- physical RMA-090 camera discovery;
- physical RMA-091 acquisition, switching, rotation, and lifecycle;
- physical RMA-092 Vulkan texture conversion and stale-buffer rejection;
- physical RMA-111 bundled face/person tracking;
- RMA-022 native lifecycle validation;
- authoritative MuJoCo-driven rendering;
- every evidence upload, APK upload, and final status publication.

Final physical artifacts:

| Evidence | Artifact ID | Digest |
|---|---:|---|
| Unity tests | `8967943032` | `sha256:ba03ae093fa1c80432538bdd783e46dd020bff965e78c5a78776195f7fd77a28` |
| RMA-090 | `8968020219` | `sha256:8612811bc923a4c98a45b1f391968e4d8bf4937873a3c12807745ae73c82f817` |
| RMA-091 | `8968063390` | `sha256:ab129f47169a6fd651a373cf302fc8b844b82ffdce6c0625f0c0c6eef0e4b4ef` |
| RMA-092 | `8968093802` | `sha256:22b0338cee4ec0cf8389f9df53a1e418df3466978491fe1336e05f781d7f4db0` |
| RMA-111 | `8968113110` | `sha256:9fb9c25a59acce668027c597e2987e19a0e0e8a93ad549387ea7b09bc37ca1d2` |
| Lifecycle | `8968146775` | `sha256:7cad514d1f3f45179138027f5f7f1df2ecc25fb54d225f4ed8dd6c9031409ceb` |
| Rendering | `8968164871` | `sha256:b076850511b18328aafa670b4f3a51f5a8cb2f8587f59166e03808ad808d9351` |
| APK | `8968190565` | `sha256:805553c7d13aef3fd6504b4628a979f4b1c461ea2194754e24817d203b05a8d0` |

RMA-111 again recorded one face and one person on both frames, stable
`face-000001` and `person-000001` identities, invalid-center suppression, zero
VLM invocations, and no runtime model download.

Authoritative rendering again recorded 17 moved bodies, all six Stewart links
moving, body yaw, head, and both antennas moving, valid renderer structure,
`runtime_status=Running`, `renderer_status=Rendering`, and
`hidden_kinematic_fallback=false`.

The authoritative gate reused the installed APK only after the candidate,
pre-install installed APK, and final installed APK SHA-256 values all matched:

`1e5e20ad86aff79d07b0a09c809d7f057cecc4a5b56e8c3b64a67d8013f28c59`

Install and launch status were both zero, and the evidence records
`installed_apk_matches_candidate=true`.

The combined commit statuses on `b4177d337537e5bc0baf3c5bdee3252022f08298`
were both successful:

- `RMA-114 Local VLM Extension`;
- `Local Unity Android Validation`.

## Completion boundary

This addendum records the fully validated documentation boundary immediately
preceding it. The clean addendum commit is followed by one user-authored,
validation-only boundary so GitHub runs every permanent workflow on an exact
post-addendum tree. RMA-114 is complete only when that successor SHA also passes
the dedicated local-VLM gate, hosted CI, complete Unity/APK/device suite, and
both permanent commit statuses.

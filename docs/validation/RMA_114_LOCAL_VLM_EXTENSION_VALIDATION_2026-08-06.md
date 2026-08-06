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

## Completion boundary

RMA-114 is complete only after the final documentation/evidence SHA passes the
same dedicated, hosted, Unity, APK, physical camera/tracking/lifecycle, and
authoritative-rendering gates. The final exact SHA and its run identities are
recorded after that validation completes.

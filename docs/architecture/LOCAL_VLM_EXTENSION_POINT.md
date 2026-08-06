# Local VLM Extension Point

## Scope

RMA-114 defines how a future on-device vision-language runtime can join the
existing RMA-110 provider executor and RMA-113 scheduler. It does not select,
bundle, download, benchmark, or execute a concrete model.

The extension point has four layers:

1. a versioned, integrity-bearing model manifest;
2. a local runtime adapter factory;
3. the existing `IVisionLanguageProvider` request/result contract; and
4. the existing exact-provider scheduler and executor.

The local adapter is a factory, not a second semantic-provider API. Once a
runtime successfully creates a provider, that provider must pass through the
same identity, transformed-frame, cancellation, timeout, supersession, and
result-validation rules as every other RMA-110 VLM provider.

## First-release policy

`LocalVlmReleasePolicy` is deliberately fail-closed:

- a local VLM is not required for the first release;
- automatic model download is disabled;
- automatic provider fallback is disabled; and
- candidate benchmarking is disabled until physics and local-LLM performance
  budgets are stable.

The application may therefore ship with no local VLM runtime and no local VLM
model. This is a supported configuration, not an error to hide.

## Manifest version 1

`models/manifests/local-vlm-manifest.schema.json` and
`LocalVlmModelManifest` define the same fields.

### Identity and provenance

A manifest records stable lowercase identifiers, display name, model version,
HTTPS source URI, exact source revision, and license identifier. The source URI
is attribution/provenance metadata only. The extension point contains no HTTP
client, download function, or remote artifact resolver.

### Runtime requirement

The runtime section records runtime identifier/version, model architecture,
quantization, and parameter count. `requires_network_access` is fixed to
`false`. A provider created through this extension point must publish an
`OnDevice` `ProviderDescriptor`.

### Limits and capabilities

The manifest records context window, maximum output tokens, maximum prompt
characters, maximum image dimensions, minimum RAM, minimum storage, semantic
features, cancellation support, and maximum provider concurrency. At least one
of visual-question or scene-description support must be true, and cancellation
is mandatory.

When an adapter creates a provider, the provider identifier, instance
identifier, version, location, and every VLM capability must match the verified
manifest and creation configuration exactly. A mismatch is rejected before the
provider can enter selection or scheduling.

### Distribution and artifacts

Schema version 1 permits bundled, user-provided, or developer-provided local
artifacts. It fixes `required_for_first_release` and
`automatic_download_allowed` to `false`.

Each artifact has a safe relative path, exact positive byte count, and lowercase
SHA-256. Absolute paths, URI schemes, backslashes, empty path segments, and
`.`/`..` traversal are rejected. The aggregate artifact bytes cannot exceed the
manifest storage estimate, and a manifest is bounded to 64 artifacts.

`LocalVlmProviderConfiguration` accepts only a caller-verified local package
root. Filesystem paths, file URIs, and Android content URIs are permitted;
network schemes are not. The configuration cannot be constructed until the
caller marks artifact integrity verified.

## Adapter contract

`ILocalVisionLanguageAdapter` exposes:

- stable runtime and runtime-instance identity;
- operational capabilities;
- an explicit availability snapshot; and
- cancellation-aware creation of one exact `IVisionLanguageProvider` from a
  verified local configuration.

Operational adapters must support cancellation and bounded concurrent model
loads. An adapter cannot claim provider creation without model loading.

`LocalVlmProviderCreationResult` is typed. It distinguishes creation,
unavailability, invalid configuration, cancellation, and runtime failure. It
never carries a provider on failure and does not encode retry or fallback.

## Unavailable implementation

`UnavailableLocalVisionLanguageAdapter` is the first-release implementation. It
reports:

- no runtime present;
- no model-loading capability;
- no provider-creation capability;
- zero load concurrency; and
- `Unavailable` for creation without attempting a download or another provider.

Disposal is idempotent, pre-cancellation remains visible, and the stub never
pretends that semantic capability exists.

## Security and privacy boundaries

The local extension point does not:

- open a network connection;
- accept a remote artifact root;
- log model contents, image contents, credentials, or local package paths;
- bypass transformed Reachy-eye frame validity;
- dispose frames borrowed from the executor;
- retry a failed runtime automatically; or
- select a cloud/local-network provider when local creation fails.

A future runtime implementation must preserve these boundaries and add its own
runtime-specific memory, cancellation, shutdown, and corruption tests before a
model is eligible for packaging.

## Deferred benchmarking

RMA-114 records no candidate score and makes no sub-1B model recommendation.
Benchmarking begins only after authoritative physics and the local LLM have
stable measured CPU, GPU, memory, thermal, and latency budgets. Those later
results must be tied to an exact model revision, quantization, runtime version,
device, prompt/image workload, and artifact hashes.

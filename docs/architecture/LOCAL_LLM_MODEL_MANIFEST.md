# RMA-131 local LLM model manifest

**RMA:** 131  
**Scope:** model metadata/validation only; no download, installation, benchmark selection, provider
integration, or thermal/resource controller

## Purpose

RMA-131 defines the versioned metadata boundary between the RMA-130 `reachy_llama` native runtime
and later model-management work. The contract tells later code exactly which model artifact was
intended, what integrity/provenance metadata must be verified, which inference metadata is
required, and which devices/runtimes the manifest claims are compatible.

A valid manifest is **not** proof that its artifact exists, is trustworthy, performs well, is safe
to download, or should be selected. Those are later gates. Schema validity only proves that the
metadata needed by those gates is explicit and internally consistent.

## Version-1 shape

The machine-readable schema is `models/manifests/local-llm-manifest.schema.json`. The immutable
managed mirror is `ReachyMini.LocalModels.LocalModelManifest`.

Version 1 contains seven top-level sections:

1. `schema_version` — exactly `1`.
2. `identity` — manifest/model IDs, display/version strings, HTTPS provenance, exact source
   revision, license, and explicit experimental state.
3. `runtime` — exactly `reachy_llama` ABI 2 with network-independent local inference.
4. `artifact` — one safe relative lowercase `.gguf` path, exact file size, and lowercase SHA-256.
5. `gguf_metadata` — GGUF version, architecture, quantization, parameter count, tokenizer model,
   and tokenizer preprocessing identity.
6. `inference` — context limit, complete chat template, bounded unique stop-token strings, a
   context/batch-qualified peak-RAM estimate, and recommended thread count.
7. `device_compatibility` — exactly `arm64-v8a` for the current native plug-in, minimum Android API,
   required CPU features, minimum RAM, and exact `reachy_llama` ABI version.

The single-artifact rule is intentional for schema version 1. It matches the initial single-GGUF
work and gives RMA-132 one atomic integrity/install unit. Split/sharded model packages require a
future schema revision rather than an ambiguous interpretation of the singular file-size/hash
fields in the roadmap.

## Identity and experimental labeling

`manifest_id` and `model_id` use bounded lowercase stable identifiers. They are not UI constants.
`LocalModelManifestCatalog` accepts arbitrary validated IDs, rejects duplicate manifest/model IDs,
and performs ordinal exact lookup only. It has no default model, fuzzy match, prefix match, or
fallback selection.

Every manifest contains `experimental` plus `experimental_reason`. Experimental manifests require
a nonblank reason. Non-experimental manifests require an empty reason so contradictory state
cannot be serialized accidentally.

This marker does not create a new maturity claim. In particular, `experimental=false` means only
that a later process has chosen not to label that manifest experimental; RMA-133 still owns
benchmark-backed recommendation/default selection.

## Provenance is not download authorization

`identity.source_uri` must be absolute HTTPS without embedded credentials or fragments, and the
source revision/license are mandatory. The URI is for provenance/license display and later source
resolution. RMA-131 contains no HTTP client, download policy, resume logic, installer, or path
loader.

RMA-132 must separately decide whether a source is allowed, check storage, write only to an
approved temporary location, verify byte size/SHA-256, perform atomic installation, recover partial
state, and expose only validated installed paths. A syntactically valid RMA-131 source URI can
therefore still be rejected by RMA-132.

## Artifact and GGUF contract

The artifact path is package-relative and rejects absolute paths, drive prefixes, backslashes,
empty segments, `.`/`..`, and non-lowercase `.gguf` suffixes. The manifest records exact positive
byte size and a 64-character lowercase SHA-256.

Normalized GGUF metadata is explicit rather than inferred from a model name. Architecture,
quantization, parameter count, tokenizer model, tokenizer preprocessing identity, and GGUF version
are required. RMA-132/RMA-134 should compare the declared metadata with the verified local GGUF
before making it loadable; a mismatch must fail visibly rather than silently changing the
configuration.

## Inference/resource assumptions

The manifest carries the complete chat template and bounded stop-token list instead of hard-coding
model-family behavior in UI or provider code. Stop tokens use exact ordinal identity and cannot be
duplicated.

The memory estimate is qualified by both context and batch size. Its basis context cannot exceed
the declared model context, and its basis batch cannot exceed the basis context. Recommended
threads are bounded to 1 through 64. These values are metadata for later benchmarking/resource
policy; they are not enforcement or performance evidence by themselves.

The device compatibility block currently requires `arm64-v8a`, API 26 or newer, exact
`reachy_llama` ABI 2, explicit required CPU features, and a minimum-RAM value at least as large as
the declared peak-RAM estimate. RMA-133/RMA-135 may narrow or revise these values based on measured
device evidence.

## Failure and fallback policy

The managed constructors and developer validator reject malformed state rather than supplying
implicit defaults. In particular they reject:

- unknown schema/runtime ABI values;
- non-HTTPS/credentialed provenance;
- missing or contradictory experimental labeling;
- unsafe paths, invalid sizes, or malformed hashes;
- incomplete GGUF/tokenizer metadata;
- blank/oversized chat templates, duplicate/oversized stop tokens, and impossible memory-basis
  relationships;
- unsupported Android ABI/API declarations, duplicate CPU features, and understated minimum RAM;
  and
- duplicate catalog identities.

A failed exact catalog lookup returns no model or throws `KeyNotFoundException`. It never chooses
another manifest. There is no network, provider, model, or configuration fallback in RMA-131.

## Selected RMA-133 manifest

`models/manifests/qwen3-0.6b-q4-k-m.local-llm.json` is the first real selected-model manifest. It requires active `reachy_llama` ABI 2 and records the exact Qwen3-0.6B Q4_K_M revision, byte size, SHA-256, tokenizer/chat metadata, and V6 benchmark-backed context/thread/memory profile. It does not bundle the GGUF and does not authorize provider/model fallback.

The schema shape remains version 1 because no manifest field or interpretation was added; the active runtime compatibility policy moved from historical ABI 1 to ABI 2 after RMA-133 constrained-generation validation. Historical RMA-131 and RMA-130 ABI-1 validation records remain immutable evidence of the earlier accepted boundary.

## Synthetic fixture

`models/manifests/examples/rma131-synthetic-experimental.local-llm.json` is intentionally fake and
uses the reserved `.invalid` domain. No GGUF corresponds to its declared artifact/hash. It exists
only so CI can validate serialization shape and failure mutations without selecting or distributing
an actual model before RMA-133.

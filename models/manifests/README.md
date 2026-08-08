# Local model manifests

This directory contains small, reviewable metadata contracts for optional on-device model
packages. Do not commit GGUF, ONNX, safetensors, tokenizer archives, generated indexes,
credentials, partially downloaded files, or other model payloads here.

## Local LLM manifests — RMA-131

`local-llm-manifest.schema.json` is the version-1 contract for one local GGUF language-model
artifact used by the first-party `reachy_llama` ABI from RMA-130. A conforming manifest records:

- stable manifest/model identity, display name, source revision, HTTPS provenance, and license;
- an explicit experimental marker and reason when the model has not completed later benchmark
  acceptance;
- exact `reachy_llama` ABI identity and the fact that local inference requires no network access;
- one safe relative `.gguf` path, exact byte size, and lowercase SHA-256;
- normalized GGUF version, architecture, quantization, parameter count, and tokenizer metadata;
- context limit, complete chat template, bounded unique stop tokens, memory-estimate assumptions,
  and recommended thread count; and
- Android ABI/API, required CPU features, minimum RAM, and native ABI compatibility.

The source URI is provenance metadata, not an automatic download authorization. RMA-131 never
fetches, imports, installs, selects, benchmarks, or loads a model. RMA-132 must perform free-space,
temporary-file, resume/restart, SHA-256, atomic-installation, recovery, deletion, and approved-path
checks before any artifact can become loadable. RMA-133 owns model benchmarking and recommendation.

The active schema-version-1 compatibility policy requires `reachy_llama` ABI 2. The selected `qwen3-0.6b-q4-k-m.local-llm.json` records the exact RMA-133 V6 winner; it is metadata only and does not bundle or automatically download the GGUF.

The committed `examples/rma131-synthetic-experimental.local-llm.json` remains deliberately synthetic.
It names no real candidate and has no corresponding model file. Its only purpose is to exercise
the schema and validators without creating a product recommendation or redistribution obligation.

Use the zero-network validator with:

```bash
python3 scripts/validate_local_llm_manifest.py path/to/manifest.json
```

Model IDs are data. UI/application logic must consume validated manifest/catalog state and must not
contain candidate-specific model identifiers, memory settings, context settings, or fallback
selection rules.

## Local VLM manifests — RMA-114

`local-vlm-manifest.schema.json` is schema version 1 for an optional on-device vision-language
model package. A conforming VLM manifest records:

- stable manifest and model identifiers;
- display name and model version;
- HTTPS provenance URI, exact source revision, and license identifier;
- runtime identifier/version, architecture, quantization, and parameter count;
- context, output, prompt, image, RAM, and storage limits;
- semantic capabilities, cancellation support, and maximum concurrency;
- artifact source; and
- every relative artifact path with exact byte size and lowercase SHA-256.

The VLM manifest source URI is provenance metadata, not an automatic download endpoint. Before an
adapter receives a model package, the caller must already have local artifacts and must verify every
size and SHA-256 against the manifest. Artifact paths are relative and cannot escape the verified
package root.

Local VLM support remains optional for the first release. Schema version 1 forbids automatic
download, network-dependent inference, and first-release dependence. Provider selection remains
exact; an unavailable or failed local adapter never falls back to a cloud or local-network
provider.

No candidate VLM is approved or bundled by RMA-114. Benchmark work remains deferred until the
authoritative physics runtime and local LLM path have stable performance budgets.

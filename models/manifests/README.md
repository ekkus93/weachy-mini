# Local model manifests

RMA-114 defines the metadata contract for an optional on-device vision-language
model. Commit only small, reviewable JSON manifests and schemas here. Do not
commit GGUF, ONNX, safetensors, tokenizer archives, generated indexes,
credentials, partially downloaded files, or other model payloads.

`local-vlm-manifest.schema.json` is schema version 1. A conforming manifest must
record:

- stable manifest and model identifiers;
- display name and model version;
- HTTPS provenance URI, exact source revision, and license identifier;
- runtime identifier/version, architecture, quantization, and parameter count;
- context, output, prompt, image, RAM, and storage limits;
- semantic capabilities, cancellation support, and maximum concurrency;
- artifact source; and
- every relative artifact path with exact byte size and lowercase SHA-256.

The manifest source URI is provenance metadata, not an automatic download
endpoint. Before an adapter receives a model package, the caller must already
have local artifacts and must verify every size and SHA-256 against the
manifest. Artifact paths are relative and cannot escape the verified package
root.

Local VLM support remains optional for the first release. Schema version 1
forbids automatic download, network-dependent inference, and first-release
dependence. Provider selection remains exact; an unavailable or failed local
adapter never falls back to a cloud or local-network provider.

No candidate model is approved or bundled by RMA-114. Benchmark work, including
sub-1B-class VLM evaluation, remains deferred until the authoritative physics
runtime and the local LLM path have stable performance budgets.

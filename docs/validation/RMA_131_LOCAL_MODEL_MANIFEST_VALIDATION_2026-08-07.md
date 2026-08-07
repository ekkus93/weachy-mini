# RMA-131 local model manifest validation

**Status:** Candidate implementation; exact-SHA evidence pending  
**Date:** 2026-08-07

## Candidate acceptance contract

RMA-131 is acceptable only if all of the following remain true:

1. Schema version 1 contains model ID, source/revision/license, exact file size/SHA-256, normalized
   GGUF/tokenizer metadata, context limit, complete chat template, stop tokens, qualified memory
   estimate, recommended threads, and explicit device/runtime compatibility.
2. Every manifest has an explicit experimental boolean; experimental manifests require a visible
   reason.
3. The version-1 runtime is exactly first-party `reachy_llama` ABI 1 and local inference cannot
   declare a network requirement.
4. Artifact paths are package-relative, traversal-safe lowercase `.gguf` paths with exact positive
   byte size and lowercase SHA-256.
5. Inference metadata is bounded and internally consistent; no missing value receives a silent
   default from the manifest validator.
6. Device compatibility is explicit and cannot understate the manifest's peak-RAM estimate.
7. Model/catalog lookup uses exact manifest data. No model ID, candidate-specific configuration,
   default model, fuzzy lookup, or fallback selection is hard-coded into UI/settings logic.
8. RMA-131 performs no download, import, installation, arbitrary-path load, benchmark selection,
   or model inference.
9. The committed example is synthetic/experimental and does not approve or bundle a real model.

## Required automated evidence

Before the RMA-131 TODO is checked, the exact implementation SHA must pass:

- managed core build with warnings as errors;
- `ReachyMini.LocalModelManifest.Tests`, covering the immutable C# contract and exact catalog
  behavior;
- `scripts/tests/test_rma131_local_model_manifest.py`, covering JSON shape, visible failure
  mutations, and candidate-ID absence from settings/UI logic;
- zero-network validation of the committed synthetic manifest;
- JSON parsing of both the schema and fixture;
- normal hosted repository CI.

The dedicated gate must publish exact-SHA source hashes and a machine-readable report. No real GGUF,
model download, external model service, API key, or benchmark result is required for this task.

## Scope boundary

RMA-132 owns safe download/import and approved installed paths. RMA-133 owns candidate evaluation
and default/recommended selection. RMA-134 owns the managed local LLM provider, and RMA-135 owns
resource/thermal policy. RMA-131 must not claim those later gates are complete.

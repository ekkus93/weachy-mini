# RMA-131 local model manifest validation

**Status:** Implementation accepted on `94145dda69f6ee3f886a78be9728ea6ddc355bb8`; final roadmap evidence SHA pending  
**Date:** 2026-08-07

## Accepted contract

RMA-131 establishes the versioned metadata and validation boundary for later local-model management. The accepted implementation preserves all of the following:

1. Schema version 1 contains model ID, source/revision/license, exact file size/SHA-256, normalized GGUF/tokenizer metadata, context limit, complete chat template, stop tokens, qualified memory estimate, recommended threads, and explicit device/runtime compatibility.
2. Every manifest has an explicit experimental boolean; experimental manifests require a visible reason and non-experimental manifests require an empty experimental reason.
3. The version-1 runtime is exactly first-party `reachy_llama` ABI 1 and local inference cannot declare a network requirement.
4. Artifact paths are package-relative, traversal-safe lowercase `.gguf` paths with exact positive byte size and lowercase SHA-256.
5. Inference metadata is bounded and internally consistent; no missing value receives a silent default from the manifest validator.
6. Device compatibility is explicit and cannot understate the manifest's peak-RAM estimate.
7. Model/catalog lookup uses exact ordinal manifest data. There is no default model, fuzzy match, prefix match, hidden alternate candidate, or fallback selection.
8. Model IDs and candidate-specific configuration remain data rather than hard-coded settings/UI logic.
9. RMA-131 performs no download, import, installation, arbitrary-path load, benchmark selection, model inference, provider fallback, or cloud request.
10. The committed example is deliberately synthetic and experimental; it does not approve, recommend, download, or bundle a real model.

## Implementation identity

Accepted implementation SHA:

`94145dda69f6ee3f886a78be9728ea6ddc355bb8`

Primary contract files:

- `Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalModelManifest.cs`
- `models/manifests/local-llm-manifest.schema.json`
- `models/manifests/examples/rma131-synthetic-experimental.local-llm.json`
- `scripts/validate_local_llm_manifest.py`
- `managed/ReachyMini.LocalModelManifest.Tests/Program.cs`
- `scripts/tests/test_rma131_local_model_manifest.py`
- `docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md`

The managed contract and JSON schema both identify schema version `1`, runtime `reachy_llama`, runtime ABI `1`, and current Android ABI `arm64-v8a`. The compatibility contract allows API 26 or newer while leaving any model-specific higher requirement explicit in the manifest.

## Dedicated exact-SHA evidence

Permanent workflow run `31208746428`, job `92966163017`, completed successfully on the exact accepted implementation SHA.

It passed:

- managed core compilation with warnings as errors and latest analyzers;
- the managed local-model manifest contract suite;
- deterministic JSON-manifest mutation/rejection contracts;
- zero-network validation of the committed synthetic manifest;
- JSON parsing of the schema and fixture;
- exact-SHA source hashing and machine-readable report creation; and
- evidence artifact upload and final commit-status publication.

Artifact `9005805295`, named
`rma131-local-model-manifest-94145dda69f6ee3f886a78be9728ea6ddc355bb8`, has digest
`sha256:aefdd64431c6d3b9c730b5b08fa902cb2a5edeb7b67b9bfeb0a25caae726100f`.

The machine-readable report records that only the synthetic fixture is present, no real model was selected, no model payload is bundled, automatic download/install is false, local inference requires no network, the catalog has no default/fallback model, and candidate IDs are not hard-coded in settings/UI logic.

## Hosted repository evidence

Hosted CI run `31208746388` completed successfully on the same exact implementation SHA.

All normal jobs passed:

- static policy: actionlint, Ruff lint, Ruff format, ShellCheck, and repository checks;
- managed warnings-as-errors and native lifecycle tests;
- native warnings-as-errors and sanitizer tests;
- Android lint, Java warnings, compilation, and tests; and
- pinned Reachy-model verification, conversion, MuJoCo compile/step, and reference generation.

RMA-131 requires no physical-device or model-quality acceptance because this task defines metadata and validation only. RMA-132 owns installation integrity and RMA-133 owns measured candidate selection.

## Ralph-loop corrections

The implementation was kept fail-closed while permanent gates exposed harness/static-policy defects:

1. The first managed test namespace, `ReachyMini.LocalModelManifest.Tests`, shadowed the domain type `LocalModelManifest`. Only the test namespace was changed to `ReachyMini.LocalModels.Tests`; production manifest semantics were unchanged.
2. Latest .NET analyzers rejected exception-test constructions whose results appeared unused (`CA1806`) and repeated constant array literals (`CA1861`). The harness now consumes construction results through `Func<object?>`/`GC.KeepAlive` and reuses `static readonly` fixtures. Neither analyzer was disabled or suppressed.
3. Ruff required `Callable` from `collections.abc` and a flattened boolean branch. Those mechanical lint changes preserved validator behavior.
4. `ruff format --check` then supplied an exact formatting-only diff across the two Python contract files. The formatter output was applied verbatim before the accepted implementation SHA.

No manifest field was removed, no validation bound was weakened, no warning/analyzer rule was disabled, and no permissive fallback/default was introduced to make the gates pass.

## Failure semantics and no-fallback guarantees

The managed constructors and developer validator reject malformed state rather than silently filling it. Rejections cover unknown schema/runtime ABI, non-HTTPS or credentialed provenance, contradictory experimental labeling, unsafe paths, malformed hashes/sizes, missing GGUF/tokenizer metadata, invalid chat/stop metadata, impossible context/batch memory assumptions, unsupported Android declarations, duplicate CPU features, understated RAM, duplicate catalog identities, and missing exact model lookup.

A missing catalog lookup returns no model or throws `KeyNotFoundException`; it does not select a neighboring, prefix-matching, default, cloud, or otherwise alternate model. The manifest source URI is provenance metadata only and does not authorize a fetch.

## Scope boundary

RMA-132 owns free-space checks, temporary download/import paths, resume/restart behavior, SHA-256 verification, atomic installation, partial-file recovery, deletion/orphan cleanup, and approved installed paths. RMA-133 owns candidate evaluation and default/recommended selection. RMA-134 owns the managed local LLM provider, and RMA-135 owns resource/thermal policy.

RMA-131 therefore does not claim that any real GGUF model is suitable, safe to redistribute, performant, installed, or selected.

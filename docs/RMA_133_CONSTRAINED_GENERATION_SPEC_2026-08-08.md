# RMA-133 Constrained Generation and V6 Model Selection Specification

**Date:** 2026-08-08  
**Status:** Proposed implementation specification  
**Parent work:** RMA-130, RMA-131, RMA-132, RMA-133  
**Target branch:** `master`

## 1. Purpose

RMA-133 must select an initial local GGUF language model for the Android Reachy Mini runtime using reproducible physical-device evidence. Candidate-set V5 demonstrated that the current failure is no longer primarily model size, memory, speed, or thermal headroom. The Qwen2.5-Coder-1.5B-Instruct Q4_K_M candidate produced semantically promising behavior, but every response was wrapped in a Markdown JSON fence and therefore correctly failed the strict output contract.

The next experiment shall add **generation-time constrained output** to `reachy_llama`, strengthen the semantic benchmark oracle where V5 exposed a false-positive weakness, and run a new V6 candidate comparison without weakening any numerical acceptance gate.

This work must remain fail-closed. It must not introduce post-generation JSON repair, Markdown stripping, parser recovery, hidden retries, hidden model switching, threshold reduction, permissive fallback behavior, or silent acceptance of malformed output.

## 2. V5 evidence that motivates this work

The V5 benchmark was executed from source SHA:

- `b245d732dfd7d7923060fa777c32b8a5ca12fe55`

GitHub Actions evidence:

- workflow: `RMA-133 local LLM benchmark`
- run: `31247094414`
- physical-device job: `93077401840`
- result: `completed / failure`
- artifact: `9019295576`
- artifact name: `rma133-local-llm-benchmark-v5-b245d732dfd7d7923060fa777c32b8a5ca12fe55`
- artifact digest: `sha256:3e113123499125b121918e592d78120a204c5553e0fa48aab23c3dd31fa6d8fb`
- device: LG-H872, `arm64-v8a`, Android API 26

The pinned Android runtime in that evidence used:

- llama.cpp release: `b10313`
- llama.cpp commit: `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`
- Android NDK: `28.2.13676358`
- CPU baseline: `armv8-a`
- static CPU-only backend

### 2.1 V5 Qwen2.5-Coder-1.5B result

Candidate:

- ID: `qwen2.5-coder-1.5b-instruct-q4-k-m`
- artifact SHA-256: `cc324af070c2ecbfd324a30884d2f951a7ff756aba85cb811a6ec436933bb046`
- source revision: `2ab9f8f42af02fc212effaef7c4850c885e965f4`

Measured V5 results:

- completed cases: 12/12
- schema reliability: 0.0 / 12 of 12
- recorded semantic quality: 0.0/100 because schema-invalid responses are not semantically scored
- mean decode speed: 3.0352 tokens/s
- peak RSS: 1,221,906,432 bytes
- load time: 3,823.281 ms
- mean time to first text: 96,182.642 ms
- battery before: 31.2 C
- battery peak/after: 38.0 C
- battery rise: 6.8 C
- parameter count reported by runtime: 1,777,088,000
- training context reported by runtime: 32,768 tokens

Every V5 response failed the strict schema gate because it was not exactly one JSON object. The 1.5B candidate consistently emitted a valid-looking JSON object inside a Markdown `json` code fence. The scorer correctly rejected this rather than repairing it.

A diagnostic-only analysis that removed only the outer Markdown fence produced a mean semantic score of approximately 89.58/100. This diagnostic **is not acceptance evidence** and must never be promoted into an official pass. It only establishes that V6 should first test generation-time constraints before increasing model size again.

The diagnostic also exposed specific semantic weaknesses:

- two required gaze targets were omitted;
- several speech-term expectations were missed;
- `reject_stale_target` received an incorrect raw-actuator refusal but still matched the old generic `can't` speech term.

The final issue means the semantic oracle itself must be tightened before V6 can be trusted.

## 3. Goals

The implementation shall:

1. preserve V1-V5 evidence and frozen contracts unchanged;
2. record V5 as a failed experiment with no selected model;
3. add explicit generation-time grammar constraints to the native `reachy_llama` runtime;
4. reject invalid or unusable constraints explicitly rather than falling back to unconstrained generation;
5. add contract tests for the constrained-generation API and sampler behavior;
6. define a checked-in, hashed RMA-133 behavior-output grammar;
7. strengthen the semantic benchmark oracle so generic refusal language cannot satisfy an unrelated case;
8. create a new V6 benchmark contract lineage instead of mutating V5;
9. benchmark the V5 candidate pair under the same numerical quality/resource/thermal gates;
10. select a model only if all frozen V6 gates pass on the physical Android device;
11. if no candidate passes, preserve the failure evidence and stop without promotion.

## 4. Non-goals

This work shall not:

- add post-generation JSON repair;
- strip Markdown fences before scoring or production consumption;
- accept JSON embedded inside prose;
- retry a malformed answer with an unconstrained or alternate prompt behind the caller's back;
- silently disable the grammar after grammar initialization failure;
- lower the 100% schema reliability requirement;
- lower the 85/100 semantic-quality requirement;
- increase memory or thermal thresholds to force a pass;
- silently switch models or providers;
- make cloud inference a fallback;
- select a production model from the diagnostic fence-stripped V5 results;
- rewrite or relabel historical V1-V5 evidence;
- couple raw LLM text directly to motor, joint, torque, angle, velocity, position, or coordinate actuation.

## 5. Benchmark lineage and immutability

V5 remains frozen under:

- benchmark ID: `rma133-initial-local-model-v2`
- experiment ID: `rma133-candidate-set-v5`

V6 shall use a new benchmark contract lineage because both the generation mechanism and semantic oracle change:

- benchmark ID: `rma133-initial-local-model-v3`
- experiment ID: `rma133-candidate-set-v6-constrained-generation`

The old scorer must continue to reproduce V1-V5 results when invoked against historical configs. V6-specific validation must be additive/versioned rather than changing historical expectations in place.

## 6. Numerical V6 acceptance gates

V6 shall retain the existing numerical gates exactly:

- completed cases: 12/12
- schema reliability: 1.0 / 12 of 12
- semantic quality: >= 85.0/100
- mean decode speed: >= 1.0 token/s
- peak RSS: <= 1,500,000,000 bytes
- maximum battery temperature: < 45.0 C
- battery temperature rise: <= 10.0 C

The existing deterministic ranking remains:

1. semantic quality descending;
2. schema reliability descending;
3. mean decode tokens/s descending;
4. peak RSS ascending;
5. load time ascending;
6. candidate ID ascending.

No new acceptance threshold may be added merely to disqualify an otherwise passing candidate after results are known. Performance measurements such as time-to-first-text may be recorded and discussed, but if they become a gate that change must be defined before the V6 physical run and must create a new frozen contract revision.

## 7. V6 candidates and runtime profile

V6 shall retain both V5 candidates so the effect of constrained generation is measured consistently rather than only on the favored model:

1. `qwen3-0.6b-q4-k-m` as the retained small-model control;
2. `qwen2.5-coder-1.5b-instruct-q4-k-m` as the larger local alternative.

The exact immutable model revisions, artifact URLs, sizes, quantization class, and SHA-256 hashes from V5 shall be retained.

The runtime profile shall remain:

- context tokens: 2048
- batch tokens: 256
- micro-batch tokens: 64
- maximum generated tokens: 128
- threads: 4
- batch threads: 4
- temperature: 0.0
- min-p: 0.0
- seed: 133
- stream queue capacity: 64

The V4 system prompt shall remain byte-identical for V6 unless a pre-run implementation defect proves that constrained generation requires a prompt contract change. If the system prompt changes, V6 must be re-frozen before any physical acceptance run and the new SHA-256 must be recorded.

## 8. Native runtime design

### 8.1 ABI policy

The current native runtime reports `REACHY_LLAMA_ABI_VERSION == 1`, and its validation requires `struct_size == sizeof(...)`. Therefore the implementation must not append fields to an ABI-1 struct while pretending ABI 1 is unchanged.

Constrained generation shall be introduced as a deliberate **ABI 2** runtime change.

Historical RMA-130 ABI-1 evidence remains valid as historical evidence. The new runtime supersedes it for first-party constrained-generation callers. The project shall not add a hidden compatibility shim or silently accept both structure layouts under one ABI number.

All first-party callers, tests, benchmark binaries, architecture documentation, and model-manifest compatibility checks that depend on the active runtime ABI must be updated intentionally.

### 8.2 Public constrained-generation contract

The runtime shall expose an explicit constrained-generation request rather than storing borrowed grammar pointers inside an asynchronously used config object.

A suitable design is:

```c
typedef enum reachy_llama_constraint_type {
    REACHY_LLAMA_CONSTRAINT_NONE = 0,
    REACHY_LLAMA_CONSTRAINT_GBNF = 1
} reachy_llama_constraint_type;

typedef struct reachy_llama_generation_constraint {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t type;
    uint32_t reserved;
    const char * grammar_utf8;
    size_t grammar_bytes;
    const char * root_utf8;
    size_t root_bytes;
} reachy_llama_generation_constraint;
```

and an explicit constrained start entry point such as:

```c
int32_t reachy_llama_generation_start_constrained(
    reachy_llama_model_handle model,
    const char * prompt_utf8,
    const reachy_llama_generation_config * config,
    const reachy_llama_generation_constraint * constraint,
    reachy_llama_generation_handle * out_generation,
    reachy_llama_error_info * error);
```

The exact API may be adjusted during implementation if the same invariants are preserved.

Required invariants:

- the runtime validates all sizes, enum values, reserved fields, pointer/length pairs, and upper bounds before launching the worker;
- the runtime deep-copies grammar and root strings before returning from the start call;
- the worker never dereferences caller-owned grammar memory;
- embedded NUL bytes are rejected rather than truncating the contract;
- grammar/root input has a documented bounded maximum size;
- invalid UTF-8 or malformed constraint input produces an explicit error;
- grammar sampler initialization failure produces an explicit terminal/start error;
- constrained-generation failure never falls back to the unconstrained sampler;
- cancellation, bounded stream queue behavior, model lifetime, and error reporting retain the RMA-130 fail-closed semantics.

### 8.3 Status/error reporting

Add explicit error statuses for constraint validation/initialization as needed. Do not map a grammar failure to success, end-of-sequence, or an empty completion.

Error messages must identify whether the failure occurred during:

- request validation;
- grammar parsing/initialization;
- sampling/acceptance;
- decoding;
- cancellation.

The benchmark must capture a constrained-generation failure as a failed case with evidence. It must never silently rerun unconstrained.

### 8.4 llama.cpp sampler integration

The implementation shall use the grammar sampler provided by the exact pinned llama.cpp source rather than hand-writing token masking logic.

Before coding the integration, verify the API and sampler lifecycle against commit `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`. If the required grammar API is not available or cannot be used safely at that pin, stop and document the incompatibility. A deliberate llama.cpp pin upgrade may then be proposed and validated separately. Do not substitute post-processing or parser repair.

The grammar constraint must participate in token sampling from the first generated token so that Markdown fences, `<think>` blocks, prose prefixes, and unknown JSON fields are unreachable outputs under the RMA-133 grammar.

Sampler ownership and cleanup must be RAII-safe or otherwise leak-free across success, error, cancellation, and initialization failure.

## 9. RMA-133 behavior-output grammar

Add a versioned checked-in grammar, for example:

- `benchmarks/rma133/behavior-output-v1.gbnf`

Its SHA-256 must be stored in the V6 config and emitted in benchmark evidence.

The grammar shall constrain the root output to one JSON object matching the behavior envelope. It shall permit only:

- `schema_version` with literal value `1`;
- non-empty JSON `speech` text;
- `gaze_target: null` or an object containing exactly `kind` and `entity_id`;
- `kind` value `tracked_entity` when a gaze object is present;
- entity IDs in the permitted `entity-...` lexical shape;
- expressions: `neutral`, `attentive`, `curious`, `pleased`, `concerned`, `surprised`;
- gestures: `none`, `nod`, `small_head_tilt`, `recoil`;
- urgency: `low`, `normal`, `high`.

The grammar shall not permit:

- Markdown fences;
- `<think>` or other reasoning wrappers outside the JSON object;
- additional top-level keys;
- raw actuator fields;
- trailing prose;
- multiple JSON objects.

The grammar may require a canonical field order to keep the grammar small and deterministic. The semantic JSON validator remains authoritative for content-level checks such as maximum speech length and unsafe keys.

## 10. Semantic oracle hardening

The current `speech_any_terms` OR-list is too permissive for safety-sensitive rejection cases. V5 proved that `reject_stale_target` can incorrectly receive credit for an unrelated actuator refusal because the generic word `can't` matches.

V6 shall introduce a versioned behavior case file, for example:

- `benchmarks/rma133/behavior_cases-v2.tsv`

Replace the single loose speech-term field with deterministic concept-group expectations. A suitable representation is:

- `speech_required_groups`: semicolon-separated groups, where each group contains `|` alternatives and **every group must match at least one alternative**;
- `speech_forbidden_terms`: optional `|` alternatives that must not appear.

Examples:

- stale target: require a tracking concept plus a stale/current-availability concept; forbid unrelated actuator/motor explanations;
- raw actuator request: require a safe/high-level-behavior refusal concept;
- camera unavailable: require a camera/vision concept plus an unavailable/cannot-see concept.

The speech component remains worth the same 25 points. The total semantic score remains 100 points. This is a stricter oracle, not a threshold reduction or score reweighting.

Tests must include the exact V5 false-positive phrase `I can't issue raw actuator commands.` against `reject_stale_target` and prove that it receives no speech credit under V6.

## 11. Strict output validation remains independent

Even though the grammar constrains generation, the scorer must continue independently validating the emitted bytes.

A grammar is not evidence that the output is valid. The scorer must still verify:

- exactly one JSON object;
- valid UTF-8;
- allowed top-level keys only;
- no unsafe actuation-related key fragments;
- schema version exactly 1;
- valid non-empty bounded speech;
- valid expression/gesture/urgency enum;
- valid gaze object shape and entity ID when present.

This defense-in-depth requirement prevents a runtime integration bug from being hidden by an assumption that grammar enforcement worked.

## 12. Benchmark evidence requirements

Each V6 raw result and final report shall record enough information to prove that constrained generation was actually active:

- benchmark ID and experiment ID;
- source SHA;
- exact llama.cpp pin;
- `reachy_llama` ABI version;
- candidate ID, revision, artifact hash, size, quantization;
- system prompt path and SHA-256;
- behavior case file path and SHA-256;
- grammar path and SHA-256;
- constraint type and grammar root;
- device serial/model/ABI/API;
- per-case completion status;
- per-case exact response bytes;
- prompt/generated token counts;
- time to first text and total time;
- decode tokens/s;
- RSS measurements;
- battery/thermal measurements;
- schema and semantic scoring reasons;
- selector decision.

If the runtime reports that the constraint was not active, the benchmark must fail before model selection.

## 13. CI and physical-device workflow

The permanent RMA-133 workflow shall validate the V6 frozen contract on hosted CI before the physical benchmark begins.

Hosted validation shall include:

- grammar file presence/hash checks;
- scorer unit tests;
- semantic false-positive regression tests;
- ABI-2 header/contract tests;
- constrained runtime build tests;
- benchmark source syntax/build checks;
- V1-V5 historical scorer/config compatibility tests.

The physical job shall:

1. check out the exact source SHA;
2. verify the exact llama.cpp pin;
3. build the ABI-2 Android runtime;
4. build the V6 benchmark binary;
5. push runtime, benchmark, prompt, cases, grammar, and exact model artifacts to the device;
6. verify model hashes before execution;
7. run each V6 candidate under the frozen profile;
8. score raw bytes without repair;
9. select only if all gates pass;
10. upload evidence even when no candidate passes.

An expected `no_candidate_passed` selector result may cause the workflow to conclude failure, but the evidence-upload step must still execute.

## 14. Selection and promotion rules

A candidate may be selected only from the exact V6 physical evidence and only if `eligible == true` under every gate.

If Qwen2.5-Coder-1.5B passes and ranks first, then and only then may follow-on closure work create the real RMA-131 model manifest and mark RMA-133 complete.

If Qwen3-0.6B unexpectedly passes and ranks first, the deterministic ranking result must be honored rather than preferring the larger candidate by assumption.

If neither candidate passes:

- preserve the result;
- do not create a selected production manifest;
- do not lower thresholds;
- do not strip or repair output;
- determine the next experiment from the actual rejection reasons.

## 15. Documentation closure after a V6 pass

After a successful exact-SHA V6 run, closure shall also correct stale size-specific documentation discovered during RMA-133:

- change current RMA-133 architecture wording from `initial sub-1B` to size-neutral initial-local-model wording while preserving historical V1-V4 descriptions;
- update `REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md` statements that still require approximately 1B or smaller;
- update the RMA-194 release checklist from `sub-1B-class` to the selected benchmark-backed local model;
- update model-manifest/runtime ABI documentation for the active ABI;
- mark RMA-130 roadmap status consistently with its already accepted runtime evidence if no unresolved blocker remains;
- mark RMA-133 complete only after the real selected-model manifest and final evidence are committed and exact-SHA CI is green.

## 16. Tests required before physical V6

At minimum, add tests proving:

1. valid GBNF constraint request succeeds;
2. null/invalid constraint request fails explicitly;
3. malformed grammar fails explicitly;
4. invalid grammar never falls back to unconstrained generation;
5. caller grammar memory can be released after start because the runtime deep-copies it;
6. grammar/root maximum lengths are enforced;
7. cancellation cleans up grammar/sampler/context state;
8. constrained output cannot begin with Markdown fences;
9. constrained output cannot emit `<think>` before the JSON object;
10. unknown top-level JSON keys cannot be emitted by the RMA-133 grammar;
11. the strict scorer still rejects hand-crafted malformed output independently of the grammar;
12. the V5 stale-target false-positive phrase fails the V6 speech oracle;
13. historical V1-V5 config/report tests continue to reproduce their original semantics;
14. V6 config rejects changed numerical thresholds;
15. V6 config rejects a missing/mismatched grammar hash;
16. benchmark evidence proves constrained mode was active.

## 17. Safety and failure policy

The following are release-blocking defects:

- any hidden unconstrained fallback after constraint failure;
- any JSON/Markdown repair in production or benchmark scoring;
- any alternate-model fallback not explicitly selected by the caller;
- any swallowed constraint initialization error;
- any scoring path that counts schema-invalid output as accepted semantic evidence;
- any benchmark that claims constrained generation without recording/verifying the grammar hash;
- any raw actuator instruction surviving into the behavior envelope;
- any modification to old evidence that changes a historical pass/fail result.

The preferred failure mode is explicit failure with preserved raw evidence.

## 18. Definition of done

This specification is complete only when:

- V5 failure evidence is committed and documented;
- ABI-2 constrained generation is implemented and tested;
- the RMA-133 GBNF contract is checked in and hashed;
- the semantic oracle regression is fixed with tests;
- V6 is frozen before execution;
- permanent hosted CI passes on the V6 source SHA;
- the physical LG-H872 V6 benchmark completes and uploads evidence;
- a candidate either passes every gate and is selected deterministically, or the no-pass result is preserved without promotion;
- if selected, the real model manifest, roadmap/doc closure, and final exact-SHA CI are completed;
- no repair, fallback, threshold reduction, or silent failure path was introduced.

# RMA-134 Local LLM Provider Specification

**Date:** 2026-08-08  
**Roadmap task:** RMA-134 — Implement local LLM provider  
**Target branch:** `master`  
**Depends on:** RMA-130, RMA-131, RMA-132, RMA-133  
**Next owner:** RMA-135 resource and thermal governor

## 1. Purpose

RMA-134 turns the accepted `reachy_llama` ABI-2 runtime and the RMA-133-selected Qwen3-0.6B Q4_K_M artifact into a first-party managed local LLM provider that can be used by later conversation/behavior orchestration.

The provider must stream generation without blocking the Unity/simulation thread, support cancellation and conversation reset, enforce explicit context/output limits, validate the final high-level behavior intent, and fail to a visible local unavailable/error state. It must never make or authorize a hidden cloud request.

RMA-134 is not the thermal/resource governor. Runtime resource knobs must remain an explicit input profile so RMA-135 can reduce or suspend inference without rewriting provider semantics.

## 2. Existing trusted boundaries

RMA-134 consumes two separately validated objects:

1. a `LocalModelManifest` from RMA-131/RMA-133, and
2. a `LocalModelApprovedArtifact` issued by RMA-132 after path confinement, byte-count, and SHA-256 validation.

The production provider must not accept an arbitrary GGUF path as a public creation API. Manifest ID, model ID, artifact byte count, artifact SHA-256, runtime ID, and ABI requirement must match before native load.

The current selected model is `qwen3-0.6b` / `qwen3-0.6b-q4-k-m` with artifact SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e` and `reachy_llama` ABI 2.

## 3. Frozen behavior-generation contract

RMA-134 must preserve the behavior-output contract that actually passed RMA-133 V6 rather than replacing it with a looser prompt/parser combination.

The first production behavior profile is source-bound to:

- system prompt: `benchmarks/rma133/system_prompt-v4.txt`;
- GBNF grammar: `benchmarks/rma133/behavior-output-v1.gbnf`;
- grammar SHA-256: `2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734`;
- grammar root: `root`;
- Qwen3 user suffix: `/no_think`;
- output schema version: `1`.

The provider may embed these exact checked-in bytes for runtime packaging, but permanent tests must prove the embedded contract still matches the frozen repository files/hashes. A later contract change is a deliberate versioned change, not an implicit prompt tweak.

## 4. Public managed provider boundary

The public RMA-134 boundary should expose:

- explicit provider state (`Unavailable`, `Loading`, `Ready`, `Generating`, `Faulted`, `Disposed`);
- explicit creation/reload results;
- a required execution profile;
- bounded conversation messages supplied by the caller;
- streamed untrusted text fragments/events;
- a terminal result containing a strictly validated behavior intent only on success;
- cancellation;
- conversation reset;
- diagnostics/metrics needed by RMA-135 and later UI work.

The provider does **not** own a hidden transcript database. The caller supplies the bounded conversation history for each request. Conversation reset rotates a provider conversation epoch and cancels/invalidate any in-flight generation so text from the prior epoch cannot become a successful current result.

This keeps transcript policy available to RMA-150/RMA-160 rather than silently creating persistence in RMA-134.

## 5. Execution profile and RMA-135 boundary

RMA-134 must not infer or silently tune resource settings from device temperature or memory.

`LocalLlmExecutionProfile` must explicitly carry at least:

- context tokens;
- batch tokens;
- micro-batch tokens;
- maximum generated tokens;
- generation threads;
- batch threads;
- temperature;
- min-p;
- seed;
- bounded native stream queue capacity; and
- managed request/response bounds.

A named RMA-133 V6 baseline helper may reproduce the accepted benchmark profile exactly:

- context `2048`;
- batch `256`;
- micro-batch `64`;
- max generation `128`;
- threads `4` / batch threads `4`;
- temperature `0.0`;
- min-p `0.0`;
- seed `133`;
- stream queue `64`.

RMA-135 may later supply a lower-resource profile or deny inference. RMA-134 must not independently change profiles mid-generation.

## 6. Prompt construction and context enforcement

For each request:

1. prepend the frozen behavior system prompt;
2. accept only bounded `user`/`assistant` history from the caller; callers cannot replace the system prompt;
3. require the final supplied message to be a user message;
4. append `/no_think` only through the explicit selected-model behavior profile;
5. apply the exact manifest chat template through `reachy_llama_apply_chat_template`;
6. query the native tokenizer for the exact templated prompt token count;
7. reject before generation if `prompt_tokens + max_generated_tokens > context_tokens` or if the requested context exceeds the manifest model limit.

There is no prompt truncation, history dropping, context auto-shrinking, or retry with a shorter prompt.

## 7. Streaming and thread isolation

Native `reachy_llama` already performs context creation, prefill, sampling, and decode on its own worker thread. RMA-134 adds a managed worker that performs native start/poll/cancel/release and event dispatch away from the Unity/simulation caller thread.

Rules:

- one provider/model has at most one active generation;
- concurrent start returns explicit `Busy`; it is not hidden in a request queue;
- native `NONE` poll results are not fabricated into text or success;
- native text fragments are forwarded in sequence without silent drop/coalescing;
- streamed text is explicitly untrusted/non-executable until terminal validation;
- sink/callback failure cancels the native generation and becomes an explicit consumer failure;
- terminal native completion is not surfaced as provider success until the complete bytes pass strict intent validation.

## 8. Cancellation and conversation reset

Cancellation must request native `reachy_llama_generation_cancel`, continue bounded cleanup until the native terminal state is observed, and release the generation handle.

Conversation reset must:

- atomically advance the provider conversation epoch;
- request cancellation of any active generation;
- prevent any later fragment or terminal result from the old epoch from becoming a current successful intent;
- retain no hidden conversation history.

A reset is not a provider/model fallback and does not start a replacement generation automatically.

## 9. Strict behavior-intent validation

The final response is accepted only if it is exactly one JSON object matching schema version 1.

Required fields:

- `schema_version`: integer `1`;
- `speech`: JSON string, at most 160 characters;
- `expression`: one of `neutral`, `attentive`, `curious`, `pleased`, `concerned`, `surprised`;
- `gesture`: one of `none`, `nod`, `small_head_tilt`, `recoil`;
- `urgency`: one of `low`, `normal`, `high`.

Optional `gaze_target`, when present, is `null` or exactly `{ "kind": "tracked_entity", "entity_id": "entity-N" }` with decimal digits after `entity-`.

Unknown keys, duplicate keys, trailing bytes/prose, Markdown fences, `<think>`, raw-actuation fields, invalid UTF-8/JSON escapes, invalid enums, malformed gaze objects, or overlong speech fail explicitly. No JSON repair, fence stripping, unknown-key ignore, or parser recovery is permitted.

RMA-151 may later promote/reconcile this versioned intent type into the wider orchestrator schema, but RMA-134 must validate what it executes today.

## 10. Native interop rules

Managed interop must bind ABI 2 exactly and use `struct_size`/`abi_version` fields expected by `reachy_llama.h`.

It must use the native query/copy protocol rather than guessing buffer sizes:

- chat-template output: query required bytes, then allocate/copy;
- tokenization: query exact required token count;
- generation polling: on text, query required bytes and re-poll with that capacity;
- errors: decode only the bounded `reachy_llama_error_info` message.

All managed/native string transfer is UTF-8. Temporary unmanaged prompt/template/message/grammar buffers are freed on every path.

A malformed/failed grammar start remains an explicit native/provider failure. There is no call to unconstrained `reachy_llama_generation_start` from the production behavior provider.

## 11. Failure, availability, and recovery

Creation can return explicit `Unavailable` for missing/unapproved artifact, ABI mismatch, incompatible manifest, or model-load failure. It must not substitute another model/provider.

Generation can return explicit statuses including at least:

- success;
- busy;
- invalid request;
- context limit;
- cancelled;
- superseded by reset;
- invalid intent;
- runtime failure;
- consumer/sink failure;
- unavailable/disposed.

The provider supports in-process model reload/recovery when no generation is active. A failed reload remains `Faulted`/`Unavailable`; it does not require restarting the application, but it also does not automatically switch provider.

## 12. Privacy and network boundary

The RMA-134 production implementation contains no HTTP client, socket client, API key, cloud endpoint, provider selection, or model download logic.

It does not persist prompts, generated text, or conversation history. Diagnostic results may report bounded status/metrics but must not silently log full private prompt/response content.

Cross-provider policy remains RMA-146 and defaults disabled.

## 13. Validation strategy

### Hosted managed contracts

Use an injected deterministic fake runtime to prove:

- approved-artifact/manifest identity checks;
- ABI mismatch and load failure -> explicit unavailable;
- worker-thread dispatch;
- exact prompt/template input and selected `/no_think` suffix;
- exact context preflight with no truncation;
- single-active-generation `Busy` behavior;
- ordered streaming with no drop;
- cancellation -> native cancel + release;
- reset -> stale-output suppression/supersession;
- invalid final intent never becomes success;
- unknown/raw-actuation keys are rejected;
- sink failure cancels and fails visibly;
- runtime error is preserved;
- dispose unloads the model;
- reload can recover without app restart.

### Static contracts

Permanent source tests must reject:

- unconstrained generation calls from the production provider;
- HTTP/cloud/provider-fallback code in the local provider;
- JSON repair/fence stripping/retry helpers;
- arbitrary public model-path construction;
- drift between the embedded behavior contract and frozen RMA-133 files.

### Hosted native smoke

Build the managed interop against the current ABI-2 definitions and retain existing RMA-130/RMA-133 native lifecycle/sanitizer tests.

### Physical Android acceptance

On the representative LG-H872 unless explicitly changed:

- obtain the exact RMA-133-selected installed artifact through the approved package boundary or a validation-only equivalent that verifies the exact manifest SHA before provider creation;
- load it through the managed RMA-134 provider/interop;
- generate at least one frozen high-level behavior case offline;
- prove terminal intent validation success;
- exercise cancellation and a second generation without app/process restart;
- run simulation timing concurrently and record whether the existing physics budget is preserved;
- record memory/thermal observations without changing policy during the run.

If warm-device throughput or physics interference appears, preserve it as RMA-135 evidence. Do not weaken RMA-134 correctness gates to hide resource pressure.

## 14. Completion criteria

RMA-134 is complete only when:

- production provider code is merged on `master`;
- hosted managed/static/native gates are green on the exact source SHA;
- representative-phone offline generation succeeds through the managed provider;
- final intent validation is proven;
- cancellation/reset/recovery are proven;
- physics timing acceptance is recorded;
- no hidden cloud/model/provider fallback exists; and
- the roadmap TODO records exact source/run/job/artifact evidence.

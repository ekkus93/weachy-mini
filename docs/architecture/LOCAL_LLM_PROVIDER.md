# Local LLM Provider

## Status

RMA-134 introduces the first product-facing local language-model provider over the first-party `reachy_llama` ABI-2 runtime selected and constrained by RMA-130 through RMA-133.

The provider is local-only. It has no cloud transport, alternate provider, alternate model, output-repair path, or unconstrained-generation path.

## Model authority

A provider may be constructed only from a `LocalModelApprovedArtifact` produced by the RMA-132 package manager and the matching `LocalModelManifest`. The provider verifies that manifest ID, model ID, file size, SHA-256, runtime ID, ABI version, and no-network requirement agree before any native model load.

RMA-133 selected `qwen3-0.6b` / Q4_K_M. The product profile initially mirrors the successful physical V6 profile:

- context: 2,048 tokens;
- batch: 256 tokens;
- micro-batch: 64 tokens;
- maximum generated output: 128 tokens;
- generation threads: 4;
- batch threads: 4;
- temperature: 0;
- min-p: 0;
- seed: 133;
- native stream queue capacity: 64.

A configuration exceeding the selected manifest's context, measured-memory basis, batch basis, or recommended thread count is rejected before loading.

## Runtime boundary

`ILocalLlmRuntimeFactory` and `ILocalLlmModelSession` intentionally expose only the operations required by the product provider:

1. load the exact approved artifact;
2. apply the model's embedded chat template;
3. count the rendered prompt tokens;
4. start **constrained** generation;
5. poll/cancel/release the generation.

There is deliberately no unconstrained generation method on the managed runtime interface. `ReachyLlamaNativeRuntimeFactory` binds only the ABI-2 constrained start for product generation.

The GBNF grammar and system prompt used by the selected V6 benchmark are packaged as product resources. Their bytes are independently hash-checked against the frozen RMA-133 sources.

## Worker and streaming ownership

Only one generation may be active per provider. A concurrent request returns an explicit `Busy` result; requests are not silently queued.

Generation polling runs on a worker task, never the Unity presentation thread. Raw generated text is published as bounded `OutputDelta` events. One queue slot is reserved for a terminal event. If the consumer cannot keep up and the bounded queue fills, generation is cancelled and the operation fails visibly rather than allowing unbounded memory growth.

Raw deltas are informational only. They are not executable behavior intent.

## Behavior-intent validation

RMA-134 deliberately does not define the future application-wide planner schema owned by RMA-151. It validates the narrower frozen RMA-133 output contract:

- exactly one JSON object;
- numeric `schema_version: 1`;
- speech no longer than 160 characters;
- optional `gaze_target` only in the exact tracked-entity shape;
- frozen expression, gesture, and urgency enums;
- no unknown fields;
- no trailing Markdown, XML, reasoning text, second object, or raw-actuation field.

A completed native generation is not exposed as a `Completed` behavior intent unless the independent managed parser accepts the full object.

## Conversation transaction contract

Conversation state uses committed turns only. A turn enters history only after:

1. native constrained generation reaches completion;
2. the independent behavior parser accepts the exact output;
3. the generation handle releases successfully;
4. the conversation epoch is still current and the turn was not cancelled.

Invalid, cancelled, timed-out, overflowed, faulted, or cleanup-failed turns are never committed.

History has an explicit turn bound. The provider does not silently truncate old turns. When the configured bound is reached, the next request fails with `ContextLimit` until the caller explicitly resets the conversation.

Before every generation, the complete rendered chat template is token-counted. The provider reserves the full configured output allowance and rejects the turn if prompt plus reserve exceeds the configured context.

For the selected Qwen3 model the final current-user message receives the explicit `/no_think` suffix used by the accepted RMA-133 benchmark. This is part of the selected-model product configuration, not a parser repair.

## Cancellation, reset, and fault recovery

Caller cancellation, provider/reset cancellation, and timeout are distinct signals.

- Caller cancellation or reset produces an explicit cancelled terminal event.
- Timeout produces `TimedOut`.
- Reset cancels any active turn, waits for terminal native cleanup, clears committed history, and retains the verified loaded model when the runtime remains healthy.
- Native runtime errors place the provider in retained `Faulted` state.
- A faulted provider does not automatically retry, reload, select another model, or use a cloud provider.
- Recovery requires an explicit `ReloadAsync` request against the same approved artifact and manifest.

Cancellation must drain the native generation to a terminal event within a bounded cleanup interval. A cancel, drain, or release failure becomes a visible runtime fault; it is never swallowed.

## Unavailable behavior

Missing model, ABI mismatch, load failure, package mismatch, and runtime fault all remain explicit provider states. The application may report that the local LLM is unavailable. RMA-134 does not authorize any hidden cloud request or alternate model/provider fallback.

## Physical acceptance

RMA-134 device acceptance must use the exact selected Qwen3 artifact on the supported ARM64 Android phone and production ABI-2 runtime. In addition to proving real constrained provider output, it records authoritative simulation timing before and after inference so local generation cannot be accepted merely because the app stayed alive. Physics remains authoritative and higher priority than inference.

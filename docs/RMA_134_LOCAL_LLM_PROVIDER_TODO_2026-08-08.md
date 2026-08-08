# RMA-134 Local LLM Provider TODO

**Date:** 2026-08-08  
**Spec:** `docs/RMA_134_LOCAL_LLM_PROVIDER_SPEC_2026-08-08.md`  
**Status:** In progress  
**Target branch:** `master`

## Operating rules

- Work from the current green RMA-133 closure state.
- Consume only a validated `LocalModelManifest` plus RMA-132 `LocalModelApprovedArtifact`; no arbitrary public GGUF path.
- Require `reachy_llama` ABI 2 exactly.
- Preserve the RMA-133 V6 behavior system prompt, GBNF grammar, grammar root, and selected Qwen3 `/no_think` suffix as a versioned production contract.
- Never call unconstrained generation from the behavior provider.
- Never add JSON repair, Markdown stripping, parser recovery, hidden retry, hidden history truncation, model substitution, provider substitution, or cloud fallback.
- Stream partial text as untrusted only. A native completed event is not provider success until the full response passes strict intent validation.
- Keep all resource knobs explicit. RMA-134 may expose the exact RMA-133 baseline profile; RMA-135 owns thermal/memory-driven profile changes or suspension.
- Preserve warm-device RMA-133 evidence; do not assume cool-start throughput is continuously available.
- Do not mark RMA-134 complete until exact-SHA hosted CI and physical Android provider acceptance are green.

---

## Phase 0 — Freeze provider scope

### RMA-134-LP-001 — Add dedicated architecture/specification

- [x] Define trusted RMA-131/RMA-132/RMA-133 inputs.
- [x] Define no-arbitrary-path creation boundary.
- [x] Define no-cloud/no-provider-fallback policy.
- [x] Define explicit RMA-135 execution-profile boundary.
- [x] Define conversation reset/stale-output semantics.
- [x] Define final intent-validation gate.
- [x] Define hosted and physical acceptance strategy.

**Phase 0 gate:** provider semantics are frozen before implementation.

---

## Phase 1 — Add managed provider contracts

### RMA-134-LP-010 — Define provider states/results

- [ ] Add explicit provider lifecycle state enum.
- [ ] Add explicit creation/reload result types.
- [ ] Add explicit generation terminal statuses including busy, context-limit, cancelled, superseded, invalid-intent, runtime failure, consumer failure, unavailable, and disposed.
- [ ] Ensure every failure includes bounded diagnostic detail and no private prompt/response logging.

### RMA-134-LP-011 — Define execution profile

- [ ] Add immutable explicit context/batch/ubatch/max-generation/thread/sampling/queue fields.
- [ ] Add bounded managed request/response limits.
- [ ] Validate runtime-native structural constraints before generation.
- [ ] Validate requested context against the manifest context limit.
- [ ] Add an exact named RMA-133 V6 baseline profile helper.
- [ ] Do not add device-temperature or memory adaptation here.

### RMA-134-LP-012 — Define request/stream contracts

- [ ] Add bounded user/assistant conversation message type.
- [ ] Forbid caller-provided replacement system messages.
- [ ] Require final message to be `user`.
- [ ] Add request identity and provider conversation epoch visibility.
- [ ] Add ordered untrusted text/event stream sink contract.
- [ ] Make partial text explicitly non-executable.

**Phase 1 gate:** public contracts make fallback, limits, and stale output explicit.

---

## Phase 2 — Add ABI-2 managed native interop

### RMA-134-LP-020 — Bind ABI-2 structures and symbols

- [ ] Add managed layouts for error/model/generation/constraint/chat/event/metrics structs.
- [ ] Bind ABI/version/status/default-config/model-load/unload/template/tokenize/constrained-start/poll/cancel/metrics/release.
- [ ] Do not bind/use unconstrained start from the production behavior provider path.
- [ ] Validate managed struct sizes in hosted tests.

### RMA-134-LP-021 — Implement UTF-8 ownership helpers

- [ ] Encode strings with strict UTF-8 and explicit NUL termination for native calls.
- [ ] Allocate/free all temporary unmanaged strings on every path.
- [ ] Reject embedded NUL in managed inputs before native calls.
- [ ] Decode native text/errors with strict bounded UTF-8.

### RMA-134-LP-022 — Implement query/copy protocols

- [ ] Query exact chat-template byte capacity before copy.
- [ ] Query exact prompt token count before generation.
- [ ] Poll text using required-byte query then exact copy.
- [ ] Preserve native event sequence and status.
- [ ] Treat unexpected buffer/status combinations as explicit runtime failures.

**Phase 2 gate:** managed interop mirrors the accepted native ABI without guessed capacities or ownership leaks.

---

## Phase 3 — Freeze production behavior contract

### RMA-134-LP-030 — Add versioned embedded behavior profile

- [ ] Embed exact RMA-133 V4 system prompt bytes for Android runtime availability.
- [ ] Embed exact RMA-133 `behavior-output-v1.gbnf` bytes.
- [ ] Pin grammar SHA-256 and root `root`.
- [ ] Pin selected Qwen3 model/artifact identity.
- [ ] Pin selected Qwen3 `/no_think` user suffix.
- [ ] Reject the selected production profile if manifest/artifact identity differs.

### RMA-134-LP-031 — Add drift regression coverage

- [ ] Prove embedded system prompt matches frozen RMA-133 source bytes/hash.
- [ ] Prove embedded grammar matches frozen RMA-133 source bytes/hash.
- [ ] Prove selected suffix is exactly `/no_think`.
- [ ] Prove unknown model IDs do not inherit the Qwen3 suffix/profile silently.

**Phase 3 gate:** production prompt/grammar semantics are identical to the accepted benchmark lineage.

---

## Phase 4 — Implement strict behavior-intent parser

### RMA-134-LP-040 — Parse exact schema-v1 object

- [ ] Require one JSON object and no trailing bytes/prose.
- [ ] Require numeric `schema_version == 1`.
- [ ] Require speech/expression/gesture/urgency exactly once.
- [ ] Allow optional gaze only in the accepted position/shape.
- [ ] Reject duplicate/unknown keys.
- [ ] Decode JSON string escapes strictly.

### RMA-134-LP-041 — Validate values and safety shape

- [ ] Enforce speech <=160 characters.
- [ ] Enforce six expression enum values.
- [ ] Enforce four gesture enum values.
- [ ] Enforce three urgency enum values.
- [ ] Enforce tracked gaze kind and `entity-[0-9]+` IDs.
- [ ] Reject raw actuator/joint/torque/coordinate keys and any other unknown fields.
- [ ] Reject Markdown fences, `<think>`, second objects, or trailing prose.

### RMA-134-LP-042 — Prohibit repair

- [ ] Add tests proving fenced JSON fails.
- [ ] Add tests proving leading/trailing explanation fails.
- [ ] Add tests proving wrong string schema version fails.
- [ ] Add tests proving unknown/raw-actuation fields fail.
- [ ] Do not strip, repair, or retry malformed output.

**Phase 4 gate:** only a strict high-level behavior object can become an executable provider result.

---

## Phase 5 — Implement provider lifecycle and generation

### RMA-134-LP-050 — Create/load from approved artifact

- [ ] Verify manifest/artifact manifest ID, model ID, byte count, SHA identity, runtime ID, and ABI.
- [ ] Run model load away from the caller/Unity thread.
- [ ] Enable native tensor checking intentionally.
- [ ] Return explicit `Unavailable`/failure on load error.
- [ ] Never search for another model automatically.

### RMA-134-LP-051 — Build exact prompt and enforce context

- [ ] Inject fixed system prompt internally.
- [ ] Apply selected suffix only to final user message.
- [ ] Apply manifest chat template through native ABI.
- [ ] Tokenize exact templated prompt.
- [ ] Reject context overflow before start.
- [ ] Do not truncate/drop history or shrink requested output automatically.

### RMA-134-LP-052 — Stream generation on managed worker

- [ ] Start only `generation_start_constrained` with GBNF.
- [ ] Reject concurrent generation with explicit `Busy`.
- [ ] Poll on worker/thread-pool execution, not Unity/simulation caller thread.
- [ ] Forward every text fragment in native sequence.
- [ ] Keep text fragments marked untrusted.
- [ ] Assemble a separately bounded final response for validation.

### RMA-134-LP-053 — Terminal validation

- [ ] On native completed, strictly parse complete response.
- [ ] Emit provider success only after parse/validation succeeds.
- [ ] Invalid final output -> `InvalidIntent` and no executable intent.
- [ ] Native error -> explicit runtime failure preserving status.
- [ ] Always release terminal generation handles.

### RMA-134-LP-054 — Cancellation

- [ ] User cancellation requests native cancel.
- [ ] Continue cleanup until terminal cancellation/error is observed.
- [ ] Release generation handle.
- [ ] Do not auto-restart after cancellation.

### RMA-134-LP-055 — Conversation reset

- [ ] Atomically rotate conversation epoch.
- [ ] Cancel active generation.
- [ ] Suppress post-reset old-epoch text from success/current output.
- [ ] Return old request as `Superseded` where appropriate.
- [ ] Retain no hidden transcript/history.

### RMA-134-LP-056 — Consumer failure

- [ ] If stream sink throws/fails, cancel native generation.
- [ ] Clean up and release.
- [ ] Return explicit consumer failure.
- [ ] Never swallow the sink failure and report generation success.

### RMA-134-LP-057 — In-process recovery and disposal

- [ ] Add explicit reload when no generation is active.
- [ ] Failed reload remains visible/faulted.
- [ ] Successful reload returns to ready without app restart.
- [ ] Dispose cancels/cleans any active generation and unloads the model.

**Phase 5 gate:** managed lifecycle is fail-closed, stale-safe, cancellable, and recoverable without hidden retries/fallbacks.

---

## Phase 6 — Hosted managed/static validation

### RMA-134-LP-060 — Add dedicated managed test project

- [ ] Test artifact/manifest mismatch.
- [ ] Test ABI mismatch.
- [ ] Test model-load failure.
- [ ] Test exact RMA-133 baseline profile.
- [ ] Test worker-thread generation.
- [ ] Test exact prompt + `/no_think` construction.
- [ ] Test context preflight and no truncation.
- [ ] Test ordered stream delivery.
- [ ] Test `Busy` instead of hidden queue.
- [ ] Test cancellation/cancel/release.
- [ ] Test reset/stale suppression.
- [ ] Test valid intent success.
- [ ] Test malformed/unsafe intent failure.
- [ ] Test sink failure.
- [ ] Test runtime terminal error.
- [ ] Test reload recovery.
- [ ] Test dispose/unload.

### RMA-134-LP-061 — Add static regression test

- [ ] Reject HTTP/network client usage in RMA-134 production provider files.
- [ ] Reject unconstrained generation call usage in production provider path.
- [ ] Reject repair/fence-stripping/parser-recovery helpers.
- [ ] Reject public arbitrary-path provider creation.
- [ ] Verify frozen prompt/grammar source/hash linkage.

### RMA-134-LP-062 — Add permanent workflow

- [ ] Build `ReachyMini.Core` warnings-as-errors.
- [ ] Run managed RMA-134 contracts.
- [ ] Run static RMA-134 contracts.
- [ ] Compile static-test Python.
- [ ] Run relevant native ABI-2 hosted contracts or a focused interop smoke.
- [ ] Upload exact-SHA evidence.
- [ ] Publish `RMA-134 Local LLM Provider` commit status.

**Phase 6 gate:** exact hosted source SHA is green before physical acceptance.

---

## Phase 7 — Physical Android provider acceptance

### RMA-134-LP-070 — Build/stage product provider runtime

- [ ] Stage current ABI-2 `libreachy_llama.so` into Unity Android runtime.
- [ ] Ensure managed RMA-134 interop/provider code is included in ARM64/API-26 build.
- [ ] Verify no unexpected dynamic dependencies or exports changed.

### RMA-134-LP-071 — Prepare exact selected model

- [ ] Use the RMA-133 selected Qwen3-0.6B Q4_K_M artifact only.
- [ ] Verify exact file size and SHA-256 before provider creation.
- [ ] Bind exact real manifest.
- [ ] Do not download/switch models as a hidden test fallback.

### RMA-134-LP-072 — Offline generation acceptance

- [ ] Run on LG-H872 unless an explicit device change is documented first.
- [ ] Prove provider creation/load through managed ABI path.
- [ ] Generate at least one frozen behavior case offline.
- [ ] Prove ordered streaming occurred.
- [ ] Prove final strict intent validation succeeded.
- [ ] Record prompt/generated-token metrics and native/provider statuses.

### RMA-134-LP-073 — Cancellation/reset/recovery acceptance

- [ ] Cancel an in-flight generation and prove cleanup/release.
- [ ] Reset conversation and prove stale old-epoch output is not accepted.
- [ ] Generate again without app/process restart.
- [ ] Exercise explicit reload/recovery without provider fallback.

### RMA-134-LP-074 — Physics coexistence gate

- [ ] Run representative simulation stepping while local generation is active.
- [ ] Record physics timing against the existing budget.
- [ ] Record memory and thermal observations.
- [ ] If resource pressure causes failure, preserve evidence for RMA-135 rather than weakening correctness.

**Phase 7 gate:** the selected model produces a validated offline intent through the real managed provider on a representative phone, with lifecycle recovery and recorded physics coexistence.

---

## Phase 8 — Closure

### RMA-134-LP-080 — Add validation record

- [ ] Create `docs/validation/RMA_134_LOCAL_LLM_PROVIDER_VALIDATION_2026-08-08.md` (or actual execution date).
- [ ] Record exact source SHA, hosted run/job/artifact IDs/digests, physical run/job/artifact IDs/digests, device, selected model hash, runtime ABI/pin, provider profile, generation metrics, cancellation/reset/reload results, and physics timing.
- [ ] Explicitly state no cloud/model/provider fallback or JSON repair was used.

### RMA-134-LP-081 — Close roadmap task

- [ ] Mark all five RMA-134 roadmap bullets complete only after hosted + physical gates pass.
- [ ] Add completion evidence to `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md`.
- [ ] Preserve RMA-135 ownership of adaptive resource/thermal policy.

### RMA-134-LP-082 — Final exact-SHA CI

- [ ] Commit closure docs/evidence.
- [ ] Verify permanent CI on exact closure SHA.
- [ ] Verify RMA-134 dedicated status green.
- [ ] Ensure closure-only docs do not invalidate physical evidence inputs.

---

## Final audit checklist

- [ ] Provider accepts only manifest + approved artifact boundary.
- [ ] ABI 2 required exactly.
- [ ] Selected production behavior contract matches RMA-133 V6.
- [ ] Qwen3 `/no_think` behavior is explicit and model-bound.
- [ ] No arbitrary system-prompt replacement.
- [ ] Context/output limits are explicit and fail closed.
- [ ] No hidden history truncation.
- [ ] One active generation; `Busy` instead of hidden queue.
- [ ] Stream fragments are ordered and untrusted.
- [ ] Native completed is not provider success before strict parse.
- [ ] Invalid intent never becomes executable output.
- [ ] Cancellation releases native generation.
- [ ] Reset suppresses stale old-epoch output.
- [ ] Reload recovers in-process without app restart.
- [ ] No unconstrained fallback.
- [ ] No cloud/model/provider fallback.
- [ ] No JSON repair/Markdown stripping/parser recovery.
- [ ] No private transcript persistence introduced.
- [ ] RMA-135 remains owner of adaptive resource/thermal decisions.
- [ ] Exact hosted and physical evidence is green before roadmap closure.

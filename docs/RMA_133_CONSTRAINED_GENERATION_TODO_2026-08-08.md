# RMA-133 Constrained Generation and V6 Model Selection TODO

**Date:** 2026-08-08  
**Spec:** `docs/RMA_133_CONSTRAINED_GENERATION_SPEC_2026-08-08.md`  
**Status:** Ready for implementation / Ralph loop  
**Target branch:** `master`

## Operating rules

- Work directly from the current `master` state unless a later explicit instruction changes that.
- Preserve V1-V5 benchmark evidence and frozen contracts unchanged.
- Do not weaken any numerical V6 acceptance gate.
- Do not add JSON repair, Markdown stripping, parser recovery, hidden retries, hidden model/provider fallback, or silent fallback to unconstrained generation.
- A failed grammar/constraint must fail explicitly.
- A failed candidate selection must preserve evidence and remain a failure; never promote a candidate by exception.
- Commit permanent evidence and source changes intentionally. Verify exact source SHAs in CI before marking tasks complete.
- Do not mark RMA-133 complete until a real selected-model manifest exists and exact-SHA closure CI is green.

---

## Phase 0 — Preserve and document V5 failure

### RMA-133-CG-001 — Add permanent V5 validation record

- [x] Create `docs/validation/RMA_133_CANDIDATE_SET_V5_VALIDATION_2026-08-08.md`.
- [x] Record source SHA `b245d732dfd7d7923060fa777c32b8a5ca12fe55`.
- [x] Record run `31247094414` and physical job `93077401840`.
- [x] Record artifact `9019295576` and digest `sha256:3e113123499125b121918e592d78120a204c5553e0fa48aab23c3dd31fa6d8fb`.
- [x] Record device LG-H872 / `arm64-v8a` / API 26.
- [x] Record exact llama.cpp commit `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb` and release `b10313`.
- [x] Record both V5 candidate reports and selector result `no_candidate_passed`.
- [x] Record that Qwen2.5-Coder-1.5B completed 12/12 but had schema reliability 0.0 because responses were Markdown-fenced rather than exactly one JSON object.
- [x] Record the 1.5B resource metrics: 3.0352 tok/s, 1,221,906,432-byte peak RSS, 38.0 C peak battery temperature, +6.8 C rise, 3,823.281 ms load.
- [x] Clearly label the approximately 89.58/100 fence-stripped rescore as **diagnostic only**, not acceptance evidence.
- [x] Record the discovered semantic-oracle false positive for `reject_stale_target`.
- [x] State explicitly that no model was selected from V5.

### RMA-133-CG-002 — Add V5 evidence regression coverage

- [x] Add/extend tests that load the V5 config/report fixture and prove the selector returns no selected candidate.
- [x] Prove Markdown-fenced output remains schema-invalid under the historical V5 scorer.
- [x] Ensure no new V6 code changes V5 historical results.

**Phase 0 gate:** V5 failure is reproducibly documented before V6 can be treated as a new experiment.

---

## Phase 1 — Freeze the V6 contract lineage

### RMA-133-CG-010 — Add V6 candidate config

- [x] Create `benchmarks/rma133/candidates-v6.json`.
- [x] Set `benchmark_id` to `rma133-initial-local-model-v3`.
- [x] Set `experiment_id` to `rma133-candidate-set-v6-constrained-generation`.
- [x] Retain the exact V5 Qwen3-0.6B control artifact/revision/hash.
- [x] Retain the exact V5 Qwen2.5-Coder-1.5B artifact/revision/hash.
- [x] Retain Q4_K_M for both candidates.
- [x] Retain the V5/V4 system-prompt bytes and record its exact SHA-256.
- [x] Add a required constrained-generation contract section.
- [x] Record grammar path, grammar SHA-256, constraint type `GBNF`, and grammar root.
- [x] Record behavior-case-v2 path and SHA-256.
- [x] Keep the runtime profile exactly: context 2048, batch 256, ubatch 64, max generation 128, threads 4/4, temperature 0.0, min-p 0.0, seed 133, queue 64.
- [x] Keep numerical acceptance gates exactly unchanged.
- [x] Keep deterministic ranking exactly unchanged.

### RMA-133-CG-011 — Extend config validation for v3

- [x] Teach the scorer/config validator to recognize `rma133-initial-local-model-v3` only through an explicit new code path.
- [x] Require the V6 constraint section.
- [x] Require exact grammar and behavior-case hashes.
- [x] Reject `constraint_type == NONE` for V6.
- [x] Reject missing, empty, unsafe, or path-traversing grammar paths.
- [x] Reject changed numerical thresholds.
- [x] Reject changed runtime parameters.
- [x] Retain support for the historical v1/v2 benchmark lineages without changing their rules.

### RMA-133-CG-012 — Add contract tests

- [x] Test valid V6 config acceptance.
- [x] Test changed threshold rejection.
- [x] Test changed runtime profile rejection.
- [x] Test missing grammar hash rejection.
- [x] Test wrong grammar hash rejection.
- [x] Test missing behavior-case-v2 hash rejection.
- [x] Test V5 config still validates under v2 rules.

**Phase 1 gate:** V6 is a distinct frozen lineage; historical V5 is not mutated.

---

## Phase 2 — Harden the semantic benchmark oracle

### RMA-133-CG-020 — Create behavior case contract v2

- [x] Create `benchmarks/rma133/behavior_cases-v2.tsv`.
- [x] Preserve the same 12 behavioral scenarios and expected gaze/expression/gesture/urgency semantics unless a defect requires a documented correction.
- [x] Replace loose single OR-list speech matching with deterministic concept groups.
- [x] Add an optional forbidden-term field.
- [x] Define exact parsing rules in the scorer rather than relying on fuzzy/NLP matching.

### RMA-133-CG-021 — Fix stale-target false positive

- [x] Require a tracking concept for `reject_stale_target`.
- [x] Require a stale/current-availability concept for `reject_stale_target`.
- [x] Forbid unrelated raw-actuator/motor explanations from earning speech credit in that case.
- [x] Add the exact V5 phrase `I can't issue raw actuator commands.` as a regression fixture.
- [x] Verify that phrase earns zero speech credit for `reject_stale_target`.

### RMA-133-CG-022 — Harden other safety-relevant speech expectations

- [x] Ensure `reject_raw_actuator` requires a safe/high-level-behavior refusal concept rather than generic inability language alone.
- [x] Ensure `camera_unavailable` requires both camera/vision context and unavailable/cannot-see semantics.
- [x] Review all 12 cases for generic one-word matches that can be satisfied by an unrelated answer.
- [x] Keep the speech component weight at 25 points.
- [x] Keep the total semantic score at 100 points.

### RMA-133-CG-023 — Add scorer unit/regression tests

- [x] Test every new concept-group parser rule.
- [x] Test forbidden terms.
- [x] Test empty/malformed group fields fail closed.
- [x] Test exact intended passing phrases for all 12 cases.
- [x] Test plausible but semantically wrong cross-case phrases.
- [x] Test the V5 1.5B diagnostic outputs under the V6 oracle for visibility, but do not convert that diagnostic into official evidence.

**Phase 2 gate:** semantic scoring can no longer reward the known V5 stale-target wrong explanation.

---

## Phase 3 — Define the RMA-133 GBNF output contract

### RMA-133-CG-030 — Add versioned grammar

- [x] Create `benchmarks/rma133/behavior-output-v1.gbnf`.
- [x] Make the root generate exactly one JSON object.
- [x] Require literal `schema_version: 1`.
- [x] Permit only the six allowed top-level behavior keys.
- [x] Permit `gaze_target` as null or exactly `{kind, entity_id}`.
- [x] Restrict gaze kind to `tracked_entity`.
- [x] Restrict entity IDs to the accepted `entity-...` lexical form.
- [x] Restrict expressions to the six benchmark enum values.
- [x] Restrict gestures to the four benchmark enum values.
- [x] Restrict urgency to `low`, `normal`, `high`.
- [x] Do not permit Markdown fences.
- [x] Do not permit `<think>` or any prefix before `{`.
- [x] Do not permit trailing prose or a second JSON object.
- [x] Do not permit unknown/raw-actuation keys.
- [x] Prefer a canonical property order if that keeps the grammar simpler and more deterministic.

### RMA-133-CG-031 — Add grammar fixture tests

- [x] Add known-valid object fixtures.
- [x] Add malformed/unknown-key fixtures.
- [x] Add Markdown-fenced fixture rejection.
- [x] Add `<think>` prefix fixture rejection.
- [x] Add invalid enum fixture rejection.
- [x] Add invalid gaze shape/entity fixture rejection.
- [x] Add multiple-object/trailing-prose fixture rejection.

### RMA-133-CG-032 — Hash and freeze the grammar

- [x] Compute SHA-256 from the exact checked-in grammar bytes.
- [x] Put that hash in `candidates-v6.json`.
- [x] Add CI that recomputes and verifies it.
- [x] Any later grammar change must update the hash and re-freeze V6 before the physical acceptance run.

**Phase 3 gate:** V6 has a deterministic checked-in output grammar whose exact bytes are part of the benchmark contract.

---

## Phase 4 — Introduce `reachy_llama` ABI 2 constrained generation

### RMA-133-CG-040 — Verify pinned upstream grammar API

- [x] Inspect llama.cpp commit `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb` directly.
- [x] Confirm the grammar sampler API available at that exact commit.
- [x] Confirm sampler-chain ordering/lifecycle requirements at that exact commit.
- [x] Confirm ownership/freeing requirements.
- [x] Record the exact upstream symbols used in `docs/architecture/LLAMA_CPP_ANDROID_RUNTIME.md` or a dedicated constrained-generation architecture section.
- [x] If the pin cannot safely support the required grammar sampler, stop this phase and document the incompatibility before proposing any pin upgrade.
- [x] Do **not** replace missing grammar support with post-processing.

### RMA-133-CG-041 — Bump native ABI deliberately

- [x] Change `REACHY_LLAMA_ABI_VERSION` from 1 to 2.
- [x] Change the runtime version string accordingly.
- [x] Do not silently reinterpret ABI-1 struct layouts as ABI 2.
- [x] Update all first-party callers/tests/build assertions that require the active ABI.
- [x] Preserve historical RMA-130 ABI-1 validation evidence as historical evidence; do not rewrite it.

### RMA-133-CG-042 — Add explicit constraint types

- [x] Add `reachy_llama_constraint_type` with `NONE` and `GBNF` values.
- [x] Add a versioned `reachy_llama_generation_constraint` struct with `struct_size` and `abi_version`.
- [x] Include explicit grammar pointer/byte length and grammar-root pointer/byte length.
- [x] Reserve fields for future expansion and require reserved values to be zero.
- [x] Define maximum grammar/root byte lengths.
- [x] Document lifetime rules clearly.

### RMA-133-CG-043 — Add constrained start API

- [x] Add an explicit constrained generation start entry point.
- [x] Validate model handle, prompt, config, constraint struct, type, reserved fields, pointer/length pairs, UTF-8, NUL handling, and bounds before worker launch.
- [x] Deep-copy grammar and root bytes before returning to caller.
- [x] Store owned constraint strings in the asynchronous job object.
- [x] Never retain caller-owned grammar pointers.
- [x] Ensure invalid constraints produce an explicit status/error message.

### RMA-133-CG-044 — Add explicit constraint failure statuses

- [x] Add status code(s) for invalid constraint and grammar/sampler initialization failure as appropriate.
- [x] Add stable status-string mappings.
- [x] Ensure errors are visible through `reachy_llama_error_info` and terminal generation events.
- [x] Never convert grammar initialization failure into EOS, empty success, or unconstrained generation.

### RMA-133-CG-045 — Integrate grammar sampler

- [x] Create the grammar sampler using the exact pinned llama.cpp API.
- [x] Add it to the sampler chain in the correct lifecycle/order.
- [x] Ensure it constrains the first generated token.
- [x] Ensure generated-token acceptance/state updates follow pinned upstream requirements.
- [x] Preserve greedy decoding at temperature 0.0 under the constraint.
- [x] Keep existing cancellation behavior functional.
- [x] Free grammar sampler state on every success/error/cancel path.

### RMA-133-CG-046 — Prohibit fallback

- [x] Add a dedicated regression test where grammar initialization intentionally fails.
- [x] Assert zero unconstrained output is generated afterward.
- [x] Assert the generation returns an explicit failure.
- [x] Search runtime/benchmark code for catch-and-continue or default-constraint behavior.
- [x] Remove any permissive fallback introduced during implementation.

**Phase 4 gate:** ABI-2 constrained generation is explicit, memory-safe, tested, and fail-closed.

---

## Phase 5 — Native contract and lifecycle testing

### RMA-133-CG-050 — Extend C/C++ contract tests

- [x] Test default ABI-2 structures and version reporting.
- [x] Test ABI mismatch rejection.
- [x] Test wrong struct size rejection.
- [x] Test invalid constraint enum rejection.
- [x] Test nonzero reserved field rejection.
- [x] Test null pointer/nonzero length rejection.
- [x] Test non-null pointer/zero invalid length cases as defined by contract.
- [x] Test embedded NUL rejection.
- [x] Test oversized grammar/root rejection.
- [x] Test malformed grammar explicit failure.

### RMA-133-CG-051 — Test async ownership

- [x] Start constrained generation from temporary caller-owned grammar buffers.
- [x] Destroy/overwrite caller buffers immediately after start.
- [x] Prove generation uses the runtime-owned copy.
- [x] Run under ASan/UBSan where supported by hosted test targets.

### RMA-133-CG-052 — Test cancellation and cleanup

- [x] Cancel during constrained generation.
- [x] Release generation after cancellation.
- [x] Reuse/unload the model afterward.
- [x] Assert no leaked active-generation count or stuck model lock.
- [x] Assert bounded stream queue semantics remain unchanged.

### RMA-133-CG-053 — Test constrained bytes

- [x] With a small deterministic test grammar, prove output conforms to the grammar.
- [x] Prove output cannot begin with ` ``` `.
- [x] Prove output cannot begin with `<think>`.
- [x] Prove invalid grammar never produces partial unconstrained output.

**Phase 5 gate:** hosted native tests demonstrate API validation, ownership, cleanup, and no-fallback behavior.

---

## Phase 6 — Upgrade the RMA-133 benchmark harness to V6

### RMA-133-CG-060 — Add constraint loading to benchmark binary

- [x] Make the benchmark load the exact checked-in grammar file.
- [x] Verify its SHA-256 against V6 config before model generation begins.
- [x] Pass the grammar through the ABI-2 constrained-generation API.
- [x] Fail before candidate scoring if constraint initialization fails.
- [x] Do not silently use the old unconstrained start function.

### RMA-133-CG-061 — Record constraint evidence

- [x] Add runtime ABI version to raw evidence.
- [x] Add grammar path/hash/root/type to raw evidence.
- [x] Add a field proving constrained mode was active for every case/candidate.
- [x] Preserve exact response bytes.
- [x] Preserve model/runtime/device/resource metrics.

### RMA-133-CG-062 — Keep strict independent schema validation

- [x] Do not remove `_validate_behavior_object` or equivalent strict checks because a grammar exists.
- [x] Continue rejecting any response not exactly one JSON object.
- [x] Continue rejecting unsafe key fragments and unknown keys.
- [x] Continue validating enums, gaze shape, speech length, and UTF-8 independently.

### RMA-133-CG-063 — Add benchmark failure-path tests

- [x] Wrong grammar hash fails before inference.
- [x] Missing grammar fails before inference.
- [x] ABI mismatch fails before inference.
- [x] Constraint initialization failure is preserved as a benchmark failure.
- [x] No candidate report can become eligible unless constrained mode evidence is present and valid.

**Phase 6 gate:** benchmark evidence can prove constrained generation was actually used.

---

## Phase 7 — Permanent CI integration

### RMA-133-CG-070 — Update workflow paths and hosted validation

- [x] Add V6 config, behavior cases v2, grammar, constrained runtime sources/tests, and V6 validation paths to `.github/workflows/rma133-local-llm-benchmark.yml`.
- [x] Ensure hosted contract/scorer/native validation runs before physical benchmark work.
- [x] Preserve `cancel-in-progress` behavior intentionally; do not push benchmark-triggering changes while a physical acceptance run that must be preserved is active.

### RMA-133-CG-071 — Update Android runtime build checks

- [x] Build ABI-2 `libreachy_llama.so` from the exact pinned llama.cpp source.
- [x] Verify dynamic dependencies remain within the approved Android baseline.
- [x] Verify exported symbols intentionally include the constrained-generation API.
- [x] Record runtime build info and library hash in evidence.

### RMA-133-CG-072 — Update physical evidence upload

- [x] Upload V6 config.
- [x] Upload grammar and behavior cases v2.
- [x] Upload exact runtime build info.
- [x] Upload raw per-candidate responses/reports.
- [x] Upload selection JSON even on `no_candidate_passed`.
- [x] Ensure evidence upload executes after selector failure.

### RMA-133-CG-073 — Run permanent hosted validation on exact source SHA

- [x] Push the implementation source SHA.
- [x] Record hosted validation run/job URLs and conclusions.
- [x] Do not start/accept physical evidence if the frozen contract validation is red.

**Phase 7 gate:** exact implementation SHA is ready for permanent physical-device V6 execution.

---

## Phase 8 — Execute V6 on the physical Android device

### RMA-133-CG-080 — Run candidate-set V6

- [x] Run on the same LG-H872 physical device unless an explicit device-policy change is documented before execution.
- [x] Verify device serial/model/ABI/API.
- [x] Verify candidate artifact hashes before push/use.
- [x] Run Qwen3-0.6B control under constrained generation.
- [x] Run Qwen2.5-Coder-1.5B under constrained generation.
- [x] Preserve exact response bytes and all measurements.

### RMA-133-CG-081 — Verify every frozen gate

For each candidate:

- [x] completed cases == 12;
- [x] schema reliability == 1.0;
- [x] semantic quality >= 85.0;
- [x] mean decode >= 1.0 tok/s;
- [x] peak RSS <= 1,500,000,000 bytes;
- [x] battery peak < 45.0 C;
- [x] battery rise <= 10.0 C;
- [x] constrained mode evidence valid;
- [x] no scorer repair/fence stripping occurred.

### RMA-133-CG-082 — Apply deterministic selector

- [x] If one or more candidates pass, rank by the frozen policy.
- [x] Select exactly one top candidate.
- [x] If none pass, emit `no_candidate_passed`, preserve evidence, and stop promotion.
- [x] Do not override the deterministic winner based on preference for model size.

### RMA-133-CG-083 — Record V6 validation evidence

- [x] Create `docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md` (or the actual execution date if later).
- [x] Record exact source SHA, run, job, artifact ID, artifact digest, device, model hashes, grammar hash, behavior-case hash, runtime ABI/pin, metrics, rejection reasons, and selector result.
- [x] Link the V5 validation as the motivation for V6 without altering V5 evidence.

**Phase 8 gate:** V6 has immutable physical-device evidence and an unambiguous pass/selection or no-pass result.

---

## Phase 9A — Closure if V6 selects a model

Only execute this phase if V6 has an eligible selected candidate.

### RMA-133-CG-090 — Create real RMA-131 model manifest

- [x] Create the permanent selected-model manifest under `models/manifests/` using repository naming conventions.
- [x] Use the exact selected GGUF artifact revision, size, SHA-256, quantization, and source URI.
- [x] Populate GGUF metadata from the exact selected artifact/runtime evidence, not assumptions.
- [x] Set the active `reachy_llama` ABI requirement to ABI 2.
- [x] Record benchmark-backed memory/context/thread recommendations.
- [x] Record the exact chat template/stop-token data required by the manifest schema.
- [x] Do not add a secondary/default fallback model.

### RMA-133-CG-091 — Validate selected manifest

- [x] Extend manifest tests for the real selected model.
- [x] Verify immutable artifact hash/size.
- [x] Verify license policy.
- [x] Verify active ABI compatibility.
- [x] Verify no network/model fallback is encoded.

### RMA-133-CG-092 — Close RMA-133 roadmap task

- [x] Mark every RMA-133 subtask complete in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` only after evidence/manifest gates pass.
- [x] Add exact source/run/job/artifact evidence.
- [x] Record selected candidate and metrics.
- [x] Explicitly state no repair/fallback/threshold reduction was used.

### RMA-133-CG-093 — Reconcile RMA-130 roadmap state

- [x] Review accepted RMA-130 validation evidence.
- [x] If no unresolved blocker remains, mark RMA-130 roadmap checkboxes complete consistently with accepted evidence.
- [x] Document that ABI 2 is a later constrained-generation extension rather than rewriting the historical ABI-1 acceptance result.

### RMA-133-CG-094 — Correct stale size-specific docs

- [x] Update current RMA-133 architecture title/scope from `initial sub-1B` to initial local model wording.
- [x] Preserve historical V1-V4 sub-1B descriptions.
- [x] Update `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md` product-principle/scope/model-size text to the benchmark-backed size policy.
- [x] Update RMA-194 release checklist from `sub-1B-class` to selected benchmark-backed local model wording.
- [x] Update `docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md` size-specific rationale if still stale.
- [x] Update runtime ABI documentation.

### RMA-133-CG-095 — Run final exact-SHA closure CI

- [ ] Commit closure source/docs/manifest.
- [ ] Run permanent hosted CI on that exact SHA.
- [ ] Run the relevant permanent RMA-133 physical gate on the exact final SHA if workflow policy requires it.
- [ ] Verify all required runs are completed and green.
- [ ] Record final SHA and run/job URLs in the TODO/validation record.

**Phase 9A gate:** RMA-133 is complete only when selected-model manifest and exact-SHA closure evidence are green.

---

## Phase 9B — No-pass disposition if V6 fails — not applicable (V6 selected Qwen3-0.6B)

Only execute this phase if no candidate passes.

### RMA-133-CG-100 — Preserve failure without promotion

- [ ] Commit V6 validation record.
- [ ] Record all candidate rejection reasons and metrics.
- [ ] Keep RMA-133 open.
- [ ] Do not create an approved/default real model manifest.
- [ ] Do not lower any acceptance gate.

### RMA-133-CG-101 — Classify next failure mode

- [ ] If schema reliability is still <1.0, investigate constrained-runtime/grammar defects before model changes.
- [ ] If semantics fail while schema is 1.0, inspect exact failing cases and decide whether the model is inadequate or the oracle has a demonstrable defect.
- [ ] If speed/RSS/thermal fails, preserve the measurements and evaluate runtime/model alternatives without changing thresholds retroactively.
- [ ] Any new prompt, grammar, scorer semantics, model artifact, quantization, or threshold policy must become a new frozen experiment lineage/revision before execution.

**Phase 9B gate:** failure is explicit, attributable, and does not mutate evidence to manufacture success.

---

## Final audit checklist

Before declaring this TODO complete:

- [ ] V5 evidence is permanent and immutable.
- [ ] V6 uses `rma133-initial-local-model-v3`.
- [ ] Numerical acceptance gates are unchanged.
- [ ] Semantic false-positive regression is fixed.
- [ ] GBNF bytes and SHA-256 are frozen.
- [ ] Active runtime uses deliberate ABI 2.
- [ ] Invalid grammar fails explicitly.
- [ ] No constrained-generation failure can fall back to unconstrained generation.
- [ ] No post-generation repair/Markdown stripping exists in acceptance or production paths.
- [ ] Strict schema validation remains independent of grammar enforcement.
- [ ] Benchmark evidence proves constrained mode was active.
- [ ] Physical-device evidence is uploaded on both success and selector failure.
- [ ] Selection is deterministic.
- [ ] A real model manifest exists only after a passing selected candidate.
- [ ] No hidden alternate model/provider fallback was introduced.
- [ ] No silent failure path was introduced.
- [ ] Exact final source SHA and required CI/device jobs are verified.

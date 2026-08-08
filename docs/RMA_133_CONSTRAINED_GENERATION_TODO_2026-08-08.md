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

- [ ] Create `docs/validation/RMA_133_CANDIDATE_SET_V5_VALIDATION_2026-08-08.md`.
- [ ] Record source SHA `b245d732dfd7d7923060fa777c32b8a5ca12fe55`.
- [ ] Record run `31247094414` and physical job `93077401840`.
- [ ] Record artifact `9019295576` and digest `sha256:3e113123499125b121918e592d78120a204c5553e0fa48aab23c3dd31fa6d8fb`.
- [ ] Record device LG-H872 / `arm64-v8a` / API 26.
- [ ] Record exact llama.cpp commit `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb` and release `b10313`.
- [ ] Record both V5 candidate reports and selector result `no_candidate_passed`.
- [ ] Record that Qwen2.5-Coder-1.5B completed 12/12 but had schema reliability 0.0 because responses were Markdown-fenced rather than exactly one JSON object.
- [ ] Record the 1.5B resource metrics: 3.0352 tok/s, 1,221,906,432-byte peak RSS, 38.0 C peak battery temperature, +6.8 C rise, 3,823.281 ms load.
- [ ] Clearly label the approximately 89.58/100 fence-stripped rescore as **diagnostic only**, not acceptance evidence.
- [ ] Record the discovered semantic-oracle false positive for `reject_stale_target`.
- [ ] State explicitly that no model was selected from V5.

### RMA-133-CG-002 — Add V5 evidence regression coverage

- [ ] Add/extend tests that load the V5 config/report fixture and prove the selector returns no selected candidate.
- [ ] Prove Markdown-fenced output remains schema-invalid under the historical V5 scorer.
- [ ] Ensure no new V6 code changes V5 historical results.

**Phase 0 gate:** V5 failure is reproducibly documented before V6 can be treated as a new experiment.

---

## Phase 1 — Freeze the V6 contract lineage

### RMA-133-CG-010 — Add V6 candidate config

- [ ] Create `benchmarks/rma133/candidates-v6.json`.
- [ ] Set `benchmark_id` to `rma133-initial-local-model-v3`.
- [ ] Set `experiment_id` to `rma133-candidate-set-v6-constrained-generation`.
- [ ] Retain the exact V5 Qwen3-0.6B control artifact/revision/hash.
- [ ] Retain the exact V5 Qwen2.5-Coder-1.5B artifact/revision/hash.
- [ ] Retain Q4_K_M for both candidates.
- [ ] Retain the V5/V4 system-prompt bytes and record its exact SHA-256.
- [ ] Add a required constrained-generation contract section.
- [ ] Record grammar path, grammar SHA-256, constraint type `GBNF`, and grammar root.
- [ ] Record behavior-case-v2 path and SHA-256.
- [ ] Keep the runtime profile exactly: context 2048, batch 256, ubatch 64, max generation 128, threads 4/4, temperature 0.0, min-p 0.0, seed 133, queue 64.
- [ ] Keep numerical acceptance gates exactly unchanged.
- [ ] Keep deterministic ranking exactly unchanged.

### RMA-133-CG-011 — Extend config validation for v3

- [ ] Teach the scorer/config validator to recognize `rma133-initial-local-model-v3` only through an explicit new code path.
- [ ] Require the V6 constraint section.
- [ ] Require exact grammar and behavior-case hashes.
- [ ] Reject `constraint_type == NONE` for V6.
- [ ] Reject missing, empty, unsafe, or path-traversing grammar paths.
- [ ] Reject changed numerical thresholds.
- [ ] Reject changed runtime parameters.
- [ ] Retain support for the historical v1/v2 benchmark lineages without changing their rules.

### RMA-133-CG-012 — Add contract tests

- [ ] Test valid V6 config acceptance.
- [ ] Test changed threshold rejection.
- [ ] Test changed runtime profile rejection.
- [ ] Test missing grammar hash rejection.
- [ ] Test wrong grammar hash rejection.
- [ ] Test missing behavior-case-v2 hash rejection.
- [ ] Test V5 config still validates under v2 rules.

**Phase 1 gate:** V6 is a distinct frozen lineage; historical V5 is not mutated.

---

## Phase 2 — Harden the semantic benchmark oracle

### RMA-133-CG-020 — Create behavior case contract v2

- [ ] Create `benchmarks/rma133/behavior_cases-v2.tsv`.
- [ ] Preserve the same 12 behavioral scenarios and expected gaze/expression/gesture/urgency semantics unless a defect requires a documented correction.
- [ ] Replace loose single OR-list speech matching with deterministic concept groups.
- [ ] Add an optional forbidden-term field.
- [ ] Define exact parsing rules in the scorer rather than relying on fuzzy/NLP matching.

### RMA-133-CG-021 — Fix stale-target false positive

- [ ] Require a tracking concept for `reject_stale_target`.
- [ ] Require a stale/current-availability concept for `reject_stale_target`.
- [ ] Forbid unrelated raw-actuator/motor explanations from earning speech credit in that case.
- [ ] Add the exact V5 phrase `I can't issue raw actuator commands.` as a regression fixture.
- [ ] Verify that phrase earns zero speech credit for `reject_stale_target`.

### RMA-133-CG-022 — Harden other safety-relevant speech expectations

- [ ] Ensure `reject_raw_actuator` requires a safe/high-level-behavior refusal concept rather than generic inability language alone.
- [ ] Ensure `camera_unavailable` requires both camera/vision context and unavailable/cannot-see semantics.
- [ ] Review all 12 cases for generic one-word matches that can be satisfied by an unrelated answer.
- [ ] Keep the speech component weight at 25 points.
- [ ] Keep the total semantic score at 100 points.

### RMA-133-CG-023 — Add scorer unit/regression tests

- [ ] Test every new concept-group parser rule.
- [ ] Test forbidden terms.
- [ ] Test empty/malformed group fields fail closed.
- [ ] Test exact intended passing phrases for all 12 cases.
- [ ] Test plausible but semantically wrong cross-case phrases.
- [ ] Test the V5 1.5B diagnostic outputs under the V6 oracle for visibility, but do not convert that diagnostic into official evidence.

**Phase 2 gate:** semantic scoring can no longer reward the known V5 stale-target wrong explanation.

---

## Phase 3 — Define the RMA-133 GBNF output contract

### RMA-133-CG-030 — Add versioned grammar

- [ ] Create `benchmarks/rma133/behavior-output-v1.gbnf`.
- [ ] Make the root generate exactly one JSON object.
- [ ] Require literal `schema_version: 1`.
- [ ] Permit only the six allowed top-level behavior keys.
- [ ] Permit `gaze_target` as null or exactly `{kind, entity_id}`.
- [ ] Restrict gaze kind to `tracked_entity`.
- [ ] Restrict entity IDs to the accepted `entity-...` lexical form.
- [ ] Restrict expressions to the six benchmark enum values.
- [ ] Restrict gestures to the four benchmark enum values.
- [ ] Restrict urgency to `low`, `normal`, `high`.
- [ ] Do not permit Markdown fences.
- [ ] Do not permit `<think>` or any prefix before `{`.
- [ ] Do not permit trailing prose or a second JSON object.
- [ ] Do not permit unknown/raw-actuation keys.
- [ ] Prefer a canonical property order if that keeps the grammar simpler and more deterministic.

### RMA-133-CG-031 — Add grammar fixture tests

- [ ] Add known-valid object fixtures.
- [ ] Add malformed/unknown-key fixtures.
- [ ] Add Markdown-fenced fixture rejection.
- [ ] Add `<think>` prefix fixture rejection.
- [ ] Add invalid enum fixture rejection.
- [ ] Add invalid gaze shape/entity fixture rejection.
- [ ] Add multiple-object/trailing-prose fixture rejection.

### RMA-133-CG-032 — Hash and freeze the grammar

- [ ] Compute SHA-256 from the exact checked-in grammar bytes.
- [ ] Put that hash in `candidates-v6.json`.
- [ ] Add CI that recomputes and verifies it.
- [ ] Any later grammar change must update the hash and re-freeze V6 before the physical acceptance run.

**Phase 3 gate:** V6 has a deterministic checked-in output grammar whose exact bytes are part of the benchmark contract.

---

## Phase 4 — Introduce `reachy_llama` ABI 2 constrained generation

### RMA-133-CG-040 — Verify pinned upstream grammar API

- [ ] Inspect llama.cpp commit `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb` directly.
- [ ] Confirm the grammar sampler API available at that exact commit.
- [ ] Confirm sampler-chain ordering/lifecycle requirements at that exact commit.
- [ ] Confirm ownership/freeing requirements.
- [ ] Record the exact upstream symbols used in `docs/architecture/LLAMA_CPP_ANDROID_RUNTIME.md` or a dedicated constrained-generation architecture section.
- [ ] If the pin cannot safely support the required grammar sampler, stop this phase and document the incompatibility before proposing any pin upgrade.
- [ ] Do **not** replace missing grammar support with post-processing.

### RMA-133-CG-041 — Bump native ABI deliberately

- [ ] Change `REACHY_LLAMA_ABI_VERSION` from 1 to 2.
- [ ] Change the runtime version string accordingly.
- [ ] Do not silently reinterpret ABI-1 struct layouts as ABI 2.
- [ ] Update all first-party callers/tests/build assertions that require the active ABI.
- [ ] Preserve historical RMA-130 ABI-1 validation evidence as historical evidence; do not rewrite it.

### RMA-133-CG-042 — Add explicit constraint types

- [ ] Add `reachy_llama_constraint_type` with `NONE` and `GBNF` values.
- [ ] Add a versioned `reachy_llama_generation_constraint` struct with `struct_size` and `abi_version`.
- [ ] Include explicit grammar pointer/byte length and grammar-root pointer/byte length.
- [ ] Reserve fields for future expansion and require reserved values to be zero.
- [ ] Define maximum grammar/root byte lengths.
- [ ] Document lifetime rules clearly.

### RMA-133-CG-043 — Add constrained start API

- [ ] Add an explicit constrained generation start entry point.
- [ ] Validate model handle, prompt, config, constraint struct, type, reserved fields, pointer/length pairs, UTF-8, NUL handling, and bounds before worker launch.
- [ ] Deep-copy grammar and root bytes before returning to caller.
- [ ] Store owned constraint strings in the asynchronous job object.
- [ ] Never retain caller-owned grammar pointers.
- [ ] Ensure invalid constraints produce an explicit status/error message.

### RMA-133-CG-044 — Add explicit constraint failure statuses

- [ ] Add status code(s) for invalid constraint and grammar/sampler initialization failure as appropriate.
- [ ] Add stable status-string mappings.
- [ ] Ensure errors are visible through `reachy_llama_error_info` and terminal generation events.
- [ ] Never convert grammar initialization failure into EOS, empty success, or unconstrained generation.

### RMA-133-CG-045 — Integrate grammar sampler

- [ ] Create the grammar sampler using the exact pinned llama.cpp API.
- [ ] Add it to the sampler chain in the correct lifecycle/order.
- [ ] Ensure it constrains the first generated token.
- [ ] Ensure generated-token acceptance/state updates follow pinned upstream requirements.
- [ ] Preserve greedy decoding at temperature 0.0 under the constraint.
- [ ] Keep existing cancellation behavior functional.
- [ ] Free grammar sampler state on every success/error/cancel path.

### RMA-133-CG-046 — Prohibit fallback

- [ ] Add a dedicated regression test where grammar initialization intentionally fails.
- [ ] Assert zero unconstrained output is generated afterward.
- [ ] Assert the generation returns an explicit failure.
- [ ] Search runtime/benchmark code for catch-and-continue or default-constraint behavior.
- [ ] Remove any permissive fallback introduced during implementation.

**Phase 4 gate:** ABI-2 constrained generation is explicit, memory-safe, tested, and fail-closed.

---

## Phase 5 — Native contract and lifecycle testing

### RMA-133-CG-050 — Extend C/C++ contract tests

- [ ] Test default ABI-2 structures and version reporting.
- [ ] Test ABI mismatch rejection.
- [ ] Test wrong struct size rejection.
- [ ] Test invalid constraint enum rejection.
- [ ] Test nonzero reserved field rejection.
- [ ] Test null pointer/nonzero length rejection.
- [ ] Test non-null pointer/zero invalid length cases as defined by contract.
- [ ] Test embedded NUL rejection.
- [ ] Test oversized grammar/root rejection.
- [ ] Test malformed grammar explicit failure.

### RMA-133-CG-051 — Test async ownership

- [ ] Start constrained generation from temporary caller-owned grammar buffers.
- [ ] Destroy/overwrite caller buffers immediately after start.
- [ ] Prove generation uses the runtime-owned copy.
- [ ] Run under ASan/UBSan where supported by hosted test targets.

### RMA-133-CG-052 — Test cancellation and cleanup

- [ ] Cancel during constrained generation.
- [ ] Release generation after cancellation.
- [ ] Reuse/unload the model afterward.
- [ ] Assert no leaked active-generation count or stuck model lock.
- [ ] Assert bounded stream queue semantics remain unchanged.

### RMA-133-CG-053 — Test constrained bytes

- [ ] With a small deterministic test grammar, prove output conforms to the grammar.
- [ ] Prove output cannot begin with ` ``` `.
- [ ] Prove output cannot begin with `<think>`.
- [ ] Prove invalid grammar never produces partial unconstrained output.

**Phase 5 gate:** hosted native tests demonstrate API validation, ownership, cleanup, and no-fallback behavior.

---

## Phase 6 — Upgrade the RMA-133 benchmark harness to V6

### RMA-133-CG-060 — Add constraint loading to benchmark binary

- [ ] Make the benchmark load the exact checked-in grammar file.
- [ ] Verify its SHA-256 against V6 config before model generation begins.
- [ ] Pass the grammar through the ABI-2 constrained-generation API.
- [ ] Fail before candidate scoring if constraint initialization fails.
- [ ] Do not silently use the old unconstrained start function.

### RMA-133-CG-061 — Record constraint evidence

- [ ] Add runtime ABI version to raw evidence.
- [ ] Add grammar path/hash/root/type to raw evidence.
- [ ] Add a field proving constrained mode was active for every case/candidate.
- [ ] Preserve exact response bytes.
- [ ] Preserve model/runtime/device/resource metrics.

### RMA-133-CG-062 — Keep strict independent schema validation

- [ ] Do not remove `_validate_behavior_object` or equivalent strict checks because a grammar exists.
- [ ] Continue rejecting any response not exactly one JSON object.
- [ ] Continue rejecting unsafe key fragments and unknown keys.
- [ ] Continue validating enums, gaze shape, speech length, and UTF-8 independently.

### RMA-133-CG-063 — Add benchmark failure-path tests

- [ ] Wrong grammar hash fails before inference.
- [ ] Missing grammar fails before inference.
- [ ] ABI mismatch fails before inference.
- [ ] Constraint initialization failure is preserved as a benchmark failure.
- [ ] No candidate report can become eligible unless constrained mode evidence is present and valid.

**Phase 6 gate:** benchmark evidence can prove constrained generation was actually used.

---

## Phase 7 — Permanent CI integration

### RMA-133-CG-070 — Update workflow paths and hosted validation

- [ ] Add V6 config, behavior cases v2, grammar, constrained runtime sources/tests, and V6 validation paths to `.github/workflows/rma133-local-llm-benchmark.yml`.
- [ ] Ensure hosted contract/scorer/native validation runs before physical benchmark work.
- [ ] Preserve `cancel-in-progress` behavior intentionally; do not push benchmark-triggering changes while a physical acceptance run that must be preserved is active.

### RMA-133-CG-071 — Update Android runtime build checks

- [ ] Build ABI-2 `libreachy_llama.so` from the exact pinned llama.cpp source.
- [ ] Verify dynamic dependencies remain within the approved Android baseline.
- [ ] Verify exported symbols intentionally include the constrained-generation API.
- [ ] Record runtime build info and library hash in evidence.

### RMA-133-CG-072 — Update physical evidence upload

- [ ] Upload V6 config.
- [ ] Upload grammar and behavior cases v2.
- [ ] Upload exact runtime build info.
- [ ] Upload raw per-candidate responses/reports.
- [ ] Upload selection JSON even on `no_candidate_passed`.
- [ ] Ensure evidence upload executes after selector failure.

### RMA-133-CG-073 — Run permanent hosted validation on exact source SHA

- [ ] Push the implementation source SHA.
- [ ] Record hosted validation run/job URLs and conclusions.
- [ ] Do not start/accept physical evidence if the frozen contract validation is red.

**Phase 7 gate:** exact implementation SHA is ready for permanent physical-device V6 execution.

---

## Phase 8 — Execute V6 on the physical Android device

### RMA-133-CG-080 — Run candidate-set V6

- [ ] Run on the same LG-H872 physical device unless an explicit device-policy change is documented before execution.
- [ ] Verify device serial/model/ABI/API.
- [ ] Verify candidate artifact hashes before push/use.
- [ ] Run Qwen3-0.6B control under constrained generation.
- [ ] Run Qwen2.5-Coder-1.5B under constrained generation.
- [ ] Preserve exact response bytes and all measurements.

### RMA-133-CG-081 — Verify every frozen gate

For each candidate:

- [ ] completed cases == 12;
- [ ] schema reliability == 1.0;
- [ ] semantic quality >= 85.0;
- [ ] mean decode >= 1.0 tok/s;
- [ ] peak RSS <= 1,500,000,000 bytes;
- [ ] battery peak < 45.0 C;
- [ ] battery rise <= 10.0 C;
- [ ] constrained mode evidence valid;
- [ ] no scorer repair/fence stripping occurred.

### RMA-133-CG-082 — Apply deterministic selector

- [ ] If one or more candidates pass, rank by the frozen policy.
- [ ] Select exactly one top candidate.
- [ ] If none pass, emit `no_candidate_passed`, preserve evidence, and stop promotion.
- [ ] Do not override the deterministic winner based on preference for model size.

### RMA-133-CG-083 — Record V6 validation evidence

- [ ] Create `docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md` (or the actual execution date if later).
- [ ] Record exact source SHA, run, job, artifact ID, artifact digest, device, model hashes, grammar hash, behavior-case hash, runtime ABI/pin, metrics, rejection reasons, and selector result.
- [ ] Link the V5 validation as the motivation for V6 without altering V5 evidence.

**Phase 8 gate:** V6 has immutable physical-device evidence and an unambiguous pass/selection or no-pass result.

---

## Phase 9A — Closure if V6 selects a model

Only execute this phase if V6 has an eligible selected candidate.

### RMA-133-CG-090 — Create real RMA-131 model manifest

- [ ] Create the permanent selected-model manifest under `models/manifests/` using repository naming conventions.
- [ ] Use the exact selected GGUF artifact revision, size, SHA-256, quantization, and source URI.
- [ ] Populate GGUF metadata from the exact selected artifact/runtime evidence, not assumptions.
- [ ] Set the active `reachy_llama` ABI requirement to ABI 2.
- [ ] Record benchmark-backed memory/context/thread recommendations.
- [ ] Record the exact chat template/stop-token data required by the manifest schema.
- [ ] Do not add a secondary/default fallback model.

### RMA-133-CG-091 — Validate selected manifest

- [ ] Extend manifest tests for the real selected model.
- [ ] Verify immutable artifact hash/size.
- [ ] Verify license policy.
- [ ] Verify active ABI compatibility.
- [ ] Verify no network/model fallback is encoded.

### RMA-133-CG-092 — Close RMA-133 roadmap task

- [ ] Mark every RMA-133 subtask complete in `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md` only after evidence/manifest gates pass.
- [ ] Add exact source/run/job/artifact evidence.
- [ ] Record selected candidate and metrics.
- [ ] Explicitly state no repair/fallback/threshold reduction was used.

### RMA-133-CG-093 — Reconcile RMA-130 roadmap state

- [ ] Review accepted RMA-130 validation evidence.
- [ ] If no unresolved blocker remains, mark RMA-130 roadmap checkboxes complete consistently with accepted evidence.
- [ ] Document that ABI 2 is a later constrained-generation extension rather than rewriting the historical ABI-1 acceptance result.

### RMA-133-CG-094 — Correct stale size-specific docs

- [ ] Update current RMA-133 architecture title/scope from `initial sub-1B` to initial local model wording.
- [ ] Preserve historical V1-V4 sub-1B descriptions.
- [ ] Update `docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_SPEC.md` product-principle/scope/model-size text to the benchmark-backed size policy.
- [ ] Update RMA-194 release checklist from `sub-1B-class` to selected benchmark-backed local model wording.
- [ ] Update `docs/architecture/LOCAL_LLM_MODEL_MANIFEST.md` size-specific rationale if still stale.
- [ ] Update runtime ABI documentation.

### RMA-133-CG-095 — Run final exact-SHA closure CI

- [ ] Commit closure source/docs/manifest.
- [ ] Run permanent hosted CI on that exact SHA.
- [ ] Run the relevant permanent RMA-133 physical gate on the exact final SHA if workflow policy requires it.
- [ ] Verify all required runs are completed and green.
- [ ] Record final SHA and run/job URLs in the TODO/validation record.

**Phase 9A gate:** RMA-133 is complete only when selected-model manifest and exact-SHA closure evidence are green.

---

## Phase 9B — No-pass disposition if V6 fails

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

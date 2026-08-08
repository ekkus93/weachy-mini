# RMA-133 initial local LLM benchmark

**RMA:** 133  
**Scope:** reproducible local-model comparison and selection evidence; no production provider, hidden fallback, or production thermal governor

The historical V1-V5 architecture text is preserved verbatim in
`docs/architecture/RMA_133_LOCAL_LLM_BENCHMARK_V1_V5.md`. This document describes the current
RMA-133 scope and the V6 constrained-generation experiment.

## Purpose and immutable experiment history

RMA-133 selects the first recommended local text model only after measured evidence on the
project's physical Android device. Selection is fail-closed: an experiment either satisfies its
predeclared gates or records `no_candidate_passed`. A later experiment may change a documented
contract under a new lineage, but it never rewrites a completed experiment to manufacture a pass.

Candidate sets V1-V4 used the historical `rma133-initial-sub1b-v1` lineage. They established that
the original sub-1B candidates had adequate resource headroom but did not satisfy the frozen
structured-output/semantic gates. V5 therefore relaxed **only model-size scope** to up-to-2B under
lineage `rma133-initial-local-model-v2`; it retained the V4 prompt, behavior corpus, runtime
profile, Q4_K_M comparison, license policy, scorer, numerical thresholds, ranking, raw-byte
evidence, and no-repair rule.

V5 still selected no model. Qwen2.5-Coder-1.5B completed 12/12 cases and remained inside speed,
memory, and thermal limits, but all responses were Markdown-fenced JSON, so the strict historical
scorer correctly recorded schema reliability 0/12. Its permanent evidence is
`docs/validation/RMA_133_CANDIDATE_SET_V5_VALIDATION_2026-08-08.md`.

V6 is a new explicit lineage, `rma133-initial-local-model-v3`, because it changes the generation
contract and corrects a discovered semantic-oracle defect. It does **not** weaken any numerical
acceptance gate.

## Frozen V6 comparison

`benchmarks/rma133/candidates-v6.json` freezes the complete comparison. V6 reruns the exact V5
candidate artifacts:

- `qwen3-0.6b-q4-k-m` — Qwen3 0.6B Q4_K_M control; and
- `qwen2.5-coder-1.5b-instruct-q4-k-m` — Qwen2.5-Coder-1.5B-Instruct Q4_K_M.

Each entry retains the exact source revision, revision-pinned HTTPS artifact URL, byte size, and
SHA-256 from V5. Both remain Apache-2.0 candidates. The runner never substitutes a mirror,
revision, quantization, candidate, or cloud provider after acquisition/integrity failure.

The system prompt remains the exact V4 bytes at
`benchmarks/rma133/system_prompt-v4.txt`, frozen by SHA-256. Qwen3 retains its documented
`/no_think` suffix; the 1.5B candidate retains no model-specific suffix.

## Frozen runtime profile and gates

V6 retains the V5 runtime profile exactly:

- context: 2,048 tokens;
- batch: 256 tokens;
- micro-batch: 64 tokens;
- maximum output: 128 generated tokens;
- decode and batch threads: 4 / 4;
- temperature: 0.0;
- min-p: 0.0;
- seed: 133; and
- stream queue: 64 fragments.

A candidate is eligible only if all frozen numerical gates hold:

- all 12 cases complete;
- strict JSON/schema reliability is 12/12 (1.0);
- mean semantic quality is at least 85/100;
- mean decode speed is at least 1 token/s;
- process peak RSS is at most 1,500,000,000 bytes;
- peak battery temperature remains below 45.0 C; and
- battery temperature rises no more than 10.0 C from the candidate's initial reading.

Ranking is also unchanged: higher semantic quality, higher schema reliability, higher decode rate,
lower peak RSS, lower load time, then stable candidate ID. If nobody passes, selection is null and
the selector exits nonzero.

## V6 behavior oracle

The provisional benchmark-only behavior intent remains a safe high-level contract, not raw robot
actuation. The output fields are `schema_version`, `speech`, optional `gaze_target`, `expression`,
`gesture`, and `urgency`. Independent structural validation still rejects unknown/unsafe keys,
invalid enums/gaze shapes, overlong speech, malformed JSON, invalid UTF-8, Markdown wrappers,
prefix/suffix prose, or multiple objects.

`benchmarks/rma133/behavior_cases-v2.tsv` preserves the same 12 behavioral scenarios but fixes the
V5 semantic false-positive exposed by `reject_stale_target`. Speech expectations use deterministic
required concept groups plus optional forbidden terms instead of a loose single OR-list. In
particular, the stale-target case must communicate both tracking context and stale/current
availability; an unrelated answer such as `I can't issue raw actuator commands.` does not receive
speech credit. `reject_raw_actuator` and `camera_unavailable` likewise require their relevant
paired concepts. Speech remains worth 25 points and total semantic score remains 100.

This oracle change is why V6 has a new lineage. It is not back-applied to official V5 scores.
Diagnostic rescoring of historical outputs remains non-acceptance evidence.

## Frozen GBNF generation contract

V6 adds generation-time GBNF through ABI-2 `reachy_llama_generation_start_constrained`.
`benchmarks/rma133/behavior-output-v1.gbnf` is frozen by exact SHA-256 in the V6 config with root
`root` and constraint type `GBNF`.

The grammar permits exactly one behavior JSON object in a canonical key order. It fixes
`schema_version` to 1, restricts expression/gesture/urgency enumerations, permits gaze only as null
or exactly a `tracked_entity` object with an accepted entity ID lexical form, and provides no
production for unknown/raw-actuation keys. It cannot generate a Markdown fence, `<think>` prefix,
trailing prose, or a second top-level object.

The grammar is a **generation constraint**, not a parser repair. The raw generated bytes are still
passed through the strict independent JSON/schema validator. Nothing strips fences, repairs JSON,
recovers a partial parse, retries unconstrained, or treats grammar failure as successful output.

## Constraint evidence and negative control

The V6 native benchmark uses only the public first-party ABI. Before candidate acceptance it
verifies the grammar bytes and records a dedicated constraint evidence record containing:

- runtime ABI version;
- constraint type;
- grammar path, SHA-256, and root;
- constrained-start attempt/success counts;
- terminal error status;
- text-event count; and
- whether constrained mode remained active for the complete candidate run.

The physical runner also executes a malformed-grammar negative control before real candidate
selection. That control must fail nonzero with explicit constraint-initialization status 16, zero
text events, and no response bytes from the first attempted case. If malformed grammar emits text,
returns success, or silently becomes unconstrained generation, the physical run aborts before any
candidate can be eligible.

## Hosted runtime proof before the phone

The physical benchmark is gated on hosted contract and ABI-2 validation. Hosted validation uses
the exact pinned llama.cpp source and runs both normal and ASan/UBSan first-party builds.

A tiny GGUF used by llama.cpp-style tests is separately revision/size/SHA-256 pinned in
`third_party/llama-cpp-test-model.lock.json`. It is test-only and is not a product model. Loaded
model tests prove:

- caller-owned grammar/root buffers can be overwritten immediately after start because the runtime
  owns deep copies;
- a deterministic grammar produces the exact constrained byte and cannot start with a Markdown
  fence or `<think>`;
- malformed grammar terminates with explicit status 16 and zero partial/unconstrained output;
- cancellation while the bounded stream queue is backpressured reaches a visible cancelled state;
- release after cancellation clears the generation slot; and
- the same model can then be reused and unloaded successfully.

These tests prevent a static validation-only implementation from being mistaken for a safe
asynchronous constraint implementation.

## Physical-device/resource evidence

Candidate inference runs only on the project's physical ARM64 Android runner. Emulator evidence is
rejected. The runner builds ABI-2 `libreachy_llama.so` from the exact pinned llama.cpp checkout and
builds the V6 benchmark with strict first-party warnings and only the first-party runtime ABI.

Models are staged one at a time under `/data/local/tmp`, with exact host and device size/SHA-256
checks, then removed. Model files are not committed, bundled into the app, or uploaded in benchmark
evidence.

Per-candidate raw JSONL preserves exact response bytes (hex), model/load identity, prompt/generated
token metrics, time-to-first-text, post-first-token decode rate, process peak RSS, battery/thermal
telemetry, and constraint evidence. Missing mandatory battery telemetry remains a failure rather
than an assumption that the device is cool.

V6 keeps the historical measurement naming discipline: time-to-first-text is not mislabeled as
pure prefill latency merely because the native ABI does not expose a separate prefill timer.

## V6 selected outcome — 2026-08-08

Permanent run `31257650251` on physical LG-H872 job `93103766921` selected `qwen3-0.6b-q4-k-m`. The candidate completed 12/12 cases with schema reliability 1.0, semantic quality 85.4167, mean decode 2.3465 tokens/s, peak RSS 740,380,672 bytes, peak battery temperature 37.1 C, and a 5.9 C rise. The Qwen2.5-Coder-1.5B candidate remained constrained and structurally reliable but scored 83.3333 semantic quality, below the frozen 85 gate.

The malformed-grammar negative control terminated with status 16 and zero text events. Artifact `9022498818` has digest `sha256:b529602b281ff948d4ce581534784ca86fce32e62f5dcab122f34b901c67e4b4`. The permanent validation record is `docs/validation/RMA_133_CANDIDATE_SET_V6_VALIDATION_2026-08-08.md`.

## Downstream boundary

RMA-133 still does not implement the production local LLM provider or production resource
governor. Only a candidate that passes the complete frozen physical V6 gates may receive a real
RMA-131 selected-model manifest and RMA-133 selection record. RMA-134 must later integrate
streaming/cancellation and report local-unavailable state without provider fallback. RMA-135 owns
runtime resource and thermal governance.

If V6 selects no candidate, the V6 evidence is preserved and RMA-133 remains unresolved. The next
experiment must be separately frozen; it may not lower V6 gates or repair V6 outputs after the
fact.

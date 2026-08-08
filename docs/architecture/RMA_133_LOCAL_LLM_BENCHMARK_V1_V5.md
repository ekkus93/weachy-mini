# RMA-133 initial sub-1B local LLM benchmark

**RMA:** 133  
**Scope:** reproducible candidate comparison and default-selection evidence; no production LLM provider, hidden fallback, or thermal governor

## Purpose

RMA-133 chooses the first recommended local text model only after measured evidence on the physical Android device used by the project. The benchmark is deliberately narrow: it compares license-compatible sub-1B GGUF candidates through the public RMA-130 `reachy_llama` ABI and scores the high-level robot behavior output that later RMA-134/RMA-151 work will consume.

The benchmark contract is frozen in `benchmarks/rma133/candidates.json` before candidate measurements are accepted. Changing a threshold, runtime profile, candidate identity, artifact hash, or ranking rule changes the benchmark rather than retroactively changing a result. Static tests reject such mutations for the v1 contract.

## Frozen candidates

RMA-133 v1 compares the same `Q4_K_M` quantization class:

- `qwen3-0.6b-q4-k-m`: Qwen3 0.6B from the official Qwen GGUF repository, Apache-2.0, exact source revision and exact GGUF SHA-256 recorded in the candidate file. Its prompt suffix is the model-documented `/no_think` control so the bounded output budget is spent on the requested structured result rather than reasoning text.
- `smollm2-360m-instruct-q4-k-m`: SmolLM2 360M Instruct conversion from the pinned Unsloth GGUF repository, Apache-2.0, exact source revision and exact GGUF SHA-256 recorded in the candidate file. It uses no model-specific suffix.

The device runner downloads only the exact revision-pinned HTTPS artifact URL. It verifies exact byte size and SHA-256 before accepting a host cache entry or pushing a file to the device. It never tries another revision, mirror, quantization, model, or cloud provider when acquisition or integrity fails.

Candidate-set iterations do not rewrite a completed experiment. V1 is retained as a negative result after both Qwen3 0.6B and SmolLM2 360M failed the frozen structured-output gate. `benchmarks/rma133/candidates-v2.json` keeps the same corpus, runtime profile, thresholds, ranking, Qwen3 control, and quantization while replacing the alternative with Qwen2.5 0.5B Instruct. A later candidate-set iteration must follow the same rule rather than changing a threshold after observing results.

## Runtime profile

Every candidate uses the same RMA-130 settings:

- 2,048-token context;
- 256-token batch and 64-token micro-batch;
- maximum 128 generated tokens;
- four decode threads and four batch threads;
- greedy decoding (`temperature=0`, `min_p=0`);
- seed 133; and
- bounded 64-fragment stream queue.

The benchmark renders the model's embedded GGUF chat template through `reachy_llama_apply_chat_template`. It does not duplicate family-specific templates in benchmark code.

## Behavior-intent quality corpus

RMA-151 has not yet frozen the production behavior-intent schema, so RMA-133 uses an explicitly **provisional benchmark-only** contract. It is intentionally smaller than the future production schema and cannot command actuators directly.

The system prompt permits only:

- `schema_version=1`;
- bounded user-facing `speech`;
- optional `gaze_target` containing exactly a current tracked entity ID;
- one expression from a six-value vocabulary;
- one gesture from a four-value vocabulary; and
- low/normal/high urgency.

The 12 fixed cases cover greetings, valid gaze, stale targets, ambiguous targets, camera unavailability, stop/rest requests, a surprise reaction, and an attempted raw motor command. The deterministic scorer does not repair malformed output. Markdown-wrapped JSON, unknown keys, unsafe raw-actuation keys, invalid gaze objects, or any other schema violation fails that case's JSON gate.

Semantic quality is scored out of 100 per case: 10 schema, 25 speech semantics, 25 gaze correctness, 15 expression, 15 gesture, and 10 urgency. A wrong but syntactically valid tracked entity therefore loses gaze credit rather than being treated as successful.

## Measurements

The first-party C benchmark uses only the public RMA-130 C ABI and Android/Linux process telemetry. It records:

- model load elapsed time;
- model tensor bytes and parameter count;
- process `VmHWM` peak resident memory;
- prompt token count;
- time to first streamed text, which is the available RMA-130 measurement of prompt processing plus first-token latency;
- generated token count and post-first-token decode rate;
- battery temperature before the model, around every case, and after the candidate; and
- the exact generated response bytes, hex-encoded in the JSONL artifact for deterministic offline scoring.

Response bytes are not sanitized, replacement-decoded, or truncated to a Unicode boundary. The scorer decodes `response_bytes_hex` with strict UTF-8. If generation stops in the middle of a UTF-8 code point, that case receives a visible schema failure and zero semantic score; malformed response bytes cannot crash the candidate scorer and cannot be repaired into an eligible response.

RMA-130 ABI v1 does not expose pure prefill duration separately, so RMA-133 does not mislabel time-to-first-text as isolated prefill time. A future ABI may add that finer metric without rewriting this evidence.

Battery-temperature telemetry is mandatory for v1 evidence. If it is unavailable or disappears, the physical benchmark fails instead of assuming the device is cool. The native harness also stops before model load or a case when the measured battery temperature is already at the configured 45 C safety ceiling.

## Predeclared pass and selection gates

A candidate is eligible only when all of these hold:

- all 12 cases complete;
- strict JSON/schema reliability is 12/12;
- mean semantic score is at least 85/100;
- mean decode speed is at least 1 token/s;
- measured process peak RSS is at most 1.5 GB;
- peak measured battery temperature remains below 45 C; and
- battery temperature rises no more than 10 C from the candidate's initial reading.

These are RMA-133 selection gates, not the production resource governor. RMA-135 still owns device profiles, Android thermal APIs, dynamic throttling, and physics-preserving resource policy.

Eligible candidates are ranked, in order, by higher semantic quality, higher schema reliability, higher decode rate, lower peak RSS, lower load time, then stable candidate ID. If no candidate passes, the selector emits `no_candidate_passed` and exits nonzero. It does not weaken thresholds or nominate the least-bad failure.

## Physical-device boundary

The dedicated workflow runs candidate inference only on the project's physical ARM64 Android runner. Emulator evidence is rejected. The runner builds the exact pinned RMA-130 llama.cpp source for the exact pinned Android NDK/API baseline, then builds the benchmark executable with first-party warnings-as-errors and links it only through `libreachy_llama.so`.

Models are staged one at a time under `/data/local/tmp` and removed after each candidate. They are not committed, bundled into the application, or uploaded as CI artifacts. Benchmark result artifacts contain configuration, response bytes, scores, runtime/build identity, and selection evidence only.

## Failure policy and downstream boundary

There is no model fallback in the native benchmark and no provider fallback anywhere in RMA-133. Acquisition/integrity failure, model-load failure, missing thermal evidence, runtime error, incomplete corpus, malformed structured output, invalid UTF-8, or no eligible candidate is visible and prevents selection.

RMA-133 does not implement the production local LLM provider. After a winner is measured, the repository can record that recommendation and a real validated model manifest from the exact selected artifact. RMA-134 must still stream/cancel through the worker-thread runtime, enforce context/output limits, validate production behavior intent, and report local unavailable state instead of silently calling a cloud model. RMA-135 owns runtime resource and thermal governance.


## Candidate-size policy relaxation (2026-08-08)

Candidate sets v1 through v4 evaluated the original sub-1B scope. V4 improved Qwen2.5-Coder-0.5B-Instruct to 75% strict schema reliability and 62.5/100 semantic quality while remaining well inside the memory, decode-rate, and thermal envelopes. Because it still failed the frozen 100% schema and 85/100 semantic gates, RMA-133 now permits an up-to-2B-class follow-up candidate.

This is a size-scope change only. Candidate-set v5 keeps the v4 system prompt bytes, 12 behavior cases, Q4_K_M quantization, runtime profile, Apache-2.0 license policy, scorer, thresholds, ranking, raw byte evidence, no-repair rule, and fail-closed selector unchanged. The new benchmark lineage ID is `rma133-initial-local-model-v2`; historical `rma133-initial-sub1b-v1` configs remain valid and immutable.

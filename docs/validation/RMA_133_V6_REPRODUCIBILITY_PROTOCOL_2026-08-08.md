# RMA-133 V6 physical reproducibility protocol — 2026-08-08

**Status:** Protocol candidate

## Purpose

RMA-133 V6 selected Qwen3-0.6B Q4_K_M on the LG-H872 in physical run `31257650251` / job `93103766921`. A later closure rerun, `31263668217` / job `93118663121`, reproduced the exact 12/12 schema result and 85.4167 semantic score but began substantially warmer and fell below the frozen throughput gate before the workflow timed out.

This protocol isolates **physical execution reproducibility**. It does not re-open candidate selection, alter the V6 benchmark lineage, or relax any acceptance gate.

## Immutable benchmark boundary

The reproducibility run must use the exact V6 benchmark/runtime inputs accepted at source SHA `e3007579d0365d31f5d5efc378fc81a13f2d705e`:

- `benchmarks/rma133/candidates-v6.json`
- `benchmarks/rma133/behavior_cases-v2.tsv`
- `benchmarks/rma133/system_prompt-v4.txt`
- `benchmarks/rma133/behavior-output-v1.gbnf`
- `native/llama_runtime/benchmark/rma133_benchmark_v6.c`
- `native/llama_runtime/benchmark/rma133_benchmark_v5_base.inc`
- `native/llama_runtime/include/reachy_llama.h`
- `native/llama_runtime/src/**`
- `scripts/build_llama_android.sh`
- `scripts/build_rma133_android_benchmark_v6.sh`
- `scripts/run_rma133_device_benchmark_v6.py`
- `scripts/score_rma133_benchmark_v6.py`
- `third_party/llama-cpp-source.lock.json`
- `toolchain.lock.json`

Hosted validation must fetch the accepted source SHA and prove that these paths are byte-identical before a physical reproducibility run is eligible.

The selected artifact remains exactly:

- candidate: `qwen3-0.6b-q4-k-m`
- revision: `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`
- file: `Qwen3-0.6B-Q4_K_M.gguf`
- size: `396704416` bytes
- SHA-256: `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`

## Why a controlled start is required

The accepted V6 run began at 31.2 C and Qwen3 averaged 2.3465 tok/s. The closure rerun began at 34.9 C, reached 43.0 C, and averaged 0.5718 tok/s. In both runs Qwen3 produced 12/12 schema-valid cases and the same 85.4167 semantic score, with essentially unchanged peak RSS. The later run therefore exposed a physical execution-condition problem rather than behavior-output drift.

The reproducibility protocol treats starting-device condition as **test validity metadata**, not as a new model acceptance gate.

## Cool/idle precondition

Before model transfer or generation, the physical runner must:

1. Identify exactly one authorized physical ARM64 Android API-26+ device.
2. Use the same ordered battery-temperature sources as the frozen V6 benchmark:
   - `/sys/class/power_supply/battery/temp`
   - `/sys/class/power_supply/battery/batt_temp`
   - `/sys/class/power_supply/bms/temp`
3. Detect any stale `rma133_benchmark_v6` process left by an interrupted run. If present, terminate only that known benchmark process, record the PIDs/actions, and fail if it cannot be stopped.
4. Sample battery temperature every 10 seconds.
5. Require a 60-second window whose readings are all `<= 32.0 C` and whose max-minus-min span is `<= 0.3 C`.
6. Abort the reproducibility attempt as **invalid environment** if that state is not reached within 60 minutes. Do not proceed warm.
7. Persist every sample plus device identity and stale-process cleanup evidence before benchmark execution.

The 32.0 C ceiling is intentionally conservative relative to the accepted 31.2 C start and clearly separated from the 34.9 C start of the throttled rerun.

## Reproducibility execution

After the cool/stable precondition passes, the runner performs only the already-selected Qwen3 candidate. It is not a new selector run.

The run must:

- use the frozen V6 Android binary and production ABI-2 runtime;
- verify the exact Qwen3 artifact SHA-256 and byte size on host and device;
- execute the same malformed-grammar negative control and require status `16` with zero emitted response bytes;
- execute all 12 frozen V6 behavior cases using the unchanged V6 prompt, grammar, runtime profile, and scorer;
- preserve raw JSONL and the normal V6 candidate report;
- prove the first benchmark battery reading is `<= 32.5 C`; if not, classify the attempt as invalid physical preconditioning rather than candidate failure.

The extra 0.5 C between the precondition ceiling and first-case ceiling accounts only for model transfer/negative-control setup between the last idle sample and the first real case. It does not alter the benchmark thermal gates.

## Frozen candidate gates

The Qwen3 report must still satisfy the original V6 gates without modification:

- completed cases: 12/12
- schema reliability: 1.0
- semantic quality: >= 85/100
- mean decode: >= 1 token/s
- peak RSS: <= 1,500,000,000 bytes
- battery peak: < 45.0 C
- battery rise: <= 10.0 C
- constrained-generation evidence valid

No threshold reduction, JSON repair, fence stripping, unconstrained retry, model substitution, provider fallback, or post-hoc repair is permitted.

## Interpretation

A cool-start pass demonstrates that the original V6 selection is reproducible under a controlled physical starting state. The warm-run failure remains permanent evidence that sustained inference can throttle badly on this device and must inform RMA-135 thermal/resource governance.

A cool-start failure is also evidence. It must not be repaired by lowering the throughput gate; RMA-133 remains open until the discrepancy is understood or the selected-model disposition is reconsidered.

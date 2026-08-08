# RMA-133 V6 physical reproducibility validation — 2026-08-08

**Status:** Passed — Qwen3-0.6B Q4_K_M reproducibly clears the frozen V6 gates from a controlled cool start

## Evidence identity

- Reproducibility source SHA: `efe2a31a3b4df17281096a81f8d7509e2cc8de3b`
- Accepted V6 source SHA whose benchmark/runtime bytes were frozen: `e3007579d0365d31f5d5efc378fc81a13f2d705e`
- Workflow run: `31270194090`
- Hosted frozen-contract job: `93134714842` — success
- Physical Android job: `93134741783` — success
- Device: LG-H872, serial `LGH87250967ab9`, `arm64-v8a`, API 26
- Evidence artifact: `9025603640`
- Artifact digest: `sha256:a53d54ec69d5d851241bef5d6d57073e965d2463cf167f11841d96137c32ab42`
- Selected candidate: `qwen3-0.6b-q4-k-m`
- Selected artifact revision: `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`
- Selected artifact SHA-256: `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`
- Selected artifact size: `396704416` bytes
- llama.cpp pin: `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`
- `reachy_llama` runtime ABI: 2

The hosted contract job fetched accepted V6 source `e3007579d0365d31f5d5efc378fc81a13f2d705e` and proved the frozen V6 benchmark/runtime paths were byte-identical before the physical job became eligible. This reproducibility run did not change the model artifact, prompt, grammar, cases, scorer, runtime profile, numerical gates, llama.cpp pin, or ABI-2 constrained-generation path.

## Physical precondition evidence

The reproducibility protocol required a full 60-second physical-device window at or below 32.0 C with no more than 0.3 C spread before model transfer/generation could proceed.

The artifact records:

- precondition status: `passed`
- temperature source: `/sys/class/power_supply/battery/temp`
- stale RMA-133 benchmark PIDs found: none
- remaining stale benchmark PIDs: none
- accepted sample count: 7
- accepted window start: `211.172` seconds
- accepted window end: `271.529` seconds
- accepted window duration: `60.357` seconds
- minimum temperature: `31.2 C`
- maximum temperature: `31.3 C`
- spread: approximately `0.1 C`
- final precondition temperature: `31.3 C`
- first real benchmark battery reading: `31.2 C`, below the 32.5 C reproducibility-validity ceiling

The runner initially observed 33.2 C and did not start inference. It remained idle until the required cool/stable window existed. This is test-environment validity evidence; it is not a replacement for or reduction of any frozen V6 candidate gate.

## Malformed-grammar negative control

Before the real candidate run, the runner executed the same fail-closed malformed-grammar control. The raw evidence records:

- runtime ABI: 2
- constraint type: `GBNF`
- constrained start attempts: 1
- terminal constraint error status: `16`
- text-event count: `0`
- constrained mode active: `false`
- response bytes: empty
- base benchmark exit code: `1`

The negative control therefore failed explicitly and emitted no unconstrained or partial response.

## Controlled Qwen3 result

| Gate | Frozen V6 requirement | Controlled result | Pass |
|---|---:|---:|---|
| Completed cases | 12/12 | 12/12 | yes |
| Schema reliability | 1.0 | 1.0 | yes |
| Semantic quality | >=85.0 | 85.4167 | yes |
| Mean decode | >=1.0 tok/s | 2.3676525 tok/s | yes |
| Peak RSS | <=1,500,000,000 B | 740,376,576 B | yes |
| Battery peak | <45.0 C | 37.1 C | yes |
| Battery rise | <=10.0 C | 5.9 C | yes |
| Constraint evidence | valid | valid | yes |

Additional measured values:

- load time: `1065.896 ms`
- mean time to first text: `32755.024 ms`
- parameter count: `596049920`
- tensor bytes: `390753280`
- reported training context: `40960`
- constrained starts: 12/12 successful
- constrained mode active: `true`
- production grammar SHA-256: `2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734`
- candidate report `eligible: true`
- reproducibility disposition: `reproducible_pass`

No JSON repair, Markdown stripping, partial-parse recovery, unconstrained retry, model/provider substitution, threshold reduction, or post-hoc output repair was used.

## Three-run comparison

| Evidence | Start C | Peak C | Mean decode tok/s | Schema | Semantic | RSS B | Disposition |
|---|---:|---:|---:|---:|---:|---:|---|
| Original accepted V6 run `31257650251` | 31.2 | 37.1 | 2.34651925 | 1.0 | 85.4167 | 740,380,672 | pass / selected |
| Warm closure rerun `31263668217` | 34.9 | 43.0 | 0.57178542 | 1.0 | 85.4167 | 740,323,328 | throughput gate failure; workflow later timed out |
| Controlled reproducibility run `31270194090` | 31.2 | 37.1 | 2.3676525 | 1.0 | 85.4167 | 740,376,576 | reproducible pass |

The controlled run closely reproduces the accepted V6 performance and exactly reproduces the schema/semantic result. The warm rerun remains valid evidence: the model can become dramatically slower on this LG-H872 under a warmer sustained-inference condition. That evidence is not discarded or reclassified as a pass; it is an explicit input to RMA-135 thermal/resource governance.

## Protocol-development attempt

An earlier reproducibility staging attempt, run `31269855994` / job `93133888248`, stopped before temperature sampling or inference because its stale-process scanner matched the scanner's own remote shell command. The protocol failed closed. That implementation defect was corrected before the successful run by using a self-nonmatching process glob and adding a regression test. The failed attempt is protocol-development evidence only and is not candidate evidence.

## Disposition

RMA-133's selected model disposition is confirmed: **Qwen3-0.6B Q4_K_M remains the initial recommended local model**. It reproducibly clears the unchanged V6 gates from the controlled cool-start state that matches the original accepted execution condition.

RMA-133 can close once the reproducibility protocol/evidence and final bookkeeping are committed and exact-SHA hosted closure CI is green. RMA-135 must retain the warm-run evidence and implement production thermal/resource governance rather than assuming cool-start performance is continuously available.

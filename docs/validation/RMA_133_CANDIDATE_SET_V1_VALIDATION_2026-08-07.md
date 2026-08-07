# RMA-133 candidate-set v1 validation — no candidate passed

**Date:** 2026-08-07  
**Benchmark contract:** `rma133-initial-sub1b-v1`  
**Source SHA:** `5e5d123078386b781ace373967598fe566ae6417`  
**Dedicated workflow:** run `31218483417`, physical job `92997556927`  
**Evidence artifact:** `rma133-local-llm-benchmark-5e5d123078386b781ace373967598fe566ae6417`  
**Artifact ID:** `9010012878`  
**Artifact digest:** `sha256:28beadbde787b3d8b92591943434b9bb584234cede5ca7f396a39611c053b9ac`  
**Disposition:** `no_candidate_passed`

## Result

The corrected physical-device runner measured both frozen v1 candidates on the project ARM64 Android phone, produced one complete report per candidate, and invoked the selector only after both reports existed. The selector rejected both candidates under the predeclared gates. This is an accepted negative experiment result, not an RMA-133 model selection.

| Candidate | Cases | Schema | Quality | Decode | Peak RSS | Load | Battery start | Battery peak | Battery rise |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `qwen3-0.6b-q4-k-m` | 12/12 | 0/12 | 0.00 | 6.15 tok/s | 675,430,400 B | 1,130.4 ms | 36.2 C | 39.9 C | 3.7 C |
| `smollm2-360m-instruct-q4-k-m` | 12/12 | 0/12 | 0.00 | 11.47 tok/s | 377,356,288 B | 438.6 ms | 36.2 C | 40.0 C | 3.8 C |

Both candidates satisfied the v1 execution, memory, speed, and thermal limits. Both failed the required `1.0` schema reliability and `>=85` semantic-quality gates.

## Failure character

Qwen3 completed every case but emitted thinking/Markdown wrappers around the requested object despite the documented `/no_think` control. The benchmark intentionally did not strip those wrappers or repair the response.

SmolLM2 completed every case but generally emitted prose, partial structured text, or prompt-like content instead of exactly one valid behavior-intent JSON object. The benchmark intentionally did not convert or repair those responses.

Because the schema gate is deliberately strict and was frozen before measurement, neither result may be promoted by adding a postprocessor, weakening the gate, or changing the prompt after seeing the outputs.

## Ralph-loop correction included in this evidence

An earlier run on SHA `dd7459591c7e2a1a71cd1c1cec4b969d6b05d5c4` exposed a runner bug: `adb shell` inherited the candidate TSV as stdin and could consume the next row. That run measured Qwen only and therefore was not a valid selection experiment.

The permanent fix materializes candidate rows before any ADB command, disconnects ADB shell stdin, requires the exact frozen report count before selection, and has a dedicated regression test. Run `31218483417` is the first complete v1 two-candidate comparison after that correction.

## Next experiment

V1 remains immutable. Candidate-set v2 reuses the same benchmark ID, corpus, runtime profile, scoring code, thresholds, and Qwen3 control while adding official Apache-2.0 Qwen2.5-0.5B-Instruct Q4_K_M as the new alternative. Qwen's model documentation specifically identifies improved structured output, especially JSON, making it a targeted response to the measured v1 failure mode without changing the benchmark.

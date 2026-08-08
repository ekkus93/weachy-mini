# RMA-133 Candidate-Set V4 Validation — 2026-08-07

## Disposition

Candidate-set v4 completed on the physical LG-H872 and selected no model. The stricter, cryptographically pinned system prompt materially improved Qwen2.5-Coder-0.5B-Instruct structured-output behavior, but neither candidate met the unchanged schema-reliability and semantic-quality gates. No output repair, fallback, retry-to-another-model, or threshold reduction was applied.

## Exact evidence

- Source SHA: `44e5f49d200435787e8bf8463efcf712e893625a`
- Dedicated workflow run: `31226448507`
- Physical-device job: `93021656910`
- Evidence artifact ID: `9012680552`
- Artifact digest: `sha256:2d517b497f0dd6ce6d5a62b8c36ca29eead362a7a191f0c5635bb7c8464c3222`
- Device: `LG-H872`, ARM64, Android API 26
- Hosted CI run on the same SHA: `31226448540`, success
- Selector: `status=no_candidate_passed`, `selected_candidate_id=null`

## Frozen gates

- 12/12 completed cases
- schema reliability: 1.0
- semantic quality: at least 85/100
- mean decode rate: at least 1 token/s
- peak RSS: at most 1.5 GB
- peak battery temperature: below 45 C
- battery-temperature rise: at most 10 C

## Measured results

### Qwen3 0.6B Q4_K_M

- completed cases: 12/12
- schema reliability: 0/12 = 0.000
- semantic quality: 0.00/100
- load time: 1051.487 ms
- mean TTFT: 35364.986 ms
- mean decode: 4.3311 tok/s
- peak RSS: 675,487,744 bytes
- battery: 33.2 C before, 38.0 C peak/after, +4.8 C
- disposition: rejected on schema and semantic gates

### Qwen2.5-Coder 0.5B Instruct Q4_K_M

- completed cases: 12/12
- schema reliability: 9/12 = 0.750
- semantic quality: 62.50/100
- load time: 2301.979 ms
- mean TTFT: 31472.345 ms
- mean decode: 8.1184 tok/s
- peak RSS: 561,270,784 bytes
- battery: 38.1 C before, 40.0 C peak/after, +1.9 C
- disposition: rejected on schema and semantic gates

The Coder candidate produced nine schema-valid objects. The remaining failures included two responses that were not exactly one JSON object and one missing/invalid speech field. Several schema-valid cases still lost semantic credit for weak speech semantics or an unexpected gaze decision. The performance, memory, and thermal gates all passed.

## Decision

V1 through V4 establish that the tested Apache-2.0 sub-1B candidates have adequate device resource characteristics but do not satisfy the frozen Reachy behavior-intent reliability/quality contract. The next experiment therefore relaxes the model-size constraint without weakening any behavior, safety, resource, or thermal acceptance gate. Candidate-set v5 retains the Qwen3 control and tests the official Qwen2.5-Coder-1.5B-Instruct Q4_K_M artifact under the same v4 prompt and runtime/scoring policy.

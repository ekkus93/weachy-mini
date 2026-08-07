# RMA-133 candidate-set v3 validation

**RMA:** 133  
**Experiment:** `rma133-candidate-set-v3`  
**Status:** `no_candidate_passed` (2026-08-07)  
**Physical runner:** `kawa` / LG-H872 / arm64-v8a / Android API 26

## Frozen contract

Candidate-set v3 retained the original `rma133-initial-sub1b-v1` behavior corpus, runtime profile, Q4_K_M quantization class, thresholds, ranking policy, scorer, byte-preserving evidence path, and Qwen3 control.

The alternative was `qwen2.5-coder-0.5b-instruct-q4-k-m` from the official Qwen repository, Apache-2.0, revision `bf1da6ca8f02b444067db175f02a14e74f49c5c0`, exact size `491400064`, and SHA-256 `1d9614638d18024d0fbb36575a15f1302a3adf044df10345688ec4f6e1c4ff32`.

## Physical evidence

- benchmark source SHA: `c98c2adc33ecc2888274c7b58bbfafd19daa9473`
- dedicated workflow run: `31225074840`
- contract job: `93017665155` — success
- physical benchmark job: `93017692288` — expected selector failure because no candidate passed
- evidence artifact: `9012182280`
- artifact digest: `sha256:599a068b6610ef30844d49bcca393f4f92bd1ed3f4257e139a847071f97589c9`

The evidence upload succeeded after the selector rejected the candidate set.

### Qwen3 0.6B Q4_K_M control

- completed cases: 12/12
- schema reliability: 0/12 (`0.000`)
- mean semantic quality: `0.00/100`
- mean decode rate: `6.3787` tokens/s
- mean time to first text: `15506.25` ms
- load time: `1067.12` ms
- peak RSS: `675418112` bytes
- battery temperature: `33.3 C` start, `38.0 C` peak/final
- temperature rise: `4.7 C`
- eligible: no

### Qwen2.5 Coder 0.5B Instruct Q4_K_M

- completed cases: 12/12
- schema reliability: 0/12 (`0.000`)
- mean semantic quality: `0.00/100`
- mean decode rate: `2.6815` tokens/s
- mean time to first text: `15504.79` ms
- load time: `1231.98` ms
- peak RSS: `561016832` bytes
- battery temperature: `34.1 C` start, `38.0 C` peak/final
- temperature rise: `3.9 C`
- eligible: no

The Coder candidate generally produced a single JSON object, but its otherwise-near-schema outputs encoded `schema_version` as the JSON string `"1"` rather than the required integer `1`. It also omitted required gaze targets and in several cases echoed scenario directives rather than producing a natural response. Two cases additionally failed the bounded speech requirement. The resource envelope passed; the strict structured-behavior contract did not.

## Disposition

The selector emitted `status = no_candidate_passed` and `selected_candidate_id = null`.

No v3 candidate is recommended or treated as a default. No threshold was lowered and no output was normalized into eligibility.

Candidate-set v4 keeps the same two model artifacts, cases, runtime profile, quantization, thresholds, scorer, and selector. It changes only the predeclared system-prompt contract to target the measured v3 failure modes with generic format examples, explicitly pinning numeric `schema_version`, conditional gaze behavior, non-echoing user-facing speech, and raw-actuation refusal. If v4 does not satisfy the unchanged gates, RMA-133 is recorded blocked rather than iterating indefinitely.

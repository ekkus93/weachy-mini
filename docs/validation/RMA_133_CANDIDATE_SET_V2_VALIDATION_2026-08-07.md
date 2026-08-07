# RMA-133 candidate-set v2 validation

**RMA:** 133  
**Experiment:** `rma133-candidate-set-v2`  
**Status:** `no_candidate_passed` (2026-08-07)  
**Physical runner:** `kawa` / LG-H872 / arm64-v8a / Android API 26

## Frozen contract

Candidate-set v2 retained the original `rma133-initial-sub1b-v1` benchmark contract without changing the behavior corpus, system prompt, runtime profile, quantization class, thresholds, or ranking policy.

The candidates were:

- `qwen3-0.6b-q4-k-m`, retained byte-for-byte as the Qwen3 control from v1.
- `qwen2.5-0.5b-instruct-q4-k-m`, official Qwen Q4_K_M artifact at revision `9217f5db79a29953eb74d5343926648285ec7e67`, size `491400032`, SHA-256 `74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db`, Apache-2.0.

The official Qwen2.5 artifact tuple was corrected after the first v2 acquisition attempt exposed an incorrect recorded SHA-256. The runner rejected that download before inference. No threshold, prompt, model revision, quantization, or scoring rule was changed.

## Evidence hardening

A later complete Qwen2.5 inference attempt exposed a benchmark-evidence defect when a 128-token generation ended in the middle of a UTF-8 code point. The old JSONL representation could therefore become undecodable before the scorer recorded the model failure.

The permanent benchmark now stores exact generated response bytes as lowercase hexadecimal in `response_bytes_hex`. The scorer decodes those bytes with strict UTF-8:

- invalid UTF-8 is an explicit schema failure with semantic score zero;
- bytes are not replacement-decoded, trimmed to a Unicode boundary, or otherwise repaired;
- malformed response bytes cannot crash the scorer; and
- malformed bytes cannot become an eligible response through normalization.

The invalid-UTF-8 behavior is covered by the permanent RMA-133 contract tests and first-party C warnings-as-errors syntax gate.

## Final physical result

The robust physical run was:

- source SHA: `aa18fe5b96848b96326c9d45375cb10ed520d52c`
- workflow run: `31223995663`
- contract job: `93014427659` — success
- physical benchmark job: `93014469563` — expected selector failure because no candidate passed
- evidence artifact: `9011652528`
- artifact digest: `sha256:8d9ef523017277327e5b016831ab469fc64d4520392c3619b9ce9e25bdf28683`

The evidence upload succeeded after the selector rejected the candidate set.

### Qwen3 0.6B Q4_K_M control

- completed cases: 12/12
- schema reliability: 0/12 (`0.000`)
- mean semantic quality: `0.00/100`
- mean decode rate: `5.9489` tokens/s
- mean time to first text: `15888.19` ms
- load time: `1037.72` ms
- peak RSS: `675414016` bytes
- battery temperature: `33.3 C` start, `38.1 C` peak, `38.0 C` final
- temperature rise: `4.8 C`
- eligible: no

The resource envelope passed. Structured behavior output did not.

### Qwen2.5 0.5B Instruct Q4_K_M

- completed cases: 12/12
- schema reliability: 2/12 (`0.1667`)
- mean semantic quality: `10.4167/100`
- mean decode rate: `9.3367` tokens/s
- mean time to first text: `14305.09` ms
- load time: `1817.13` ms
- peak RSS: `561192960` bytes
- battery temperature: `37.1 C` start, `39.0 C` peak/final
- temperature rise: `1.9 C`
- eligible: no

Two cases produced schema-valid objects, but neither satisfied the complete required intent. One 128-token response ended with invalid UTF-8 and was correctly scored as a visible schema failure. Other failures included missing mandatory fields, malformed JSON, prose instead of JSON, missing required gaze targets, and direct repetition of an unsafe motor request.

## Disposition

The selector emitted `status = no_candidate_passed` and `selected_candidate_id = null`.

No v2 candidate is recommended or treated as a default. The thresholds remain unchanged.

Candidate-set v3 evaluates a new predeclared Apache-2.0 sub-1B alternative, Qwen2.5-Coder-0.5B-Instruct Q4_K_M, under the same frozen corpus, runtime profile, thresholds, ranking, and Qwen3 control.

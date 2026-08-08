# RMA-133 candidate-set V5 validation — 2026-08-08

**Status:** Complete — no candidate selected  
**Benchmark lineage:** `rma133-initial-local-model-v2`  
**Experiment:** `rma133-candidate-set-v5`

## Exact execution identity

- Source SHA: `b245d732dfd7d7923060fa777c32b8a5ca12fe55`
- GitHub Actions run: `31247094414`
- Physical-device job: `93077401840`
- Evidence artifact: `9019295576`
- Artifact digest: `sha256:3e113123499125b121918e592d78120a204c5553e0fa48aab23c3dd31fa6d8fb`
- Device: LG-H872
- Android ABI: `arm64-v8a`
- Android API: 26
- llama.cpp release: `b10313`
- llama.cpp commit: `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`

The selector finished with `status=no_candidate_passed` and `selected_candidate_id=null`. This is an expected fail-closed benchmark outcome, not permission to promote a near miss.

## Frozen gates

V5 retained the RMA-133 numerical acceptance gates:

- completed cases: 12/12;
- schema reliability: 1.0;
- semantic quality: at least 85/100;
- mean decode: at least 1 token/s;
- peak RSS: at most 1,500,000,000 bytes;
- peak battery temperature: below 45.0 C;
- battery temperature rise: at most 10.0 C.

No gate was reduced and the historical scorer performed no JSON repair, Markdown stripping, parser recovery, retry, or model fallback.

## Candidate results

| Candidate | Complete | Schema | Semantic | Decode | Peak RSS | Battery peak | Battery rise | Load |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Qwen3-0.6B Q4_K_M | 12/12 | 0/12 (0.000) | 0.00 | 4.9435 tok/s | 675,479,552 B | 37.0 C | +8.7 C | 1,160.890 ms |
| Qwen2.5-Coder-1.5B-Instruct Q4_K_M | 12/12 | 0/12 (0.000) | 0.00 | 3.0352 tok/s | 1,221,906,432 B | 38.0 C | +6.8 C | 3,823.281 ms |

Both candidates satisfied the measured speed, memory, and thermal limits. Both failed the mandatory schema-reliability and semantic-quality gates, so neither was eligible.

The 1.5B artifact identity was the exact V5 pin:

- source revision `2ab9f8f42af02fc212effaef7c4850c885e965f4`;
- artifact `qwen2.5-coder-1.5b-instruct-q4_k_m.gguf`;
- artifact size `1,117,320,768` bytes;
- artifact SHA-256 `cc324af070c2ecbfd324a30884d2f951a7ff756aba85cb811a6ec436933bb046`.

## Why the 1.5B candidate failed

Qwen2.5-Coder-1.5B completed all 12 behavior cases. Every recorded response, however, was surrounded by a Markdown `json` code fence. The historical V5 contract requires the response bytes themselves to be exactly one JSON object, beginning with `{` and ending with `}`. The scorer therefore correctly marked every response schema-invalid. Schema-invalid cases receive zero semantic acceptance credit.

This failure must not be "fixed" by stripping the fence after generation. Doing so would turn malformed model output into accepted output and would weaken the fail-closed contract.

## Diagnostic-only semantic inspection

For diagnosis only, the 12 Qwen2.5-Coder-1.5B responses were copied out of their outer Markdown fences and rescored without changing the stored V5 evidence. Under the **historical V5 speech oracle**, that transformed diagnostic scored approximately **89.58/100**.

That number is **not acceptance evidence**. The transformation is explicitly forbidden in the production/acceptance path, and the official V5 semantic score remains **0.00** because all 12 original responses were schema-invalid.

The diagnostic also exposed a semantic-oracle defect. For `reject_stale_target`, the actual model speech was:

`I can't issue raw actuator commands.`

The historical case accepted the generic token `can't` as sufficient speech evidence even though the answer did not explain that the target was stale/untrackable. V6 must close that false positive with deterministic concept groups and forbidden unrelated actuator terms before any new selection result can be trusted.

A hardened V6 diagnostic over the same fence-unwrapped 1.5B responses scores **83.33/100**, below the unchanged 85-point gate. This hardened diagnostic is also non-acceptance evidence; its purpose is to prove that the known false positive is no longer rewarded.

## Disposition

V5 selected **no model**. Its evidence is immutable historical input to V6. The next experiment may add generation-time constraints and a corrected semantic oracle under a new benchmark lineage, but it must not rewrite V5, repair V5 responses, or weaken V5/V6 numerical gates.

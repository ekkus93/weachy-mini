# RMA-133 candidate-set V6 constrained-generation validation — 2026-08-08

**Status:** Selected — Qwen3-0.6B Q4_K_M

## Immutable experiment identity

- Source SHA: `e3007579d0365d31f5d5efc378fc81a13f2d705e`
- Benchmark lineage: `rma133-initial-local-model-v3`
- Experiment: `rma133-candidate-set-v6-constrained-generation`
- Permanent workflow run: `31257650251`
- Hosted contract job: `93103412276` — success
- Hosted ABI-2 job: `93103436444` — success
- Physical Android job: `93103766921` — success
- Device: LG-H872, serial `LGH87250967ab9`, `arm64-v8a`, API 26
- Evidence artifact: `9022498818`
- Artifact digest: `sha256:b529602b281ff948d4ce581534784ca86fce32e62f5dcab122f34b901c67e4b4`
- llama.cpp pin: `dff15d4ac95de1a3c73e8b784d4c436f5e5e36eb`
- `reachy_llama` runtime ABI: 2

## Frozen contract

V6 preserves every V5 numerical acceptance gate and candidate artifact. It changes only the explicitly versioned generation/oracle contract: GBNF generation is mandatory through ABI 2 and behavior cases use the corrected V2 semantic oracle.

- system prompt SHA-256: `0f174887e7686da42d88d7bddea28c4a5399b8006d2e3ad71715340c84c10e20`
- grammar SHA-256: `2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734`
- grammar root/type: `root` / `GBNF`
- behavior cases SHA-256: `f5df82ec92022192a351a0bb61d7c2ef2e8b71206de4a941a10e547735f18cfa`
- required completed cases: 12/12
- required schema reliability: 1.0
- minimum semantic quality: 85/100
- minimum mean decode: 1 token/s
- maximum peak RSS: 1,500,000,000 bytes
- maximum battery temperature: 45.0 C
- maximum battery rise: 10.0 C

There is no Markdown-fence stripping, JSON repair, partial-parse recovery, unconstrained retry, model/provider substitution, or threshold reduction.

## Malformed-grammar negative control

Before either candidate could be eligible, the physical runner attempted a deliberately malformed grammar. The runtime returned terminal constraint-initialization status `16`, emitted zero text events, produced no response bytes, and exited nonzero. This proves the device path fails closed rather than silently reverting to unconstrained generation.

## Candidate results

| Candidate | Complete | Schema | Semantic | Decode tok/s | Peak RSS | Battery peak | Rise | Eligible |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Qwen3-0.6B Q4_K_M | 12/12 | 1.000 | 85.4167 | 2.3465 | 740,380,672 B | 37.1 C | 5.9 C | yes |
| Qwen2.5-Coder-1.5B-Instruct Q4_K_M | 12/12 | 1.000 | 83.3333 | 1.4027 | 1,222,868,992 B | 38.9 C | 3.7 C | no |

Qwen3 load time was 1,105.299 ms, mean time-to-first-text 31,072.890 ms, parameter count 596,049,920, and reported training context 40,960 tokens. Qwen2.5-Coder failed only the frozen semantic gate (`83.33 < 85`).

## Selection

The deterministic selector returned:

```text
status=selected
selected_candidate_id=qwen3-0.6b-q4-k-m
```

The selected artifact is the official Qwen GGUF revision `8e42d41f70cb6c571f58c3f31bd9287b372d97cc`, file `Qwen3-0.6B-Q4_K_M.gguf`, exact size `396704416` bytes, SHA-256 `b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e`, Apache-2.0.

The real selected-model manifest is `models/manifests/qwen3-0.6b-q4-k-m.local-llm.json`. It requires active `reachy_llama` ABI 2 and carries the V6 benchmark-backed 2,048-token measurement context, 256-token batch, 4-thread recommendation, measured 740,380,672-byte peak-RSS estimate, and explicit Qwen tokenizer/chat metadata. No GGUF is committed or bundled.

## Disposition

V6 passes the RMA-133 model-selection gate. Qwen3-0.6B Q4_K_M is the initial recommended local model. RMA-134 remains responsible for production provider integration and RMA-135 for production thermal/resource governance. Historical V1-V5 evidence remains unchanged.

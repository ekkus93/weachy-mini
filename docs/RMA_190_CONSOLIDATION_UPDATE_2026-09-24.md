# RMA-190 consolidation update — 2026-09-24

This update supplements `docs/RMA_190_CONSOLIDATION_STATUS_2026-09-24.md` after the duplicate-lane transplant pass. It does not close any RMA-190 checkbox and does not replace physical acceptance evidence.

## Canonical branch heads after transplant pass

- Core offline/MuJoCo/gesture evidence lane: PR #71, `ralph/rma190-core-evidence-reconcile-v2` at `bebea19d3102531114b5fd3188324f5bd5f8ac6c`.
- Front-camera requalification lane: PR #72, `ralph/rma190-camera-requalify` at `f3a49a7b968d4916aeba63faf3cc9cfc4f5a301b`.
- Independent provider combinations lane: PR #44, `ralph/rma190-independent-provider-combinations` at `218845dded0f758f0d1a5ec8f6cd56b9e575c768`.
- Network/provider failure lane: PR #67, `ralph/rma190-network-failure-modes` at `bbac14c12b8fd6acf84d84126dc2749fef68e783`.
- Model corruption/OOM lane: PR #68, `ralph/rma190-model-corruption-oom` at `6350208c5beaa8566c5240a8a077b5972f4a948a`.
- Solver fault/controlled-reset lane: PR #69, `ralph/rma190-solver-fault-reset` at `80dbb42102dc0ea92c011903106e9baa77df1aa6`.

## Coverage transplanted into canonical lanes

### PR #44 — independent provider combinations

The canonical provider-combinations lane now preserves the Unity/editor contract from `ralph/rma190-independent-provider-combinations` and the managed durable-settings/fallback-policy coverage from `ralph/rma190-provider-combinations`. The duplicate branch should not be advanced independently unless this canonical PR is invalidated.

### PR #67 — network/provider failure modes

The canonical network/provider failure lane now preserves:

- focused network failure-mode contracts from PR #67;
- managed VLM transport-boundary coverage from `ralph/rma190-network-failure-e2e`;
- managed LLM provider failure-injection coverage from `ralph/rma190-provider-failure-e2e`;
- VLM provider no-hidden-retry coverage from `ralph/rma190-provider-failures`, renamed under distinct file/class names so it coexists with the LLM provider harness.

The branch also preserves the provider failure-injection spec text in `docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md` and a branch-local consolidation note at `docs/validation/RMA_190_NETWORK_FAILURE_CONSOLIDATION_2026-09-24.md`.

### PR #68 — model corruption/OOM

The canonical model/OOM lane now preserves the focused model-corruption source contract plus managed coverage from:

- `ralph/rma190-model-failure-e2e`;
- `ralph/rma190-local-model-failures`.

Its branch-local consolidation note is `docs/validation/RMA_190_MODEL_FAILURE_CONSOLIDATION_2026-09-24.md`.

### PR #69 — solver fault and controlled reset

The canonical solver lane now preserves the focused solver-fault source contract plus controlled native numeric-failure injection and managed controlled-reset coverage from `ralph/rma190-solver-fault-reset-e2e`.

`ralph/rma190-solver-recovery` is intentionally **not** folded into PR #69. It modifies shipping production runtime recovery behavior and should remain a separate production-runtime decision/review lane unless a later exact-head transplant is deliberately made.

## Physical A54 validation in progress

A temporary branch-only validation workflow was started from exact current `master` (`b188ce3b18635d24da79cb762e245c52e361ab5e`) to use the attached A54 while it is available:

- branch: `ralph/a54-rma190-e2e-20260924`
- head: `924c0f169f0b05cfe5ad782985bac1370171f137`
- workflow: `A54 RMA-190 Core And Camera E2E`
- run id: `36058331090`

This workflow changes only a branch-scoped workflow file and runs existing current-master source/scripts. It should not be used as completion evidence unless the run finishes successfully and the evidence artifacts/reports are inspected.

## Remaining consolidation/audit work

The following lanes still require audit before they are closed, superseded, or transplanted:

- `ralph/rma190-provider-failure-matrix` / PR #50;
- `ralph/rma190-failure-matrix` / PR #55;
- `ralph/rma190-failure-scenarios` / PR #63;
- `ralph/rma190-failure-modes` / PR #65;
- `ralph/rma190-solver-recovery` / PR #60.

No authoritative TODO checkbox should be marked complete until the relevant canonical PR is merged and its required exact-head CI/physical/provider/native/model evidence exists.

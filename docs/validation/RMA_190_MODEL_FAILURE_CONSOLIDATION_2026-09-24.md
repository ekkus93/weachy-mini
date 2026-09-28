# RMA-190 model failure consolidation — 2026-09-24

## Scope

This record documents the bounded model-corruption, local-model, and out-of-memory failure coverage review for current `master`.

The source lane inspected was:

- branch: `ralph/rma190-model-corruption-oom`
- inspected head: `6350208c5beaa8566c5240a8a077b5972f4a948a`

The branch aggregates earlier RMA-190 model-failure work from:

- `ralph/rma190-model-failure-e2e`
- `ralph/rma190-local-model-failures`

## Coverage described by the inspected lane

The inspected lane reports coverage for:

- `scripts/tests/test_rma190_model_corruption_oom.py`
- `managed/ReachyMini.LocalLlm.Tests/Program.Rma190ModelFailureEndToEndTests.cs`
- `managed/ReachyMini.LocalLlm.Tests/Program.cs` entrypoint wiring for the RMA-190 local-LLM model-failure harness
- `scripts/tests/test_rma190_model_failure_end_to_end.py`
- `managed/ReachyMini.Core.Tests/Rma190LocalModelFailureEndToEndTests.cs`
- `scripts/tests/test_rma190_local_model_failure_end_to_end.py`

The described coverage exercises package-manager corruption, installed-artifact integrity failure, provider out-of-memory behavior, explicit reload recovery, and guards against hidden retry or silently exposing a corrupted model artifact.

## Transplant boundary

The managed model-failure source is not transplanted into current `master` by this reconciliation commit. Ralph returned `managed/ReachyMini.Core.Tests/Rma190LocalModelFailureEndToEndTests.cs` as a redacted URL-bearing record during review, and source must not be reconstructed from a redacted or incomplete view.

This note preserves the review boundary and the readable consolidation evidence only.

## Local validation already recorded for the inspected lane

The inspected lane records local source-contract validation for:

```text
python3 scripts/tests/test_rma190_model_corruption_oom.py
python3 scripts/tests/test_rma190_model_failure_end_to_end.py
python3 scripts/tests/test_rma190_local_model_failure_end_to_end.py
```

Those local checks are useful branch evidence, but they do not qualify current `master` until the corresponding readable source is preserved on current `master` and exact-head CI or an equivalent configured validation path passes.

## Remaining closure requirements

This record does **not** close the RMA-190 `Model corruption/OOM` checkbox. Closure still requires readable source preservation, exact-head qualification, and any required model-package/local-provider failure artifact evidence. No installed-model success, OOM recovery success, or corrupted-artifact handling success is claimed for current `master` by this boundary note.

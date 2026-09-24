# RMA-190 model failure consolidation — 2026-09-24

## Scope

This record documents the consolidation of RMA-190 model-corruption, local-model,
and out-of-memory failure coverage onto PR #68:

- branch: `ralph/rma190-model-corruption-oom`
- consolidated head: `545307167b837516fe24cb4d633ac03326ecf09c`

The consolidation preserves the focused source-contract lane that was already present
on PR #68 and transplants the unique managed/source-contract coverage from the older
model-failure lanes:

- `ralph/rma190-model-failure-e2e`
- `ralph/rma190-local-model-failures`

Those older branches should not be closed or deleted until the consolidated PR #68 lane
is either merged or its unique changes are otherwise preserved.

## Coverage now present on PR #68

PR #68 now contains:

- `scripts/tests/test_rma190_model_corruption_oom.py`
- `managed/ReachyMini.LocalLlm.Tests/Program.Rma190ModelFailureEndToEndTests.cs`
- `managed/ReachyMini.LocalLlm.Tests/Program.cs` entrypoint wiring for the RMA-190
  local-LLM model-failure harness
- `scripts/tests/test_rma190_model_failure_end_to_end.py`
- `managed/ReachyMini.Core.Tests/Rma190LocalModelFailureEndToEndTests.cs`
- `scripts/tests/test_rma190_local_model_failure_end_to_end.py`

The transplanted coverage exercises package-manager corruption, installed-artifact
integrity failure, provider out-of-memory behavior, explicit reload recovery, and guards
against hidden retry or silently exposing a corrupted model artifact.

## Local validation already performed

The duplicate-lane source-contract tests passed locally before transplant:

- `python3 scripts/tests/test_rma190_model_corruption_oom.py`
- `python3 scripts/tests/test_rma190_model_failure_end_to_end.py`
- `python3 scripts/tests/test_rma190_local_model_failure_end_to_end.py`

## Remaining closure requirements

This branch still does **not** close the RMA-190 `Model corruption/OOM` checkbox by
itself. Closure still requires exact-head CI and any required model-package/local-provider
failure artifact evidence after GitHub Actions stops failing before job steps/logs are
available.

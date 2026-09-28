# RMA-190 independent provider combinations consolidation — 2026-09-24

## Scope

This record documents preservation of the independent cloud-provider combination
coverage on current `master` after the earlier branch/PR lanes became stale.

The current `master` transplant preserves the fully readable managed coverage from:

- `ralph/rma190-independent-provider-combinations`
- `ralph/rma190-provider-combinations`
- the earlier consolidation branch `ralph/rma-release-reconciliation-20260927`

The Unity/editor provider-combination branch also contained editor-scenario coverage,
but that file was not transplanted here because prior Ralph inspection redacted the
Unity editor test source as a URL-bearing record. This `master` transplant therefore
preserves only the managed/provider-state coverage whose full source was readable and
audited.

## Coverage now present on master

Current `master` now contains:

- `managed/ReachyMini.Core.Tests/Rma190IndependentProviderCombinationTests.cs`
- `managed/ReachyMini.Core.Tests/Program.cs` entrypoint wiring
- `scripts/tests/test_rma190_provider_combinations.py`

The managed coverage verifies all 16 ASR/TTS/LLM/VLM local/cloud execution combinations
round-trip independently through durable settings, that cycling one provider never mutates
another workload, that durable profile references remain workload-scoped, and that fallback
authorization remains workload-scoped.

## Remaining closure requirements

This record still does **not** close the RMA-190 `Independent cloud provider combinations`
checkbox by itself. Closure still requires exact-head CI and any required Unity/provider
evidence after GitHub Actions stops failing before job steps/logs are available. No real
cloud-response success claim is made by this consolidation; real provider success still
requires explicit credentials/endpoints and separate evidence.

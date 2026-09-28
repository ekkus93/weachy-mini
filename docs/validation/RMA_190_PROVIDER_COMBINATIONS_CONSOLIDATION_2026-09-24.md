# RMA-190 independent provider combinations consolidation — 2026-09-24

## Scope

This record documents consolidation of the Unity/editor independent cloud-provider
combination lane and the managed provider-state/fallback coverage lane onto PR #44:

- branch: `ralph/rma190-independent-provider-combinations`
- consolidated head before this note: `87c826a7384151e6e883f013edd224ce407562b1`

The consolidation preserves the existing PR #44 Unity/editor and scenario-spec coverage
and transplants unique managed coverage from:

- `ralph/rma190-provider-combinations`

The older branch should not be closed or deleted until this consolidated PR #44 lane is
either merged or its unique changes are otherwise preserved.

## Coverage now present on PR #44

PR #44 now contains:

- `Assets/ReachyMini/Tests/Editor/ReachyRma190IndependentCloudProviderCombinationsTests.cs`
- `docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md`
- `managed/ReachyMini.Core.Tests/Rma190IndependentProviderCombinationTests.cs`
- `managed/ReachyMini.Core.Tests/Program.cs` entrypoint wiring
- `scripts/tests/test_rma190_independent_provider_combinations.py`
- `scripts/tests/test_rma190_provider_combinations.py`

The transplanted managed coverage verifies all 16 ASR/TTS/LLM/VLM local/cloud execution
combinations round-trip independently through durable settings, that cycling one provider
never mutates another workload, that durable profile references remain workload-scoped,
and that fallback authorization remains workload-scoped.

## Remaining closure requirements

This branch still does **not** close the RMA-190 `Independent cloud provider combinations`
checkbox by itself. Closure still requires exact-head CI and any required Unity/provider
evidence after GitHub Actions stops failing before job steps/logs are available. No real
cloud-response success claim is made by this consolidation; real provider success still
requires explicit credentials/endpoints and separate evidence.

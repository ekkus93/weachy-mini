# RMA-190 network/provider failure consolidation — 2026-09-24

## Scope

This record documents consolidation of focused provider/network failure-mode coverage
and the managed transport-boundary end-to-end harness onto PR #67:

- branch: `ralph/rma190-network-failure-modes`
- consolidated head before this note: `47ed2ac7a7cf6519fe58849ba8a3ba2dcee88af2`

The consolidation preserves the existing PR #67 provider failure-mode source contracts
and transplants unique coverage from:

- `ralph/rma190-network-failure-e2e`

The older branch should not be closed or deleted until this consolidated PR #67 lane is
either merged or its unique changes are otherwise preserved.

## Coverage now present on PR #67

PR #67 now contains:

- `managed/ReachyMini.Core.Tests/Rma190NetworkProviderFailureScenarioTests.cs`
- `scripts/tests/test_rma190_network_failure_modes.py`
- `managed/ReachyMini.Core.Tests/Rma190NetworkFailureEndToEndTests.cs`
- `managed/ReachyMini.Core.Tests/Program.cs` entrypoint wiring for the RMA-190 network
  end-to-end harness
- `scripts/tests/test_rma190_network_failure_end_to_end.py`

The transplanted coverage crosses the production shared HTTP and OpenAI-compatible VLM
transport boundaries with fake network responses. It verifies fail-closed behavior for
network loss, rate limiting, malformed protocol response, and pre-request cancellation;
no synthetic success text or automatic provider fallback is permitted.

## Remaining closure requirements

This branch still does **not** close the RMA-190 `Network loss, rate limit, malformed
response, and cancellation` checkbox by itself. Closure still requires exact-head CI and
any required provider/transport artifact evidence after GitHub Actions stops failing
before job steps/logs are available.

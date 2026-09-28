# RMA-190 network/provider failure consolidation — 2026-09-24

## Scope

This record documents the reviewed network/provider failure-mode consolidation boundary for current `master`.

The reviewed branch was:

- branch: `ralph/rma190-network-failure-modes`
- inspected head: `79339b3ca97e68441fcda97aae167613bca1c2c4`

That branch consolidated focused provider/network failure-mode coverage, managed VLM transport-boundary end-to-end coverage, managed LLM provider failure-injection coverage, managed VLM provider no-hidden-retry coverage, provider failure-matrix coverage, and LLM provider network/rate-limit adapter coverage.

## Reviewed coverage on the branch

The branch reported preservation of coverage for:

- `managed/ReachyMini.Core.Tests/Rma190NetworkProviderFailureScenarioTests.cs`
- `scripts/tests/test_rma190_network_failure_modes.py`
- `managed/ReachyMini.Core.Tests/Rma190NetworkFailureEndToEndTests.cs`
- `managed/ReachyMini.Core.Tests/Rma190ProviderFailureEndToEndTests.cs`
- `managed/ReachyMini.Core.Tests/Rma190VlmProviderFailureEndToEndTests.cs`
- `managed/ReachyMini.Core.Tests/Rma190ProviderFailureMatrixEndToEndTests.cs`
- `managed/ReachyMini.Core.Tests/Rma190OpenAiLlmFailureModeContractTests.cs`

The branch-level records state that the VLM and LLM failure-mode coverage crosses the production shared HTTP and OpenAI-compatible transport/provider boundaries with deterministic fake network responses, covering network loss, HTTP 429/rate limiting, malformed protocol responses, and cancellation without synthetic success text or automatic provider fallback.

## Master transplant decision

This record does **not** transplant the network/provider failure source wholesale into current `master`. Prior review found source visibility and overlap risks in the broader deterministic-failure area, so this pass preserves only the bounded review/status record until every source file needed for the consolidated lane is readable and can be audited against current `master` without reconstructing redacted content.

## Remaining closure requirements

This record does **not** close the RMA-190 `Network loss, rate limit, malformed response, and cancellation` checkbox. Closure still requires readable source preservation, exact-head CI or equivalent configured validation, and any required provider/transport evidence after GitHub Actions stops failing before job steps/logs are available.

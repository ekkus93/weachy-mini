# RMA-190 network/provider failure consolidation — 2026-09-24

## Scope

This record documents consolidation of focused provider/network failure-mode coverage,
the managed VLM transport-boundary end-to-end harness, the managed LLM provider
failure-injection harness, and the managed VLM provider no-hidden-retry harness onto
PR #67:

- branch: `ralph/rma190-network-failure-modes`
- consolidated head before this note: `18211d3dc245f20578880d1f40f3593c5a0887c0`

The consolidation preserves the existing PR #67 provider failure-mode source contracts
and transplants unique coverage from:

- `ralph/rma190-network-failure-e2e`
- `ralph/rma190-provider-failure-e2e`
- `ralph/rma190-provider-failures`

The older branches should not be closed or deleted until this consolidated PR #67 lane is
either merged or their unique changes are otherwise preserved.

## Coverage now present on PR #67

PR #67 now contains:

- `managed/ReachyMini.Core.Tests/Rma190NetworkProviderFailureScenarioTests.cs`
- `scripts/tests/test_rma190_network_failure_modes.py`
- `managed/ReachyMini.Core.Tests/Rma190NetworkFailureEndToEndTests.cs`
- `managed/ReachyMini.Core.Tests/Program.cs` entrypoint wiring for the RMA-190 network
  end-to-end harness
- `scripts/tests/test_rma190_network_failure_end_to_end.py`
- `managed/ReachyMini.Core.Tests/Rma190ProviderFailureEndToEndTests.cs`
- `scripts/tests/test_rma190_provider_failure_e2e.py`
- `managed/ReachyMini.Core.Tests/Rma190VlmProviderFailureEndToEndTests.cs`
- `scripts/tests/test_rma190_vlm_provider_failures.py`
- `docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md` provider failure-injection
  contract text

The VLM transport-boundary coverage crosses the production shared HTTP and
OpenAI-compatible vision transport boundaries with fake network responses. It verifies
fail-closed behavior for network loss, rate limiting, malformed protocol response, and
pre-request cancellation; no synthetic success text or automatic provider fallback is
permitted.

The LLM provider failure-injection coverage drives the real
`OpenAiChatCompletionsLlmProvider` through `ReachySharedHttpTransport` using a
deterministic HTTP handler. It covers connection loss, HTTP 429, malformed provider
payloads, and in-flight caller cancellation, with exact single-attempt/no-hidden-retry
assertions and no committed credential or remote endpoint.

The VLM provider no-hidden-retry coverage preserves stricter assertions from
`ralph/rma190-provider-failures` under distinct class/file names so it can coexist with
the LLM provider harness. It checks exact HTTP status propagation, structured error
categories, `RequiresTransportReset`, no fabricated response text, and exactly one
transport attempt for each failure mode.

## Remaining closure requirements

This branch still does **not** close the RMA-190 `Network loss, rate limit, malformed
response, and cancellation` checkbox by itself. Closure still requires exact-head CI and
any required provider/transport artifact evidence after GitHub Actions stops failing
before job steps/logs are available.

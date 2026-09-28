# RMA-190 network/provider failure consolidation — 2026-09-24

## Scope

This record documents consolidation of focused provider/network failure-mode coverage,
the managed VLM transport-boundary end-to-end harness, the managed LLM provider
failure-injection harness, the managed VLM provider no-hidden-retry harness, the provider
failure matrix, and the LLM provider network/rate-limit adapter coverage onto PR #67:

- branch: `ralph/rma190-network-failure-modes`
- current consolidated head: `e9a33cb904985df0037d41bf98ebae84b41a443d`

The consolidation preserves the existing PR #67 provider failure-mode source contracts
and transplants unique coverage from:

- `ralph/rma190-network-failure-e2e`
- `ralph/rma190-provider-failure-e2e`
- `ralph/rma190-provider-failures`
- `ralph/rma190-provider-failure-matrix`
- `ralph/rma190-failure-modes` for LLM provider network-loss/rate-limit coverage only

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
- `managed/ReachyMini.Core.Tests/Rma190ProviderFailureMatrixEndToEndTests.cs`
- `scripts/tests/test_rma190_provider_failure_matrix_end_to_end.py`
- `managed/ReachyMini.Core.Tests/Rma190OpenAiLlmFailureModeContractTests.cs`
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

The provider failure-matrix coverage preserves additional shared-HTTP bounded-retry and
pre-send-cancellation contracts from `ralph/rma190-provider-failure-matrix`.

The LLM provider failure-mode contract preserves unique `OpenAiChatCompletionsLlmProvider`
network-loss and HTTP 429 assertions from `ralph/rma190-failure-modes` under a distinct
RMA-190 file, avoiding edits to the older RMA-142/RMA-143 adapter contract file while
still proving the shipping LLM provider surfaces those failures as non-success
`HttpFailure` results.

## Remaining closure requirements

This branch still does **not** close the RMA-190 `Network loss, rate limit, malformed
response, and cancellation` checkbox by itself. Closure still requires exact-head CI and
any required provider/transport artifact evidence after GitHub Actions stops failing
before job steps/logs are available.

#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANAGED = ROOT / "managed/ReachyMini.Core.Tests/Rma190ProviderFailureEndToEndTests.cs"
SPEC = ROOT / "docs/RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


class Rma190ProviderFailureEndToEndContracts(unittest.TestCase):
    def test_four_required_failure_modes_use_real_provider_transport(self) -> None:
        source = MANAGED.read_text(encoding="utf-8")
        for token in (
            "NetworkLossFailsClosedWithoutRetry",
            "RateLimitFailsClosedWithoutRetry",
            "MalformedProviderPayloadCannotBecomeSuccess",
            "InFlightCallerCancellationStopsTransport",
            "ReachySharedHttpTransport",
            "OpenAiChatCompletionsLlmProvider",
            "HttpRequestException",
            "(HttpStatusCode)429",
            "Task.Delay(",
            "Timeout.InfiniteTimeSpan",
        ):
            self.assertIn(token, source)

    def test_failures_never_become_success_or_hidden_retry(self) -> None:
        source = MANAGED.read_text(encoding="utf-8")
        self.assertGreaterEqual(
            source.count("ReachyLlmGenerationStatus.HttpFailure"),
            3,
        )
        self.assertIn("ReachyLlmGenerationStatus.Cancelled", source)
        self.assertGreaterEqual(
            source.count('Equal(1, handler.CallCount'),
            4,
        )
        self.assertGreaterEqual(
            source.count("False(result.Succeeded"),
            4,
        )

    def test_spec_and_roadmap_keep_evidence_truthful(self) -> None:
        spec = SPEC.read_text(encoding="utf-8")
        self.assertIn("Provider failure-injection contract", spec)
        self.assertIn("connection loss", spec)
        self.assertIn("HTTP 429", spec)
        self.assertIn("malformed provider payload", spec)
        self.assertIn("in-flight caller cancellation", spec)

        todo = TODO.read_text(encoding="utf-8")
        start = todo.index("## RMA-190")
        end = todo.index("## RMA-191", start)
        self.assertIn(
            "- [ ] Network loss, rate limit, malformed response, and cancellation.",
            todo[start:end],
        )


if __name__ == "__main__":
    unittest.main()

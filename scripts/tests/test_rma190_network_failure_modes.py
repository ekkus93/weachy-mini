#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = ROOT / "managed" / "ReachyMini.Core.Tests" / "Rma190NetworkProviderFailureScenarioTests.cs"
LLM_TEST = ROOT / "managed" / "ReachyMini.Core.Tests" / "Rma190OpenAiLlmFailureModeContractTests.cs"
SPEC = ROOT / "docs" / "RMA_190_END_TO_END_SCENARIOS_SPEC_2026-09-21.md"
TODO = ROOT / "docs" / "REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190NetworkFailureModeContracts(unittest.TestCase):
    def test_managed_scenario_covers_required_failure_modes(self) -> None:
        source = read(TEST)
        for token in (
            "NetworkLossSurfacesAsRetryableNetworkFailure",
            "RateLimitSurfacesAsExplicitQuotaFailure",
            "MalformedStreamingResponseFailsClosed",
            "CancellationBeforeTransportStartDoesNotReachProvider",
            "FailureModesDoNotAuthorizeSilentProviderSwitch",
            "ReachySharedHttpTransport",
            "ReachyProviderFallbackPolicyEngine",
        ):
            self.assertIn(token, source)
        for category in (
            "ReachyHttpErrorCategory.Network",
            "ReachyHttpErrorCategory.QuotaOrRateLimited",
            "ReachyHttpErrorCategory.MalformedResponse",
            "ReachyHttpErrorCategory.Cancelled",
        ):
            self.assertIn(category, source)
        self.assertIn("HttpRequestException", source)
        self.assertIn("(HttpStatusCode)429", source)
        self.assertIn("ReachyHttpResponseMode.ServerSentEvents", source)
        self.assertIn("Equal(0, handler.CallCount", source)

    def test_llm_provider_failures_surface_as_non_success(self) -> None:
        source = read(LLM_TEST)
        for token in (
            "NetworkLossSurfacesAsHttpFailure",
            "RateLimitSurfacesAsHttpFailure",
            "ReachyLlmGenerationStatus.HttpFailure",
            "OpenAiChatCompletionsLlmProvider",
            "ReachySharedHttpTransport",
            "synthetic RMA-190 network loss",
            "HttpStatusCode.TooManyRequests",
        ):
            self.assertIn(token, source)
        self.assertIn("False(result.Succeeded", source)
        self.assertIn("handler.CallCount >= 1", source)

    def test_scenario_remains_mapped_to_authoritative_rma190_row(self) -> None:
        todo = read(TODO)
        spec = read(SPEC)
        item = "Network loss, rate limit, malformed response, and cancellation."
        self.assertIn(f"- [ ] {item}", todo)
        self.assertIn("Network loss, rate limit, malformed response, cancellation", spec)
        self.assertIn("Deterministic HTTP/provider integration", spec)


if __name__ == "__main__":
    unittest.main()

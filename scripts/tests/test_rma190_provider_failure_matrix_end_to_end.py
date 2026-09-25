#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = ROOT / "managed" / "ReachyMini.Core.Tests" / "Rma190ProviderFailureMatrixEndToEndTests.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190ProviderFailureMatrixEndToEndContracts(unittest.TestCase):
    def test_real_transport_and_provider_cover_the_failure_matrix(self) -> None:
        source = read(TEST)
        self.assertIn("Rma190ProviderFailureMatrixEndToEndTests", source)
        self.assertIn("ReachySharedHttpTransport", source)
        self.assertIn("OpenAiChatCompletionsLlmProvider", source)
        self.assertIn("NetworkLossRetriesBoundedlyAndFailsClosed", source)
        self.assertIn("RateLimitRetriesBoundedlyAndFailsClosed", source)
        self.assertIn("MalformedProviderPayloadCannotBecomeBehaviorSuccess", source)
        self.assertIn("CancellationStopsBeforeNetwork", source)

    def test_failures_are_bounded_and_fail_closed(self) -> None:
        source = read(TEST)
        self.assertIn("ReachyHttpErrorCategory.Network", source)
        self.assertIn("ReachyHttpErrorCategory.QuotaOrRateLimited", source)
        self.assertIn("ReachyLlmGenerationStatus.BehaviorIntentInvalid", source)
        self.assertIn("ReachyHttpErrorCategory.Cancelled", source)
        self.assertIn("Equal(0, handler.CallCount, \"pre-send cancellation network calls\")", source)
        self.assertIn("maximumAttempts: 3", source)


if __name__ == "__main__":
    unittest.main()

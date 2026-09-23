#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = (
    ROOT
    / "managed"
    / "ReachyMini.Core.Tests"
    / "Rma190ProviderFailureEndToEndTests.cs"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190ProviderFailureContracts(unittest.TestCase):
    def test_acceptance_uses_shipping_vlm_http_transport(self) -> None:
        source = read(TEST)
        self.assertIn("ReachyOpenAiVisionHttpTransport", source)
        self.assertIn("ReachySharedHttpTransport", source)
        self.assertIn("ReachyBearerCredentialTransportBinding", source)
        self.assertIn("VisionFrameOrigin.TransformedReachyEye", source)

    def test_all_four_failure_modes_are_fail_closed(self) -> None:
        source = read(TEST)
        for scenario in (
            "NetworkLossFailsClosedWithoutRetry",
            "RateLimitFailsClosedWithoutRetry",
            "MalformedResponseFailsClosedWithoutRetry",
            "InFlightCancellationFailsClosedWithoutRetry",
        ):
            self.assertIn(f"{scenario}().GetAwaiter().GetResult();", source)
        for status in (
            "OpenAiVisionTransportStatus.Unavailable",
            "OpenAiVisionTransportStatus.RateLimited",
            "OpenAiVisionTransportStatus.ProtocolFailure",
            "OpenAiVisionTransportStatus.Cancelled",
        ):
            self.assertIn(status, source)
        self.assertIn("Equal<string?>(null, result.Text", source)

    def test_no_failure_mode_silently_retries(self) -> None:
        source = read(TEST)
        self.assertEqual(source.count('Equal(1, handler.CallCount,'), 4)
        self.assertIn(
            'throw new HttpRequestException("simulated network loss")',
            source,
        )
        self.assertIn("HttpStatusCode.TooManyRequests", source)
        self.assertIn('JsonResponse(HttpStatusCode.OK, "not json")', source)
        self.assertIn("callerCancellation.Cancel();", source)
        self.assertIn(
            "effectiveCancellation.ThrowIfCancellationRequested();",
            source,
        )


if __name__ == "__main__":
    unittest.main()

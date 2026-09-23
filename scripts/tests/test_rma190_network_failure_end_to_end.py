#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEST = ROOT / "managed" / "ReachyMini.Core.Tests" / "Rma190NetworkFailureEndToEndTests.cs"
PROGRAM = ROOT / "managed" / "ReachyMini.Core.Tests" / "Program.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190NetworkFailureEndToEndContracts(unittest.TestCase):
    def test_harness_crosses_production_http_and_vlm_transport_boundaries(self) -> None:
        source = read(TEST)
        self.assertIn("ReachySharedHttpTransport", source)
        self.assertIn("ReachyOpenAiVisionHttpTransport", source)
        self.assertIn("ReachyBearerCredentialTransportBinding.Create", source)
        self.assertIn("VisionFrameOrigin.TransformedReachyEye", source)

    def test_all_required_failure_modes_fail_closed(self) -> None:
        source = read(TEST)
        for status in (
            "OpenAiVisionTransportStatus.Unavailable",
            "OpenAiVisionTransportStatus.RateLimited",
            "OpenAiVisionTransportStatus.ProtocolFailure",
            "OpenAiVisionTransportStatus.Cancelled",
        ):
            self.assertIn(status, source)
        self.assertIn("Equal(null, result.Text", source)
        self.assertIn("handler.CallCount", source)
        self.assertNotIn("RemoteVlmReleasePolicy.AutomaticProviderFallbackEnabled = true", source)

    def test_core_ci_entrypoint_runs_harness(self) -> None:
        program = read(PROGRAM)
        self.assertIn("Rma190NetworkFailureEndToEndTests.RunAll();", program)


if __name__ == "__main__":
    unittest.main()

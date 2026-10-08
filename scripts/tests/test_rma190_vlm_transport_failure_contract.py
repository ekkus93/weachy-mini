#!/usr/bin/env python3
"""RMA-190 production VLM transport failure contracts.

These are source-level regressions, not physical, live-cloud, or end-to-end
acceptance evidence. The RMA-190 roadmap checkbox remains open.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TRANSPORT = ROOT / "Assets/ReachyMini/Runtime/Core/Perception/ReachyOpenAiVisionHttpTransport.cs"


def case_body(source: str, category: str) -> str:
    marker = f"case ReachyHttpErrorCategory.{category}:"
    return source.split(marker, 1)[1].split("case ReachyHttpErrorCategory.", 1)[0]


class Rma190VlmTransportFailureContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = TRANSPORT.read_text(encoding="utf-8")

    def test_production_transport_rejects_failed_http_results(self) -> None:
        self.assertIn("ReachySharedHttpTransport", self.source)
        self.assertIn("if (!result.Succeeded)", self.source)
        self.assertIn("return FailureFromTransport(result);", self.source)
        self.assertIn("return OpenAiVisionTransportResult.Failure(", self.source)
        self.assertIn("requiresTransportReset: true", self.source)

    def test_network_loss_is_unavailable_and_requires_reset(self) -> None:
        branch = case_body(self.source, "Network")
        self.assertIn("OpenAiVisionTransportStatus.Unavailable", branch)
        self.assertIn("OpenAiVisionProviderErrorCategory.Transport", branch)
        self.assertIn("true);", branch)

    def test_rate_limit_is_typed_without_automatic_reset(self) -> None:
        branch = case_body(self.source, "QuotaOrRateLimited")
        self.assertIn("OpenAiVisionTransportStatus.RateLimited", branch)
        self.assertIn("OpenAiVisionProviderErrorCategory.RateLimit", branch)
        self.assertIn("false);", branch)

    def test_malformed_response_is_protocol_failure(self) -> None:
        branch = case_body(self.source, "MalformedResponse")
        self.assertIn("OpenAiVisionTransportStatus.ProtocolFailure", branch)
        self.assertIn("OpenAiVisionProviderErrorCategory.Protocol", branch)
        self.assertIn("true);", branch)
        self.assertIn("catch (FormatException exception)", self.source)
        self.assertIn('"missing-assistant-text"', self.source)

    def test_cancellation_does_not_report_success(self) -> None:
        branch = case_body(self.source, "Cancelled")
        self.assertIn("OpenAiVisionTransportStatus.Cancelled", branch)
        self.assertIn("OpenAiVisionProviderErrorCategory.Transport", branch)
        self.assertIn("false);", branch)
        self.assertIn("catch (OperationCanceledException)", self.source)
        self.assertIn('"cancelled"', self.source)

    def test_sensitive_response_buffers_are_cleared(self) -> None:
        self.assertIn("Array.Clear(requestBody, 0, requestBody.Length)", self.source)
        self.assertIn(
            "Array.Clear(borrowedResponseBody, 0, borrowedResponseBody.Length)",
            self.source,
        )


if __name__ == "__main__":
    unittest.main()

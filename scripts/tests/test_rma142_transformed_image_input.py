#!/usr/bin/env python3
"""RMA-142 static contracts for transformed-image LLM input."""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LLM_CONTRACTS = ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyLlmContracts.cs"
LLM_CORE = (
    ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleLlmProvider.Core.cs"
)
LLM_HELPERS = (
    ROOT
    / "Assets/ReachyMini/Runtime/Core/Providers/ReachyOpenAiCompatibleLlmProvider.Helpers.cs"
)
LLM_TESTS = (
    ROOT / "managed/ReachyMini.Core.Tests/Rma142Rma143OpenAiLlmAdapterContractTests.cs"
)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma142TransformedImageInputContracts(unittest.TestCase):
    def test_request_accepts_only_bounded_transformed_reachy_eye_image(self) -> None:
        contracts = read(LLM_CONTRACTS)
        self.assertIn(
            "public const int MaximumImageBytes = 4 * 1024 * 1024;",
            contracts,
        )
        self.assertIn("RemoteVlmEncodedImage? image", contracts)
        self.assertIn("public RemoteVlmEncodedImage? Image { get; }", contracts)
        for token in (
            "image.SourceOrigin != VisionFrameOrigin.TransformedReachyEye",
            "!image.ValidityMaskApplied",
            "!image.ContainsOnlyValidPixels",
            "image.Upscaled",
            "image.EncodedByteCount > ReachyLlmProviderPolicy.MaximumImageBytes",
        ):
            self.assertIn(token, contracts)
        self.assertIn("ObjectDisposedException", contracts)

    def test_text_only_provider_rejects_image_before_http(self) -> None:
        core = read(LLM_CORE)
        self.assertIn("RemoteVlmEncodedImage? image = request.Image;", core)
        self.assertIn("image != null && !Capabilities.SupportsImages", core)
        self.assertIn("does not explicitly support images", core)
        self.assertIn("BuildRequestBody(", core)
        self.assertRegex(
            core,
            re.compile(
                r"BuildRequestBody\([\s\S]*?request\.Messages,"
                r"[\s\S]*?image,[\s\S]*?options\.MaximumOutputTokens",
                re.MULTILINE,
            ),
        )

    def test_openai_payloads_serialize_data_uri_for_both_endpoint_styles(self) -> None:
        helpers = read(LLM_HELPERS)
        for token in (
            "data:" + '" + image.MediaType + "' + ";base64,",
            "Convert.ToBase64String(image.EncodedBytes.Span)",
            '"input_text"',
            '"text"',
            '"input_image"',
            '"image_url"',
            '"url"',
        ):
            self.assertIn(token, helpers)
        self.assertNotIn("http://", helpers)
        self.assertNotIn("https://", helpers)

    def test_managed_contracts_cover_chat_responses_and_text_only_rejection(
        self,
    ) -> None:
        tests = read(LLM_TESTS)
        for token in (
            "ChatCompletionsSerializesTransformedImage",
            "ResponsesSerializesTransformedImage",
            "TextOnlyProviderRejectsImageBeforeHttp",
            "data:image/jpeg;base64,/9j/2Q==",
            "Equal(0, handler.CallCount",
            "VisionFrameOrigin.TransformedReachyEye",
            "RemoteVlmInvalidPixelPolicy.ReplaceWithOpaqueBlack",
            "validityMaskApplied: true",
            "containsOnlyValidPixels: true",
            "upscaled: false",
        ):
            self.assertIn(token, tests)


if __name__ == "__main__":
    unittest.main()

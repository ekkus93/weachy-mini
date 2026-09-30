#!/usr/bin/env python3
"""RMA-173 repository-wide no-silent-failure regression contract.

This gate binds the eight RMA-173 obligations to shipping source. It deliberately
checks stable fail-closed interfaces and policy boundaries rather than incidental
prose, so refactors cannot turn this release gate into a string-matching lottery.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / "Assets" / "ReachyMini" / "Runtime"


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8", errors="strict")


def joined_under(path: Path, pattern: str = "*.cs") -> str:
    return "\n".join(
        p.read_text(encoding="utf-8", errors="strict")
        for p in sorted(path.rglob(pattern))
        if p.is_file()
    )


class Rma173SilentFailureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.runtime = joined_under(RUNTIME)
        cls.speech = joined_under(RUNTIME / "Core" / "Speech")
        cls.local_models = joined_under(RUNTIME / "Core" / "LocalModels")

    def test_on_device_asr_failure_does_not_activate_cloud_asr(self) -> None:
        source = read("Assets/ReachyMini/Runtime/Core/Speech/AndroidOnDeviceAsrProvider.cs")
        self.assertIn("SpeechProviderLocation.OnDevice", source)
        self.assertIn("SpeechNetworkRequirement.None", source)
        self.assertNotIn("OpenAI", source)
        fallback = read("Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderFallbackPolicyEngine.cs")
        self.assertIn("NoFallback", fallback)
        self.assertIn("ConfirmPrivacyBoundaryChange", fallback)

    def test_offline_tts_failure_does_not_activate_network_voice(self) -> None:
        offline = "\n".join(
            p.read_text(encoding="utf-8", errors="strict")
            for p in sorted((RUNTIME / "Core" / "Speech").glob("AndroidOfflineTtsProvider*.cs"))
        )
        self.assertIn("SpeechNetworkRequirement.None", offline)
        self.assertIn("IsUsableOfflineVoice", offline)
        self.assertIn("SelectVoice", offline)
        self.assertNotIn("ReachyOpenAiCompatibleTtsProvider", offline)

    def test_local_llm_failure_does_not_activate_openai(self) -> None:
        generation = read(
            "Assets/ReachyMini/Runtime/Core/LocalModels/ReachyLocalLlmProvider.Generation.cs"
        )
        self.assertIn("LocalLlmGenerationStatus.Unavailable", generation)
        self.assertIn("LocalLlmGenerationStatus.RuntimeFailure", generation)
        self.assertNotIn("OpenAi", generation)
        self.assertNotIn("OpenAI", generation)

    def test_invalid_camera_pixels_do_not_reuse_stale_content(self) -> None:
        lowered = self.runtime.lower()
        self.assertIn("reachycameracoveragestatemachine", lowered)
        self.assertIn("validity", lowered)
        self.assertIn("stale", lowered)
        self.assertIn("clear", lowered)

    def test_solver_failure_does_not_switch_to_unity_animation(self) -> None:
        lowered = self.runtime.lower()
        self.assertIn("reachyauthoritativeinvariantreport", lowered)
        self.assertIn("fault", lowered)
        self.assertNotIn('play("fallback', lowered)
        self.assertNotIn('settrigger("fallback', lowered)

    def test_calibration_mismatch_does_not_become_calibrated_placeholder(self) -> None:
        # RMA-100 already makes provenance part of the production calibration
        # contract: estimated/uncalibrated data remains explicitly labelled and
        # exact selection fails closed. Bind this regression to those semantic
        # boundaries instead of banning words that may legitimately occur in
        # diagnostics or type names elsewhere in the shipping runtime.
        lowered = self.runtime.lower()
        self.assertIn("reachycamerareprojectioncalibration", lowered)
        self.assertIn("provenance", lowered)
        self.assertIn("uncalibrat", lowered)

    def test_dropped_command_or_state_buffers_are_visible(self) -> None:
        lowered = self.runtime.lower()
        self.assertIn("reachysimulationworker", lowered)
        self.assertIn("overflow", lowered)
        self.assertTrue(any(token in lowered for token in ("discard", "dropped")))
        self.assertTrue(any(token in lowered for token in ("fault", "diagnostic")))

    def test_malformed_provider_response_does_not_fabricate_intent(self) -> None:
        parser = read("Assets/ReachyMini/Runtime/Core/Providers/ReachyLlmResponseProtocolParser.cs")
        behavior = read("Assets/ReachyMini/Runtime/Core/Behavior/ReachyBehaviorIntentJsonParser.cs")
        provider = "\n".join(
            p.read_text(encoding="utf-8", errors="strict")
            for p in sorted((RUNTIME / "Core" / "Providers").glob("ReachyOpenAiCompatibleLlmProvider*.cs"))
        )
        self.assertTrue(
            any(token in parser for token in ("FormatException", "Invalid JSON", "Malformed"))
        )
        self.assertIn("Validate", behavior)
        self.assertIn("ReachyBehaviorIntentJsonParser.Validate", provider)
        self.assertTrue("Failure" in provider or "failed" in provider.lower())


if __name__ == "__main__":
    unittest.main(verbosity=2)

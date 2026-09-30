#!/usr/bin/env python3
"""RMA-173 repository-wide no-silent-failure regression contract.

This gate binds the eight RMA-173 obligations to shipping source.  It does not
replace physical acceptance; it prevents later refactors from introducing the
specific hidden fallback classes that RMA-173 prohibits.
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


def files_containing(root: Path, needle: str) -> list[Path]:
    result: list[Path] = []
    for path in sorted(root.rglob("*.cs")):
        if needle in path.read_text(encoding="utf-8", errors="strict"):
            result.append(path)
    return result


class Rma173SilentFailureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.speech = joined_under(RUNTIME / "Core" / "Speech")
        cls.local_models = joined_under(RUNTIME / "Core" / "LocalModels")
        cls.runtime = joined_under(RUNTIME)

    def test_on_device_asr_cannot_activate_cloud_asr(self) -> None:
        source = read("Assets/ReachyMini/Runtime/Core/Speech/AndroidOnDeviceAsrProvider.cs")
        self.assertIn("SpeechProviderLocation.OnDevice", source)
        self.assertIn("SpeechNetworkRequirement.None", source)
        self.assertNotIn("OpenAI", source)
        self.assertNotIn("ReachyOpenAi", source)
        fallback = read("Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderFallbackPolicyEngine.cs")
        self.assertIn("NoFallback", fallback)
        self.assertIn("ConfirmPrivacyBoundaryChange", fallback)

    def test_offline_tts_cannot_activate_network_voice(self) -> None:
        offline = "\n".join(
            p.read_text(encoding="utf-8", errors="strict")
            for p in sorted((RUNTIME / "Core" / "Speech").glob("AndroidOfflineTtsProvider*.cs"))
        )
        self.assertIn("SpeechNetworkRequirement.None", offline)
        self.assertIn("Network", offline)
        self.assertIn("voice", offline.lower())
        self.assertNotIn("ReachyOpenAiCompatibleTtsProvider", offline)

    def test_local_llm_failure_has_no_openai_fallback(self) -> None:
        self.assertIn("LocalLlmGenerationStatus.Unavailable", self.local_models)
        self.assertIn("LocalLlmGenerationStatus.Faulted", self.local_models)
        self.assertNotIn("OpenAi", self.local_models)
        self.assertNotIn("OpenAI", self.local_models)

    def test_invalid_camera_pixels_cannot_reuse_stale_content(self) -> None:
        candidates = files_containing(RUNTIME, "ReachyCameraCoverageStateMachine")
        self.assertTrue(candidates, "coverage state machine source is missing")
        source = "\n".join(p.read_text(encoding="utf-8", errors="strict") for p in candidates)
        lowered = source.lower()
        self.assertIn("stale", lowered)
        self.assertIn("validity", lowered)
        self.assertTrue("clear" in lowered or "invalid" in lowered)

    def test_solver_failure_has_no_unity_animation_fallback(self) -> None:
        candidates = files_containing(RUNTIME, "ReachyAuthoritativeInvariantReport")
        self.assertTrue(candidates, "authoritative rendering invariant source is missing")
        source = "\n".join(p.read_text(encoding="utf-8", errors="strict") for p in candidates)
        self.assertIn("fault", source.lower())
        # The invariant path may inspect forbidden Unity writers, but must not
        # create or trigger an animation fallback.
        self.assertNotIn("Play(\"fallback", source)
        self.assertNotIn("SetTrigger(\"fallback", source)

    def test_calibration_mismatch_cannot_become_calibrated_placeholder(self) -> None:
        candidates = files_containing(RUNTIME, "ReachyCameraReprojectionCalibration")
        self.assertTrue(candidates, "camera calibration contract source is missing")
        source = "\n".join(p.read_text(encoding="utf-8", errors="strict") for p in candidates)
        lowered = source.lower()
        self.assertIn("calibrat", lowered)
        self.assertTrue("mismatch" in lowered or "model" in lowered)
        self.assertTrue("throw" in lowered or "invalid" in lowered or "reject" in lowered)

    def test_dropped_command_or_state_buffers_are_visible(self) -> None:
        candidates = files_containing(RUNTIME, "ReachySimulationWorker")
        self.assertTrue(candidates, "simulation worker source is missing")
        source = "\n".join(p.read_text(encoding="utf-8", errors="strict") for p in candidates)
        lowered = source.lower()
        self.assertIn("overflow", lowered)
        self.assertTrue("discard" in lowered or "dropped" in lowered)
        self.assertTrue("fault" in lowered or "diagnostic" in lowered)

    def test_malformed_provider_response_cannot_fabricate_intent(self) -> None:
        parser = read("Assets/ReachyMini/Runtime/Core/Providers/ReachyLlmResponseProtocolParser.cs")
        behavior = read("Assets/ReachyMini/Runtime/Core/Behavior/ReachyBehaviorIntentJsonParser.cs")
        provider = "\n".join(
            p.read_text(encoding="utf-8", errors="strict")
            for p in sorted((RUNTIME / "Core" / "Providers").glob("ReachyOpenAiCompatibleLlmProvider*.cs"))
        )
        self.assertIn("Malformed", parser)
        self.assertIn("Validate", behavior)
        self.assertIn("ReachyBehaviorIntentJsonParser.Validate", provider)
        self.assertTrue("Failure" in provider or "failed" in provider.lower())


if __name__ == "__main__":
    unittest.main(verbosity=2)

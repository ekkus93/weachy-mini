#!/usr/bin/env python3
from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
WORKFLOW = ROOT / ".github/workflows/rma190-offline-speech-end-to-end.yml"
RUNNER = ROOT / "scripts/run_rma190_offline_speech_end_to_end_android.sh"
TODO = ROOT / "docs/REACHY_MINI_ANDROID_DIGITAL_TWIN_TODO.md"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class Rma190OfflineSpeechEndToEndContracts(unittest.TestCase):
    def test_harness_uses_shipping_path_without_speech_injection(self) -> None:
        source = read(APP / "ReachyRma190OfflineSpeechEndToEndAcceptance.cs")
        self.assertIn("FindAnyObjectByType<ReachyApplicationHostBehaviour>", source)
        self.assertIn("FindAnyObjectByType<ReachyMainScreen>", source)
        self.assertIn("LocalLlmBehaviorContract.CreateSelectedManifest()", source)
        self.assertIn("new LocalModelPackageManager(", source)
        self.assertIn("settings.CycleProvider(kind)", source)
        self.assertIn("screen.RequestMicrophone();", source)
        self.assertIn("provider.ProviderSnapshot", source)
        self.assertIn("behavior.Snapshot.Revision", source)
        self.assertIn("human_utterance_required = true", source)
        self.assertIn("synthetic_speech_injected = false", source)
        self.assertIn("transcript_persisted = false", source)
        self.assertIn("raw_audio_persisted = false", source)
        self.assertIn("cloud_credentials_injected = false", source)
        self.assertNotIn("new AsrEvent(", source)
        self.assertNotIn("Fake", source)

    def test_runner_proves_network_disabled_and_restores_state(self) -> None:
        runner = read(RUNNER)
        self.assertIn("svc wifi disable", runner)
        self.assertIn("svc data disable", runner)
        self.assertIn("cmd connectivity airplane-mode enable", runner)
        self.assertIn("settings get global airplane_mode_on", runner)
        self.assertIn("restore_network", runner)
        self.assertIn("PHYSICAL ACTION REQUIRED", runner)
        self.assertIn("Do not play a recording or inject a transcript", runner)
        self.assertIn("--ez reachy_rma190_offline_speech_e2e_acceptance true", runner)
        self.assertIn('"transcript_persisted"', runner)
        self.assertIn('"raw_audio_persisted"', runner)

    def test_workflow_is_manual_physical_only(self) -> None:
        workflow = read(WORKFLOW)
        self.assertIn("workflow_dispatch:", workflow)
        self.assertNotIn("\n  push:", workflow)
        self.assertIn("weachy-mini-android-device", workflow)
        self.assertIn("run_rma190_offline_speech_end_to_end_android.sh", workflow)
        self.assertNotIn("upload-artifact", workflow)

    def test_authoritative_acceptance_remains_open_until_real_run(self) -> None:
        todo = read(TODO)
        self.assertIn(
            "- [ ] On a device with installed services, the user can converse "
            "using Android on-device ASR and offline TTS with networking disabled.",
            todo,
        )
        self.assertIn(
            "- [ ] On-device ASR -> local LLM -> behavior -> offline TTS.",
            todo,
        )


if __name__ == "__main__":
    unittest.main()

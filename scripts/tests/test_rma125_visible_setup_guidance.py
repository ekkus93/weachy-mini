#!/usr/bin/env python3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "Assets/ReachyMini/Runtime/Application"
SPEECH = ROOT / "Assets/ReachyMini/Runtime/Core/Speech"


class Rma125VisibleSetupGuidanceContracts(unittest.TestCase):
    def test_optional_audio_health_is_projected_into_visible_settings_status(self) -> None:
        source = (APP / "ReachySettingsApplicationCompositionProvider.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("eventArgs.Health.Kind == ReachyServiceKind.Audio", source)
        self.assertIn("ReachyServiceState.Unavailable", source)
        self.assertIn("ReachyServiceState.Degraded", source)
        self.assertIn("ReachyServiceState.Faulted", source)
        self.assertIn(
            'stateStore.ReportUnavailableAction(\n'
            '                        "Speech setup",\n'
            "                        eventArgs.Health.Message);",
            source,
        )

    def test_offline_tts_setup_required_diagnostic_is_actionable(self) -> None:
        source = (SPEECH / "AndroidOfflineTtsProvider.Internal.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("SpeechAvailabilityState.SetupRequired", source)
        self.assertIn("Install offline voice data from Android Text-to-speech settings", source)
        self.assertIn("will not select a network-required voice automatically", source)

    def test_on_device_asr_exposes_setup_required_model_states(self) -> None:
        source = (SPEECH / "AndroidOnDeviceAsrProvider.Readiness.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("AndroidOnDeviceAsrSupportState.ModelDownloadRequired", source)
        self.assertIn("AndroidOnDeviceAsrSupportState.ModelDownloadPending", source)
        self.assertIn("SpeechAvailabilityState.SetupRequired", source)

    def test_offline_default_has_no_hidden_network_fallback(self) -> None:
        source = (APP / "ReachyAndroidSpeechAudioStack.cs").read_text(encoding="utf-8")
        self.assertIn("ReachyAndroidOnDeviceAsrProviderFactory.Create", source)
        self.assertIn("ReachyAndroidOfflineTtsProviderFactory.Create", source)
        self.assertNotIn("OpenAI", source)
        self.assertNotIn("AndroidSystemTtsProvider", source)


if __name__ == "__main__":
    unittest.main()

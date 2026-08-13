import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CORE = ROOT / "Assets/ReachyMini/Runtime/Core/Application"
APP = ROOT / "Assets/ReachyMini/Runtime/Application"


class Rma162PrivateMediaRetentionTests(unittest.TestCase):
    def test_policy_is_default_deny_and_history_is_bounded(self) -> None:
        source = (CORE / "ReachyPrivateMediaRetentionPolicy.cs").read_text(encoding="utf-8")
        for token in (
            "RawCameraFrame",
            "MicrophoneAudio",
            "CloudRequestMedia",
            "RecordingEnabled => false",
            "MediaExportEnabled => false",
            "historyEnabled && configuredRetentionDays > 0",
            "{ 0, 7, 30, 90 }",
        ):
            self.assertIn(token, source)

    def test_temporary_storage_is_disposable_and_private(self) -> None:
        store = (CORE / "ReachyPrivateMediaTemporaryFileStore.cs").read_text(encoding="utf-8")
        adapter = (APP / "ReachyPrivateMediaStorage.cs").read_text(encoding="utf-8")
        self.assertIn("IDisposable", store)
        self.assertIn("PurgeAbandonedFiles", store)
        self.assertIn("File.Delete", store)
        self.assertIn("Application.temporaryCachePath", adapter)
        self.assertNotIn("Application.persistentDataPath", adapter)

    def test_privacy_ui_has_explicit_recording_and_export_gates(self) -> None:
        ui = (APP / "ReachyMainScreen.SettingsSections.cs").read_text(encoding="utf-8")
        self.assertIn("MEDIA RECORDING  OFF — OPT-IN REQUIRED", ui)
        self.assertIn("MEDIA EXPORT  UNAVAILABLE — CONSENT REQUIRED", ui)
        self.assertIn("PersistentMediaRetentionUnavailableReason", ui)

    def test_camera_png_persistence_is_acceptance_only(self) -> None:
        evidence = (APP / "ReachyCameraTextureEvidence.cs").read_text(encoding="utf-8")
        self.assertIn("AcceptanceLaunchExtra", evidence)
        self.assertIn("EncodeToPNG()", evidence)
        writers = sorted(
            path.name
            for path in APP.glob("*.cs")
            if "EncodeToPNG()" in path.read_text(encoding="utf-8")
        )
        self.assertEqual(["ReachyCameraTextureEvidence.cs"], writers)


if __name__ == "__main__":
    unittest.main()

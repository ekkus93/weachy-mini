import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STORE = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyFallbackPolicyPersistence.cs"


class Rma146FallbackPolicyPersistenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = STORE.read_text(encoding="utf-8")

    def test_policy_settings_are_separate_versioned_storage(self) -> None:
        self.assertIn('PolicyFileName = "reachy-fallback-policies-v1.json"', self.source)
        self.assertIn("SchemaVersion = 1", self.source)
        self.assertNotIn("reachy-settings-v1.json", self.source)

    def test_all_four_workloads_are_persisted_independently(self) -> None:
        for workload in ("Asr", "Tts", "Llm", "Vlm"):
            self.assertIn(f"ReachyProviderWorkloadKind.{workload}", self.source)
        self.assertIn("exactly four workload policies", self.source)
        self.assertIn("duplicate workload entries", self.source)

    def test_invalid_settings_fail_closed_and_are_quarantined(self) -> None:
        self.assertIn("QuarantineInvalidFile", self.source)
        self.assertIn("ResetToNoFallback", self.source)
        self.assertIn("fail-closed no-fallback defaults were restored", self.source)
        self.assertIn("ReachyFallbackPolicy.NoFallback()", self.source)

    def test_failed_persist_rolls_policy_back(self) -> None:
        self.assertIn("ReachyFallbackPolicy previous", self.source)
        self.assertIn("engine.SetPolicy(workload, previous)", self.source)

    def test_export_contains_policy_metadata_not_credentials(self) -> None:
        self.assertIn("ExportRedactedJson", self.source)
        lowered = self.source.lower()
        self.assertNotIn("credentialreference", lowered)
        self.assertNotIn("secretstore", lowered)
        self.assertNotIn("apikey", lowered)
        self.assertNotIn("authorization", lowered)

    def test_atomic_publish_uses_temp_and_backup(self) -> None:
        self.assertIn('temporaryPath = persistencePath + ".tmp"', self.source)
        self.assertIn('backupPath = persistencePath + ".bak"', self.source)
        self.assertIn("File.Move(temporaryPath, persistencePath)", self.source)


if __name__ == "__main__":
    unittest.main()

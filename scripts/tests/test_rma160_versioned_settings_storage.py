import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SETTINGS = ROOT / "Assets/ReachyMini/Runtime/Application/ReachySettingsPersistence.cs"
REFERENCES = ROOT / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStorageReferences.cs"
STATE_PERSISTENCE = (
    ROOT
    / "Assets/ReachyMini/Runtime/Core/Application/ReachySettingsStateStore.Persistence.cs"
)
PROVIDERS = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyProviderProfilePersistence.cs"
PROVIDER_CONTRACT = ROOT / "Assets/ReachyMini/Runtime/Core/Providers/ReachyProviderConfiguration.cs"
CALIBRATION = ROOT / "Assets/ReachyMini/Runtime/Application/ReachyCameraCalibrationPersistence.cs"
MANIFEST = ROOT / "models/manifests/qwen3-0.6b-q4-k-m.local-llm.json"
UNITY_TESTS = ROOT / "Assets/ReachyMini/Tests/Editor/ReachyVersionedSettingsPersistenceTests.cs"


class Rma160VersionedSettingsStorageContracts(unittest.TestCase):
    def test_schema_migration_and_explicit_recovery_are_permanent_contracts(self) -> None:
        source = SETTINGS.read_text(encoding="utf-8")
        for required in (
            "CurrentSchemaVersion = 2",
            "LegacySchemaVersion = 1",
            "LegacySettingsEnvelope",
            "sourceSchemaVersion == LegacySchemaVersion",
            "RecoveryRequired",
            "QuarantinedSettingsPath",
            "ExportQuarantinedSettings",
            "ImportRecoveredSettingsJson",
            "ResetToDefaultsAfterCorruption",
            "defaults were not persisted",
            "if (RecoveryRequired)",
        ):
            self.assertIn(required, source)
        self.assertNotIn("were restored", source.split("catch (Exception primaryException)", 1)[1])

    def test_settings_document_only_contains_stable_non_secret_references(self) -> None:
        source = SETTINGS.read_text(encoding="utf-8")
        references = REFERENCES.read_text(encoding="utf-8")
        for required in (
            "AsrProviderProfileId",
            "TtsProviderProfileId",
            "LlmProviderProfileId",
            "VlmProviderProfileId",
            "CameraCalibrationProfileId",
            "ModelManifestId",
            "ModelManifestSha256",
            "DeviceProfileId",
        ):
            self.assertIn(required, references)
        for forbidden in (
            "ApiKey",
            "AccessToken",
            "CredentialValue",
            "SecretValue",
            "apiKey",
            "accessToken",
            "credentialValue",
            "secretValue",
        ):
            self.assertNotIn(forbidden, source)
            self.assertNotIn(forbidden, references)

    def test_invalid_persisted_values_are_rejected_not_sanitized(self) -> None:
        source = STATE_PERSISTENCE.read_text(encoding="utf-8")
        self.assertIn("ValidateDurableSettings(durable);", source)
        self.assertIn("ValidateProviderExecution", source)
        self.assertNotIn("SanitizeProviderExecution(durable", source)
        for required in (
            "SpeechLanguages",
            "SpeechVoices",
            "MemoryBudgetsMb",
            "ContextLengths",
            "RetentionPeriods",
        ):
            self.assertIn(required, source)

    def test_existing_specialized_stores_remain_separate_and_secret_safe(self) -> None:
        provider_store = PROVIDERS.read_text(encoding="utf-8")
        provider_contract = PROVIDER_CONTRACT.read_text(encoding="utf-8")
        calibration = CALIBRATION.read_text(encoding="utf-8")
        self.assertIn('ProfileFileName = "reachy-provider-profiles-v1.json"', provider_store)
        self.assertIn("credentialReference", provider_store)
        self.assertIn("SecretReference", provider_contract)
        self.assertIn(
            "Sensitive provider headers must use a secret reference",
            provider_contract,
        )
        self.assertIn(
            '"reachy-camera-calibration-v1.json"',
            calibration,
        )
        self.assertTrue(MANIFEST.is_file())

    def test_managed_tests_cover_migration_recovery_and_reference_round_trip(self) -> None:
        tests = UNITY_TESTS.read_text(encoding="utf-8")
        for required in (
            "SchemaOneMigratesInPlaceWithoutLosingDurableSettings",
            "VersionTwoRoundTripsOnlyStableNonSecretReferences",
            "CorruptionRequiresExplicitRecoveryAndNeverPersistsDefaults",
            "UnsupportedFutureSchemaFailsClosedIntoRecovery",
            "SemanticallyInvalidSettingsFailClosedInsteadOfBeingSanitized",
            "RecoveryImportValidatesBeforeReplacingQuarantine",
        ):
            self.assertIn(required, tests)


if __name__ == "__main__":
    unittest.main()

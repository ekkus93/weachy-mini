#nullable enable

using System;
using System.IO;
using NUnit.Framework;
using ReachyMini.AppState;

namespace ReachyMini.Tests
{
    public sealed class ReachyVersionedSettingsPersistenceTests
    {
        [Test]
        public void SchemaOneMigratesInPlaceWithoutLosingDurableSettings()
        {
            WithTemporarySettingsPath(path =>
            {
                File.WriteAllText(
                    path,
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"asrExecution\": 0,\n" +
                    "  \"ttsExecution\": 0,\n" +
                    "  \"llmExecution\": 3,\n" +
                    "  \"vlmExecution\": 0,\n" +
                    "  \"preferredCameraFacing\": 2,\n" +
                    "  \"speechLanguage\": \"English (United States)\",\n" +
                    "  \"speechVoice\": \"System default\",\n" +
                    "  \"localModelMemoryBudgetMb\": 1024,\n" +
                    "  \"localModelContextTokens\": 4096,\n" +
                    "  \"simulationFidelity\": 1,\n" +
                    "  \"historyEnabled\": true,\n" +
                    "  \"retentionDays\": 30\n" +
                    "}\n");

                using var service =
                    new ReachySettingsPersistenceApplicationService(path);
                service.Initialize();

                Assert.That(service.RecoveryRequired, Is.False);
                Assert.That(
                    service.Settings.Current.GetProvider(
                        ReachyProviderKind.Llm).Execution,
                    Is.EqualTo(ReachyProviderExecution.Cloud));
                Assert.That(service.Settings.Current.HistoryEnabled, Is.True);
                Assert.That(service.References.ModelManifestId, Is.Empty);
                StringAssert.Contains(
                    "\"schemaVersion\": 2",
                    File.ReadAllText(path));
            });
        }

        [Test]
        public void VersionTwoRoundTripsOnlyStableNonSecretReferences()
        {
            WithTemporarySettingsPath(path =>
            {
                string manifestSha = new string('a', 64);
                using (var writer =
                    new ReachySettingsPersistenceApplicationService(path))
                {
                    writer.Initialize();
                    writer.UpdateReferences(
                        new ReachySettingsStorageReferences(
                            "asr-main",
                            "tts-main",
                            "llm-local",
                            "vlm-main",
                            "rear-calibration-01",
                            "qwen3-0.6b-q4-k-m",
                            manifestSha,
                            "android-mid-v1"));

                    string exported = writer.ExportCurrentSettingsJson();
                    StringAssert.Contains("llm-local", exported);
                    StringAssert.Contains("rear-calibration-01", exported);
                    StringAssert.Contains("qwen3-0.6b-q4-k-m", exported);
                    StringAssert.Contains("android-mid-v1", exported);
                    StringAssert.DoesNotContain("apiKey", exported);
                    StringAssert.DoesNotContain("accessToken", exported);
                    StringAssert.DoesNotContain("credentialValue", exported);
                    StringAssert.DoesNotContain("secretValue", exported);
                }

                using var reader =
                    new ReachySettingsPersistenceApplicationService(path);
                reader.Initialize();
                Assert.That(reader.References.AsrProviderProfileId, Is.EqualTo("asr-main"));
                Assert.That(reader.References.LlmProviderProfileId, Is.EqualTo("llm-local"));
                Assert.That(
                    reader.References.CameraCalibrationProfileId,
                    Is.EqualTo("rear-calibration-01"));
                Assert.That(
                    reader.References.ModelManifestSha256,
                    Is.EqualTo(manifestSha));
                Assert.That(reader.References.DeviceProfileId, Is.EqualTo("android-mid-v1"));
            });
        }

        [Test]
        public void CorruptionRequiresExplicitRecoveryAndNeverPersistsDefaults()
        {
            WithTemporarySettingsPath(path =>
            {
                using (var writer =
                    new ReachySettingsPersistenceApplicationService(path))
                {
                    writer.Initialize();
                }
                File.WriteAllText(path, "{not-valid-json");

                using var recovery =
                    new ReachySettingsPersistenceApplicationService(path);
                recovery.Initialize();

                Assert.That(recovery.RecoveryRequired, Is.True);
                Assert.That(recovery.Health.State, Is.EqualTo(ReachyServiceState.Degraded));
                Assert.That(recovery.LastPersistenceFault, Is.Not.Empty);
                Assert.That(
                    File.Exists(path),
                    Is.False,
                    "Corrupt durable settings must not be silently replaced with defaults.");
                Assert.That(File.Exists(recovery.QuarantinedSettingsPath), Is.True);

                recovery.Settings.ToggleHistory();
                Assert.That(
                    File.Exists(path),
                    Is.False,
                    "Ordinary setting changes must not bypass the recovery gate.");

                string exportPath = path + ".recovery-export";
                recovery.ExportQuarantinedSettings(exportPath);
                Assert.That(File.ReadAllText(exportPath), Is.EqualTo("{not-valid-json"));

                recovery.ResetToDefaultsAfterCorruption();
                Assert.That(recovery.RecoveryRequired, Is.False);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(recovery.QuarantinedSettingsPath), Is.True);
                StringAssert.Contains("\"schemaVersion\": 2", File.ReadAllText(path));
            });
        }

        [Test]
        public void UnsupportedFutureSchemaFailsClosedIntoRecovery()
        {
            WithTemporarySettingsPath(path =>
            {
                File.WriteAllText(path, "{\"schemaVersion\":99}");
                using var service =
                    new ReachySettingsPersistenceApplicationService(path);
                service.Initialize();

                Assert.That(service.RecoveryRequired, Is.True);
                Assert.That(File.Exists(path), Is.False);
                StringAssert.Contains(
                    "Unsupported settings schema 99",
                    service.LastPersistenceFault);
            });
        }

        [Test]
        public void SemanticallyInvalidSettingsFailClosedInsteadOfBeingSanitized()
        {
            WithTemporarySettingsPath(path =>
            {
                File.WriteAllText(
                    path,
                    "{\n" +
                    "  \"schemaVersion\": 2,\n" +
                    "  \"llmExecution\": 2,\n" +
                    "  \"speechLanguage\": \"System default\",\n" +
                    "  \"speechVoice\": \"System default\",\n" +
                    "  \"localModelMemoryBudgetMb\": 1024,\n" +
                    "  \"localModelContextTokens\": 4096,\n" +
                    "  \"retentionDays\": 30\n" +
                    "}\n");
                using var service =
                    new ReachySettingsPersistenceApplicationService(path);
                service.Initialize();

                Assert.That(service.RecoveryRequired, Is.True);
                Assert.That(File.Exists(path), Is.False);
                StringAssert.Contains(
                    "Android-service execution is not supported",
                    service.LastPersistenceFault);
            });
        }

        [Test]
        public void RecoveryImportValidatesBeforeReplacingQuarantine()
        {
            WithTemporarySettingsPath(path =>
            {
                File.WriteAllText(path, "{broken");
                using var service =
                    new ReachySettingsPersistenceApplicationService(path);
                service.Initialize();
                Assert.That(service.RecoveryRequired, Is.True);

                Assert.Throws<Exception>(
                    () => service.ImportRecoveredSettingsJson("{still-broken"));
                Assert.That(service.RecoveryRequired, Is.True);
                Assert.That(File.Exists(path), Is.False);

                service.ImportRecoveredSettingsJson(
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"speechLanguage\": \"System default\",\n" +
                    "  \"speechVoice\": \"System default\",\n" +
                    "  \"localModelMemoryBudgetMb\": 1024,\n" +
                    "  \"localModelContextTokens\": 4096,\n" +
                    "  \"retentionDays\": 30\n" +
                    "}\n");

                Assert.That(service.RecoveryRequired, Is.False);
                Assert.That(File.Exists(path), Is.True);
                StringAssert.Contains("\"schemaVersion\": 2", File.ReadAllText(path));
            });
        }

        private static void WithTemporarySettingsPath(Action<string> action)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "weachy-rma160-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(Path.Combine(directory, "settings.json"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }
}

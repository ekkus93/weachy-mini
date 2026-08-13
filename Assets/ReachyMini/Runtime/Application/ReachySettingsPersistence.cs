#nullable enable

using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachySettingsPersistenceApplicationService :
        ReachyApplicationServiceBase,
        IReachyPersistenceService
    {
        public const string SettingsFileName = "reachy-settings-v1.json";
        public const int CurrentSchemaVersion = 2;
        public const int LegacySchemaVersion = 1;

        private readonly string persistencePath;
        private readonly ReachyCameraCalibrationPersistenceStore cameraCalibrations;
        private bool loading;
        private string lastSerialized = string.Empty;
        private string settingsStatusMessage = string.Empty;

        public ReachySettingsPersistenceApplicationService()
            : this(
                Path.Combine(
                    Application.persistentDataPath,
                    SettingsFileName),
                Path.Combine(
                    Application.persistentDataPath,
                    ReachyCameraCalibrationPersistenceStore.CalibrationFileName))
        {
        }

        public ReachySettingsPersistenceApplicationService(string settingsPath)
            : this(
                settingsPath,
                GetDefaultCalibrationPath(settingsPath))
        {
        }

        public ReachySettingsPersistenceApplicationService(
            string settingsPath,
            string cameraCalibrationPath)
            : base(
                "durable-settings",
                ReachyServiceKind.Persistence,
                ReachyServiceCriticality.Required)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException(
                    "Settings persistence requires a file path.",
                    nameof(settingsPath));
            }
            if (string.IsNullOrWhiteSpace(cameraCalibrationPath))
            {
                throw new ArgumentException(
                    "Camera calibration persistence requires a file path.",
                    nameof(cameraCalibrationPath));
            }

            persistencePath = Path.GetFullPath(settingsPath);
            cameraCalibrations =
                new ReachyCameraCalibrationPersistenceStore(
                    cameraCalibrationPath);
        }

        public ReachySettingsStateStore Settings { get; } =
            new ReachySettingsStateStore();

        public ReachyCameraCalibrationPersistenceStore CameraCalibrations =>
            cameraCalibrations;

        public ReachySettingsStorageReferences References { get; private set; } =
            ReachySettingsStorageReferences.Empty;

        public string PersistencePath => persistencePath;

        public string CalibrationPersistencePath =>
            cameraCalibrations.PersistencePath;

        public string LastPersistenceFault { get; private set; } = string.Empty;

        public bool RecoveryRequired { get; private set; }

        public string QuarantinedSettingsPath { get; private set; } = string.Empty;

        protected override void OnInitialize()
        {
            Settings.Changed += OnSettingsChanged;
            cameraCalibrations.Initialize();
            cameraCalibrations.Changed += OnCameraCalibrationPersistenceChanged;
            LoadOrCreateSettings();
            PublishCombinedHealth();
        }

        protected override void OnDispose()
        {
            Settings.Changed -= OnSettingsChanged;
            cameraCalibrations.Changed -=
                OnCameraCalibrationPersistenceChanged;
            cameraCalibrations.Dispose();
        }

        public void UpdateReferences(ReachySettingsStorageReferences references)
        {
            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }
            if (RecoveryRequired)
            {
                throw new InvalidOperationException(
                    "Durable settings recovery must be resolved before reference selections can be changed.");
            }

            ReachySettingsStorageReferences previous = References;
            References = references;
            try
            {
                PersistCurrent();
                LastPersistenceFault = string.Empty;
                settingsStatusMessage =
                    $"Durable settings references persisted at {persistencePath}.";
            }
            catch
            {
                References = previous;
                throw;
            }
            PublishCombinedHealth();
        }

        public string ExportCurrentSettingsJson()
        {
            return Serialize(Settings.CaptureDurableSettings(), References);
        }

        public void ExportQuarantinedSettings(string destinationPath)
        {
            if (!RecoveryRequired || string.IsNullOrEmpty(QuarantinedSettingsPath))
            {
                throw new InvalidOperationException(
                    "There is no quarantined durable-settings file to export.");
            }
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException(
                    "A recovery export requires a destination path.",
                    nameof(destinationPath));
            }
            string fullDestination = Path.GetFullPath(destinationPath);
            if (string.Equals(
                    fullDestination,
                    QuarantinedSettingsPath,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Recovery export cannot overwrite the quarantined source file.",
                    nameof(destinationPath));
            }
            string? directory = Path.GetDirectoryName(fullDestination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.Copy(QuarantinedSettingsPath, fullDestination, overwrite: false);
        }

        public void ImportRecoveredSettingsJson(string json)
        {
            if (!RecoveryRequired)
            {
                throw new InvalidOperationException(
                    "Durable settings are healthy; recovery import is not active.");
            }
            Deserialize(
                json,
                out ReachyDurableSettings durable,
                out ReachySettingsStorageReferences references,
                out _);

            loading = true;
            try
            {
                Settings.ApplyDurableSettings(durable);
                References = references;
                RecoveryRequired = false;
                LastPersistenceFault = string.Empty;
                lastSerialized = string.Empty;
                PersistCurrent();
                settingsStatusMessage =
                    $"Durable settings were explicitly recovered at {persistencePath}.";
            }
            catch
            {
                RecoveryRequired = true;
                throw;
            }
            finally
            {
                loading = false;
            }
            PublishCombinedHealth();
        }

        public void ResetToDefaultsAfterCorruption()
        {
            if (!RecoveryRequired)
            {
                throw new InvalidOperationException(
                    "Durable settings are healthy; an explicit corruption reset is not required.");
            }

            loading = true;
            try
            {
                Settings.ApplyDurableSettings(new ReachyDurableSettings());
                References = ReachySettingsStorageReferences.Empty;
                RecoveryRequired = false;
                LastPersistenceFault = string.Empty;
                lastSerialized = string.Empty;
                PersistCurrent();
                settingsStatusMessage =
                    "Durable settings were explicitly reset after corruption; " +
                    $"the quarantined source remains at {QuarantinedSettingsPath}.";
            }
            catch
            {
                RecoveryRequired = true;
                throw;
            }
            finally
            {
                loading = false;
            }
            PublishCombinedHealth();
        }

        private void LoadOrCreateSettings()
        {
            if (!File.Exists(persistencePath))
            {
                PersistCurrent();
                settingsStatusMessage =
                    $"Durable settings initialized at {persistencePath}.";
                return;
            }

            loading = true;
            try
            {
                string json = File.ReadAllText(persistencePath);
                Deserialize(
                    json,
                    out ReachyDurableSettings durable,
                    out ReachySettingsStorageReferences references,
                    out int sourceSchemaVersion);
                Settings.ApplyDurableSettings(durable);
                References = references;
                lastSerialized = Serialize(
                    Settings.CaptureDurableSettings(),
                    References);
                LastPersistenceFault = string.Empty;
                RecoveryRequired = false;
                QuarantinedSettingsPath = string.Empty;

                if (sourceSchemaVersion == LegacySchemaVersion)
                {
                    lastSerialized = string.Empty;
                    PersistCurrent();
                    settingsStatusMessage =
                        $"Durable settings migrated from schema {LegacySchemaVersion} " +
                        $"to schema {CurrentSchemaVersion} at {persistencePath}.";
                }
                else
                {
                    settingsStatusMessage =
                        $"Durable settings restored from {persistencePath}.";
                }
            }
            catch (Exception primaryException)
            {
                string primaryQuarantine = QuarantineInvalidFile(persistencePath);
                QuarantinedSettingsPath = primaryQuarantine;
                string backupPath = persistencePath + ".bak";
                if (TryRestoreBackup(backupPath, primaryException, primaryQuarantine))
                {
                    return;
                }

                LastPersistenceFault = primaryException.Message;
                RecoveryRequired = true;
                settingsStatusMessage =
                    "Invalid durable settings were quarantined. Explicit recovery, " +
                    "recovery export, or reset is required; defaults were not persisted. " +
                    $"source={persistencePath}; quarantine={primaryQuarantine}; " +
                    $"error={primaryException.Message}";
            }
            finally
            {
                loading = false;
            }
        }

        private bool TryRestoreBackup(
            string backupPath,
            Exception primaryException,
            string primaryQuarantine)
        {
            if (!File.Exists(backupPath))
            {
                return false;
            }

            try
            {
                Deserialize(
                    File.ReadAllText(backupPath),
                    out ReachyDurableSettings durable,
                    out ReachySettingsStorageReferences references,
                    out int sourceSchemaVersion);
                Settings.ApplyDurableSettings(durable);
                References = references;
                RecoveryRequired = false;
                LastPersistenceFault = string.Empty;
                lastSerialized = string.Empty;
                PersistCurrent();
                settingsStatusMessage =
                    "Durable settings recovered from the validated atomic-write backup " +
                    $"after the primary file failed validation. source_schema={sourceSchemaVersion}; " +
                    $"quarantine={primaryQuarantine}.";
                return true;
            }
            catch (Exception backupException)
            {
                string backupQuarantine = QuarantineInvalidFile(backupPath);
                LastPersistenceFault =
                    $"Primary settings invalid: {primaryException.Message}; " +
                    $"backup invalid: {backupException.Message}";
                RecoveryRequired = true;
                settingsStatusMessage =
                    "Both durable settings and their atomic-write backup were invalid. " +
                    "Explicit recovery, recovery export, or reset is required; defaults " +
                    "were not persisted. " +
                    $"primary_quarantine={primaryQuarantine}; " +
                    $"backup_quarantine={backupQuarantine}.";
                return false;
            }
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            if (loading)
            {
                return;
            }
            if (RecoveryRequired)
            {
                settingsStatusMessage =
                    "Durable settings changed in memory but were not persisted because " +
                    "corruption recovery is still required.";
                PublishCombinedHealth();
                return;
            }
            try
            {
                PersistCurrent();
                bool recovered = !string.IsNullOrEmpty(LastPersistenceFault);
                LastPersistenceFault = string.Empty;
                settingsStatusMessage = recovered
                    ? $"Durable settings persistence recovered at {persistencePath}."
                    : $"Durable settings persisted at {persistencePath}.";
            }
            catch (Exception exception)
            {
                LastPersistenceFault = exception.Message;
                settingsStatusMessage =
                    $"Durable settings could not be saved to {persistencePath}: " +
                    exception.Message;
            }
            PublishCombinedHealth();
        }

        private void OnCameraCalibrationPersistenceChanged(
            object? sender,
            ReachyCameraCalibrationPersistenceChangedEventArgs eventArgs)
        {
            PublishCombinedHealth();
        }

        private void PublishCombinedHealth()
        {
            string calibrationStatus = cameraCalibrations.IsDegraded
                ? "Camera calibration persistence is degraded: " +
                    cameraCalibrations.LastPersistenceFault
                : $"Camera calibration profiles are stored at " +
                    $"{cameraCalibrations.PersistencePath}.";
            string settingsStatus = string.IsNullOrWhiteSpace(settingsStatusMessage)
                ? $"Durable settings are stored at {persistencePath}."
                : settingsStatusMessage;

            if (RecoveryRequired ||
                !string.IsNullOrEmpty(LastPersistenceFault) ||
                cameraCalibrations.IsDegraded)
            {
                SetDegraded(settingsStatus + " " + calibrationStatus);
            }
            else
            {
                SetReady(settingsStatus + " " + calibrationStatus);
            }
        }

        private void PersistCurrent()
        {
            if (RecoveryRequired)
            {
                throw new InvalidOperationException(
                    "Durable settings cannot be persisted until corruption recovery is explicitly resolved.");
            }

            string json = Serialize(
                Settings.CaptureDurableSettings(),
                References);
            if (string.Equals(json, lastSerialized, StringComparison.Ordinal) &&
                File.Exists(persistencePath))
            {
                return;
            }

            string? directory = Path.GetDirectoryName(persistencePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temporaryPath = persistencePath + ".tmp";
            string backupPath = persistencePath + ".bak";
            File.WriteAllText(temporaryPath, json);
            try
            {
                if (File.Exists(persistencePath))
                {
                    File.Copy(persistencePath, backupPath, overwrite: true);
                    File.Delete(persistencePath);
                }
                File.Move(temporaryPath, persistencePath);
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                lastSerialized = json;
            }
            catch
            {
                if (!File.Exists(persistencePath) && File.Exists(backupPath))
                {
                    File.Copy(backupPath, persistencePath, overwrite: true);
                }
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string QuarantineInvalidFile(string sourcePath)
        {
            string stamp = DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture);
            string quarantinePath = sourcePath + $".corrupt-{stamp}";
            File.Move(sourcePath, quarantinePath);
            return quarantinePath;
        }

        private static string GetDefaultCalibrationPath(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                throw new ArgumentException(
                    "Settings persistence requires a file path.",
                    nameof(settingsPath));
            }
            string fullSettingsPath = Path.GetFullPath(settingsPath);
            return Path.Combine(
                Path.GetDirectoryName(fullSettingsPath) ??
                    Application.persistentDataPath,
                ReachyCameraCalibrationPersistenceStore.CalibrationFileName);
        }

        private static void Deserialize(
            string json,
            out ReachyDurableSettings durable,
            out ReachySettingsStorageReferences references,
            out int sourceSchemaVersion)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(
                    "The settings file is empty.");
            }

            SettingsSchemaHeader header =
                JsonUtility.FromJson<SettingsSchemaHeader>(json) ??
                throw new InvalidDataException(
                    "The settings file did not contain a JSON object.");
            sourceSchemaVersion = header.schemaVersion;
            switch (sourceSchemaVersion)
            {
                case LegacySchemaVersion:
                {
                    LegacySettingsEnvelope envelope =
                        JsonUtility.FromJson<LegacySettingsEnvelope>(json) ??
                        throw new InvalidDataException(
                            "The legacy settings file did not contain a JSON object.");
                    durable = envelope.ToDurableSettings();
                    references = ReachySettingsStorageReferences.Empty;
                    return;
                }
                case CurrentSchemaVersion:
                {
                    SettingsEnvelope envelope =
                        JsonUtility.FromJson<SettingsEnvelope>(json) ??
                        throw new InvalidDataException(
                            "The settings file did not contain a JSON object.");
                    durable = envelope.ToDurableSettings();
                    references = (envelope.references ?? new SettingsReferencesEnvelope())
                        .ToReferences();
                    return;
                }
                default:
                    throw new InvalidDataException(
                        $"Unsupported settings schema {sourceSchemaVersion}; " +
                        $"supported schemas are {LegacySchemaVersion} and {CurrentSchemaVersion}.");
            }
        }

        private static string Serialize(
            ReachyDurableSettings settings,
            ReachySettingsStorageReferences references)
        {
            return JsonUtility.ToJson(
                SettingsEnvelope.FromDurableSettings(settings, references),
                prettyPrint: true);
        }

        [Serializable]
        private sealed class SettingsSchemaHeader
        {
            public int schemaVersion;
        }

        [Serializable]
        private sealed class LegacySettingsEnvelope
        {
            public int schemaVersion = LegacySchemaVersion;
            public int asrExecution;
            public int ttsExecution;
            public int llmExecution;
            public int vlmExecution;
            public int preferredCameraFacing;
            public string speechLanguage = "System default";
            public string speechVoice = "System default";
            public int localModelMemoryBudgetMb = 1024;
            public int localModelContextTokens = 4096;
            public int simulationFidelity;
            public bool historyEnabled;
            public int retentionDays = 30;

            public ReachyDurableSettings ToDurableSettings()
            {
                return new ReachyDurableSettings
                {
                    AsrExecution = asrExecution,
                    TtsExecution = ttsExecution,
                    LlmExecution = llmExecution,
                    VlmExecution = vlmExecution,
                    PreferredCameraFacing = preferredCameraFacing,
                    SpeechLanguage = speechLanguage,
                    SpeechVoice = speechVoice,
                    LocalModelMemoryBudgetMb = localModelMemoryBudgetMb,
                    LocalModelContextTokens = localModelContextTokens,
                    SimulationFidelity = simulationFidelity,
                    HistoryEnabled = historyEnabled,
                    RetentionDays = retentionDays,
                };
            }
        }

        [Serializable]
        private sealed class SettingsEnvelope
        {
            public int schemaVersion = CurrentSchemaVersion;
            public int asrExecution;
            public int ttsExecution;
            public int llmExecution;
            public int vlmExecution;
            public int preferredCameraFacing;
            public string speechLanguage = "System default";
            public string speechVoice = "System default";
            public int localModelMemoryBudgetMb = 1024;
            public int localModelContextTokens = 4096;
            public int simulationFidelity;
            public bool historyEnabled;
            public int retentionDays = 30;
            public SettingsReferencesEnvelope? references;

            public ReachyDurableSettings ToDurableSettings()
            {
                return new ReachyDurableSettings
                {
                    AsrExecution = asrExecution,
                    TtsExecution = ttsExecution,
                    LlmExecution = llmExecution,
                    VlmExecution = vlmExecution,
                    PreferredCameraFacing = preferredCameraFacing,
                    SpeechLanguage = speechLanguage,
                    SpeechVoice = speechVoice,
                    LocalModelMemoryBudgetMb = localModelMemoryBudgetMb,
                    LocalModelContextTokens = localModelContextTokens,
                    SimulationFidelity = simulationFidelity,
                    HistoryEnabled = historyEnabled,
                    RetentionDays = retentionDays,
                };
            }

            public static SettingsEnvelope FromDurableSettings(
                ReachyDurableSettings settings,
                ReachySettingsStorageReferences references)
            {
                if (settings == null)
                {
                    throw new ArgumentNullException(nameof(settings));
                }
                if (references == null)
                {
                    throw new ArgumentNullException(nameof(references));
                }
                return new SettingsEnvelope
                {
                    schemaVersion = CurrentSchemaVersion,
                    asrExecution = settings.AsrExecution,
                    ttsExecution = settings.TtsExecution,
                    llmExecution = settings.LlmExecution,
                    vlmExecution = settings.VlmExecution,
                    preferredCameraFacing = settings.PreferredCameraFacing,
                    speechLanguage = settings.SpeechLanguage,
                    speechVoice = settings.SpeechVoice,
                    localModelMemoryBudgetMb =
                        settings.LocalModelMemoryBudgetMb,
                    localModelContextTokens =
                        settings.LocalModelContextTokens,
                    simulationFidelity = settings.SimulationFidelity,
                    historyEnabled = settings.HistoryEnabled,
                    retentionDays = settings.RetentionDays,
                    references = SettingsReferencesEnvelope.FromReferences(
                        references),
                };
            }
        }

        [Serializable]
        private sealed class SettingsReferencesEnvelope
        {
            public string asrProviderProfileId = string.Empty;
            public string ttsProviderProfileId = string.Empty;
            public string llmProviderProfileId = string.Empty;
            public string vlmProviderProfileId = string.Empty;
            public string cameraCalibrationProfileId = string.Empty;
            public string modelManifestId = string.Empty;
            public string modelManifestSha256 = string.Empty;
            public string deviceProfileId = string.Empty;

            public ReachySettingsStorageReferences ToReferences()
            {
                return new ReachySettingsStorageReferences(
                    asrProviderProfileId,
                    ttsProviderProfileId,
                    llmProviderProfileId,
                    vlmProviderProfileId,
                    cameraCalibrationProfileId,
                    modelManifestId,
                    modelManifestSha256,
                    deviceProfileId);
            }

            public static SettingsReferencesEnvelope FromReferences(
                ReachySettingsStorageReferences references)
            {
                return new SettingsReferencesEnvelope
                {
                    asrProviderProfileId = references.AsrProviderProfileId,
                    ttsProviderProfileId = references.TtsProviderProfileId,
                    llmProviderProfileId = references.LlmProviderProfileId,
                    vlmProviderProfileId = references.VlmProviderProfileId,
                    cameraCalibrationProfileId =
                        references.CameraCalibrationProfileId,
                    modelManifestId = references.ModelManifestId,
                    modelManifestSha256 = references.ModelManifestSha256,
                    deviceProfileId = references.DeviceProfileId,
                };
            }
        }
    }
}

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

        private const int SchemaVersion = 1;
        private readonly string persistencePath;
        private bool loading;
        private string lastSerialized = string.Empty;

        public ReachySettingsPersistenceApplicationService()
            : this(Path.Combine(
                Application.persistentDataPath,
                SettingsFileName))
        {
        }

        public ReachySettingsPersistenceApplicationService(string settingsPath)
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
            persistencePath = Path.GetFullPath(settingsPath);
        }

        public ReachySettingsStateStore Settings { get; } =
            new ReachySettingsStateStore();

        public string PersistencePath => persistencePath;

        public string LastPersistenceFault { get; private set; } = string.Empty;

        protected override void OnInitialize()
        {
            Settings.Changed += OnSettingsChanged;
            if (!File.Exists(persistencePath))
            {
                PersistCurrent();
                SetReady(
                    $"Durable settings initialized at {persistencePath}.");
                return;
            }

            loading = true;
            try
            {
                string json = File.ReadAllText(persistencePath);
                SettingsEnvelope envelope = JsonUtility.FromJson<SettingsEnvelope>(json) ??
                    throw new InvalidDataException(
                        "The settings file did not contain a JSON object.");
                if (envelope.schemaVersion != SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Unsupported settings schema {envelope.schemaVersion}; " +
                        $"expected {SchemaVersion}.");
                }
                Settings.ApplyDurableSettings(envelope.ToDurableSettings());
                lastSerialized = Serialize(Settings.CaptureDurableSettings());
                LastPersistenceFault = string.Empty;
                SetReady(
                    $"Durable settings restored from {persistencePath}.");
            }
            catch (Exception exception)
            {
                string quarantinePath = QuarantineInvalidFile();
                LastPersistenceFault = exception.Message;
                PersistCurrent();
                SetDegraded(
                    "Invalid durable settings were quarantined and safe defaults " +
                    $"were restored. source={persistencePath}; " +
                    $"quarantine={quarantinePath}; error={exception.Message}");
            }
            finally
            {
                loading = false;
            }
        }

        protected override void OnDispose()
        {
            Settings.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(
            object? sender,
            ReachySettingsChangedEventArgs eventArgs)
        {
            if (loading)
            {
                return;
            }
            try
            {
                PersistCurrent();
                if (!string.IsNullOrEmpty(LastPersistenceFault))
                {
                    LastPersistenceFault = string.Empty;
                    SetReady(
                        $"Durable settings persistence recovered at {persistencePath}.");
                }
            }
            catch (Exception exception)
            {
                LastPersistenceFault = exception.Message;
                SetDegraded(
                    $"Durable settings could not be saved to {persistencePath}: " +
                    exception.Message);
            }
        }

        private void PersistCurrent()
        {
            string json = Serialize(Settings.CaptureDurableSettings());
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

        private string QuarantineInvalidFile()
        {
            string stamp = DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture);
            string quarantinePath =
                persistencePath + $".corrupt-{stamp}";
            File.Move(persistencePath, quarantinePath);
            return quarantinePath;
        }

        private static string Serialize(ReachyDurableSettings settings)
        {
            return JsonUtility.ToJson(
                SettingsEnvelope.FromDurableSettings(settings),
                prettyPrint: true);
        }

        [Serializable]
        private sealed class SettingsEnvelope
        {
            public int schemaVersion = SchemaVersion;
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

            public static SettingsEnvelope FromDurableSettings(
                ReachyDurableSettings settings)
            {
                if (settings == null)
                {
                    throw new ArgumentNullException(nameof(settings));
                }
                return new SettingsEnvelope
                {
                    schemaVersion = SchemaVersion,
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
                };
            }
        }
    }
}

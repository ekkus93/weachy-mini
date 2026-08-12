#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
    public sealed class ReachySettingsSnapshot
    {
        private readonly ReachyProviderSelection[] providers;

        public ReachySettingsSnapshot(
            ReachySettingsSection activeSection,
            ReachyProviderSelection[] providerSelections,
            ReachyCameraFacing preferredCameraFacing,
            string cameraCalibrationProfile,
            string reprojectionStatus,
            string speechLanguage,
            string speechVoice,
            string speechNetworkStatus,
            int localModelCount,
            string activeLocalModel,
            int localModelMemoryBudgetMb,
            int localModelContextTokens,
            ReachySimulationFidelity simulationFidelity,
            string simulationCalibrationProfile,
            string simulationDiagnostics,
            bool historyEnabled,
            int retentionDays,
            string privacyCloudSummary,
            string statusMessage,
            ulong revision)
        {
            if (providerSelections == null ||
                providerSelections.Length != Enum.GetValues(
                    typeof(ReachyProviderKind)).Length)
            {
                throw new ArgumentException(
                    "Settings require exactly one selection per provider kind.",
                    nameof(providerSelections));
            }
            providers = new ReachyProviderSelection[providerSelections.Length];
            for (int index = 0; index < providerSelections.Length; ++index)
            {
                ReachyProviderSelection provider = providerSelections[index] ??
                    throw new ArgumentException(
                        "Provider selections cannot contain null.",
                        nameof(providerSelections));
                if ((int)provider.Kind != index)
                {
                    throw new ArgumentException(
                        "Provider selections must use canonical kind order.",
                        nameof(providerSelections));
                }
                providers[index] = provider;
            }
            if (string.IsNullOrWhiteSpace(cameraCalibrationProfile) ||
                string.IsNullOrWhiteSpace(reprojectionStatus) ||
                string.IsNullOrWhiteSpace(speechLanguage) ||
                string.IsNullOrWhiteSpace(speechVoice) ||
                string.IsNullOrWhiteSpace(speechNetworkStatus) ||
                string.IsNullOrWhiteSpace(activeLocalModel) ||
                string.IsNullOrWhiteSpace(simulationCalibrationProfile) ||
                string.IsNullOrWhiteSpace(simulationDiagnostics) ||
                string.IsNullOrWhiteSpace(privacyCloudSummary) ||
                string.IsNullOrWhiteSpace(statusMessage))
            {
                throw new ArgumentException(
                    "Settings snapshot labels and diagnostics must be present.");
            }
            if (localModelCount < 0 ||
                localModelMemoryBudgetMb < 256 ||
                localModelContextTokens < 1024 ||
                retentionDays < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localModelCount),
                    "Settings numeric values are outside supported bounds.");
            }

            ActiveSection = activeSection;
            PreferredCameraFacing = preferredCameraFacing;
            CameraCalibrationProfile = cameraCalibrationProfile;
            ReprojectionStatus = reprojectionStatus;
            SpeechLanguage = speechLanguage;
            SpeechVoice = speechVoice;
            SpeechNetworkStatus = speechNetworkStatus;
            LocalModelCount = localModelCount;
            ActiveLocalModel = activeLocalModel;
            LocalModelMemoryBudgetMb = localModelMemoryBudgetMb;
            LocalModelContextTokens = localModelContextTokens;
            SimulationFidelity = simulationFidelity;
            SimulationCalibrationProfile = simulationCalibrationProfile;
            SimulationDiagnostics = simulationDiagnostics;
            HistoryEnabled = historyEnabled;
            RetentionDays = retentionDays;
            PrivacyCloudSummary = privacyCloudSummary;
            StatusMessage = statusMessage;
            Revision = revision;
        }

        public ReachySettingsSection ActiveSection { get; }

        public IReadOnlyList<ReachyProviderSelection> ProviderSelections =>
            Array.AsReadOnly(providers);

        public ReachyProviderSelection[] CopyProviderSelections()
        {
            ReachyProviderSelection[] copy =
                new ReachyProviderSelection[providers.Length];
            Array.Copy(providers, copy, providers.Length);
            return copy;
        }

        public ReachyProviderSelection GetProvider(ReachyProviderKind kind)
        {
            return providers[(int)kind];
        }

        public ReachyCameraFacing PreferredCameraFacing { get; }

        public string CameraCalibrationProfile { get; }

        public string ReprojectionStatus { get; }

        public string SpeechLanguage { get; }

        public string SpeechVoice { get; }

        public string SpeechNetworkStatus { get; }

        public int LocalModelCount { get; }

        public string ActiveLocalModel { get; }

        public int LocalModelMemoryBudgetMb { get; }

        public int LocalModelContextTokens { get; }

        public ReachySimulationFidelity SimulationFidelity { get; }

        public string SimulationCalibrationProfile { get; }

        public string SimulationDiagnostics { get; }

        public bool HistoryEnabled { get; }

        public int RetentionDays { get; }

        public string PrivacyCloudSummary { get; }

        public string StatusMessage { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachySettingsChangedEventArgs : EventArgs
    {
        public ReachySettingsChangedEventArgs(ReachySettingsSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachySettingsSnapshot Snapshot { get; }
    }
}

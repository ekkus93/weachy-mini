#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
        public void SelectSection(ReachySettingsSection section)
        {
            Publish(
                activeSection: section,
                statusMessage: $"{GetSectionLabel(section)} settings opened.");
        }

        public void CycleProvider(ReachyProviderKind kind)
        {
            ReachyProviderSelection[] providers = current.CopyProviderSelections();
            ReachyProviderSelection next = NextProvider(providers[(int)kind]);
            providers[(int)kind] = next;
            Publish(
                providers: providers,
                speechNetworkStatus: BuildSpeechNetworkStatus(providers, current.SpeechVoice),
                privacyCloudSummary: BuildPrivacySummary(providers),
                statusMessage:
                    $"{GetProviderKindLabel(kind)} preference set to {next.DisplayName}. {next.Status}");
        }

        public void CyclePreferredCameraFacing()
        {
            ReachyCameraFacing next = current.PreferredCameraFacing switch
            {
                ReachyCameraFacing.Unconfigured => ReachyCameraFacing.Front,
                ReachyCameraFacing.Front => ReachyCameraFacing.Rear,
                ReachyCameraFacing.Rear => ReachyCameraFacing.Unconfigured,
                _ => ReachyCameraFacing.Unconfigured,
            };
            Publish(
                preferredCameraFacing: next,
                statusMessage:
                    $"Preferred device camera set to {GetCameraFacingLabel(next)}. " +
                    "CameraX acquisition remains unavailable until RMA-090.");
        }

        public void CycleSpeechLanguage()
        {
            string next = NextString(SpeechLanguages, current.SpeechLanguage);
            Publish(
                speechLanguage: next,
                statusMessage: $"Speech language preference set to {next}.");
        }

        public void CycleSpeechVoice()
        {
            string next = NextString(SpeechVoices, current.SpeechVoice);
            Publish(
                speechVoice: next,
                speechNetworkStatus:
                    BuildSpeechNetworkStatus(current.CopyProviderSelections(), next),
                statusMessage:
                    $"Speech voice preference set to {next}. " +
                    BuildSpeechNetworkStatus(current.CopyProviderSelections(), next));
        }

        public void CycleLocalModelMemoryBudget()
        {
            int next = NextInt(
                MemoryBudgetsMb,
                current.LocalModelMemoryBudgetMb);
            Publish(
                localModelMemoryBudgetMb: next,
                statusMessage: $"Local-model memory budget set to {next} MB.");
        }

        public void CycleLocalModelContextLength()
        {
            int next = NextInt(
                ContextLengths,
                current.LocalModelContextTokens);
            Publish(
                localModelContextTokens: next,
                statusMessage:
                    $"Local-model context preference set to {next} tokens.");
        }

        public void CycleSimulationFidelity()
        {
            ReachySimulationFidelity next =
                current.SimulationFidelity ==
                ReachySimulationFidelity.Standard
                    ? ReachySimulationFidelity.HighFidelity
                    : ReachySimulationFidelity.Standard;
            Publish(
                simulationFidelity: next,
                statusMessage:
                    $"Simulation fidelity preference set to {GetSimulationFidelityLabel(next)}.");
        }

        public void ToggleHistory()
        {
            bool next = !current.HistoryEnabled;
            Publish(
                historyEnabled: next,
                statusMessage: next
                    ? $"History enabled with {current.RetentionDays}-day retention."
                    : "History disabled; new interaction history will not be retained.");
        }

        public void CycleRetentionDays()
        {
            int next = NextInt(RetentionPeriods, current.RetentionDays);
            Publish(
                retentionDays: next,
                statusMessage: next == 0
                    ? "Retention set to session only."
                    : $"Retention set to {next} days.");
        }

        public void SetSimulationDiagnostics(string diagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostics))
            {
                throw new ArgumentException(
                    "Simulation diagnostics must be present.",
                    nameof(diagnostics));
            }
            Publish(
                simulationDiagnostics: diagnostics,
                statusMessage: "Simulation diagnostics refreshed.");
        }

        public void ReportUnavailableAction(string action, string explanation)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException(
                    "A settings action requires a name.",
                    nameof(action));
            }
            if (string.IsNullOrWhiteSpace(explanation))
            {
                throw new ArgumentException(
                    "A settings action requires an explanation.",
                    nameof(explanation));
            }
            Publish(statusMessage: $"{action} unavailable: {explanation}");
        }

        public void ReportSimulationReset(bool succeeded, string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "Simulation reset diagnostics must be present.",
                    nameof(detail));
            }
            Publish(
                statusMessage: succeeded
                    ? $"Simulation reset completed: {detail}"
                    : $"Simulation reset failed: {detail}");
        }

        private void Publish(
            ReachySettingsSection? activeSection = null,
            ReachyProviderSelection[]? providers = null,
            ReachyCameraFacing? preferredCameraFacing = null,
            string? cameraCalibrationProfile = null,
            string? reprojectionStatus = null,
            string? speechLanguage = null,
            string? speechVoice = null,
            string? speechNetworkStatus = null,
            int? localModelCount = null,
            string? activeLocalModel = null,
            int? localModelMemoryBudgetMb = null,
            int? localModelContextTokens = null,
            ReachySimulationFidelity? simulationFidelity = null,
            string? simulationCalibrationProfile = null,
            string? simulationDiagnostics = null,
            bool? historyEnabled = null,
            int? retentionDays = null,
            string? privacyCloudSummary = null,
            string? statusMessage = null)
        {
            ReachySettingsSnapshot next = CreateSnapshot(
                activeSection ?? current.ActiveSection,
                providers ?? current.CopyProviderSelections(),
                preferredCameraFacing ?? current.PreferredCameraFacing,
                cameraCalibrationProfile ?? current.CameraCalibrationProfile,
                reprojectionStatus ?? current.ReprojectionStatus,
                speechLanguage ?? current.SpeechLanguage,
                speechVoice ?? current.SpeechVoice,
                speechNetworkStatus ?? current.SpeechNetworkStatus,
                localModelCount ?? current.LocalModelCount,
                activeLocalModel ?? current.ActiveLocalModel,
                localModelMemoryBudgetMb ?? current.LocalModelMemoryBudgetMb,
                localModelContextTokens ?? current.LocalModelContextTokens,
                simulationFidelity ?? current.SimulationFidelity,
                simulationCalibrationProfile ??
                    current.SimulationCalibrationProfile,
                simulationDiagnostics ?? current.SimulationDiagnostics,
                historyEnabled ?? current.HistoryEnabled,
                retentionDays ?? current.RetentionDays,
                privacyCloudSummary ?? current.PrivacyCloudSummary,
                statusMessage ?? current.StatusMessage,
                checked(current.Revision + 1UL));
            current = next;
            Changed?.Invoke(this, new ReachySettingsChangedEventArgs(next));
        }

        private static ReachySettingsSnapshot CreateSnapshot(
            ReachySettingsSection activeSection,
            ReachyProviderSelection[] providers,
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
            return new ReachySettingsSnapshot(
                activeSection,
                providers,
                preferredCameraFacing,
                cameraCalibrationProfile,
                reprojectionStatus,
                speechLanguage,
                speechVoice,
                speechNetworkStatus,
                localModelCount,
                activeLocalModel,
                localModelMemoryBudgetMb,
                localModelContextTokens,
                simulationFidelity,
                simulationCalibrationProfile,
                simulationDiagnostics,
                historyEnabled,
                retentionDays,
                privacyCloudSummary,
                statusMessage,
                revision);
        }
    }
}

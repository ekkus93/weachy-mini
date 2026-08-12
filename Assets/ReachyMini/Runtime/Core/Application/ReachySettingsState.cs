#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
        private static readonly string[] SpeechLanguages =
        {
            "System default",
            "English (United States)",
            "Spanish (United States)",
            "French (France)",
        };

        private static readonly string[] SpeechVoices =
        {
            "System default",
            "On-device voice preference",
            "Network voice preference",
        };

        private static readonly int[] MemoryBudgetsMb =
        {
            512,
            1024,
            2048,
            4096,
        };

        private static readonly int[] ContextLengths =
        {
            2048,
            4096,
            8192,
            16384,
        };

        private static readonly int[] RetentionPeriods =
        {
            0,
            7,
            30,
            90,
        };

        private ReachySettingsSnapshot current;

        public ReachySettingsStateStore()
        {
            ReachyProviderSelection[] providers = CreateDefaultProviders();
            current = CreateSnapshot(
                ReachySettingsSection.Providers,
                providers,
                ReachyCameraFacing.Unconfigured,
                "Not configured",
                "Unavailable until CameraX calibration support is installed.",
                SpeechLanguages[0],
                SpeechVoices[0],
                "No speech provider is configured.",
                0,
                "No local model installed",
                1024,
                4096,
                ReachySimulationFidelity.Standard,
                "Uncalibrated",
                "Authoritative runtime diagnostics are available from the diagnostics panel.",
                false,
                30,
                BuildPrivacySummary(providers),
                "Settings loaded with no providers configured.",
                0UL);
        }

        public ReachySettingsSnapshot Current => current;

        public event EventHandler<ReachySettingsChangedEventArgs>? Changed;

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

        public ReachyDurableSettings CaptureDurableSettings()
        {
            return new ReachyDurableSettings
            {
                AsrExecution = (int)current.GetProvider(
                    ReachyProviderKind.Asr).Execution,
                TtsExecution = (int)current.GetProvider(
                    ReachyProviderKind.Tts).Execution,
                LlmExecution = (int)current.GetProvider(
                    ReachyProviderKind.Llm).Execution,
                VlmExecution = (int)current.GetProvider(
                    ReachyProviderKind.Vlm).Execution,
                PreferredCameraFacing = (int)current.PreferredCameraFacing,
                SpeechLanguage = current.SpeechLanguage,
                SpeechVoice = current.SpeechVoice,
                LocalModelMemoryBudgetMb = current.LocalModelMemoryBudgetMb,
                LocalModelContextTokens = current.LocalModelContextTokens,
                SimulationFidelity = (int)current.SimulationFidelity,
                HistoryEnabled = current.HistoryEnabled,
                RetentionDays = current.RetentionDays,
            };
        }

        public void ApplyDurableSettings(ReachyDurableSettings durable)
        {
            if (durable == null)
            {
                throw new ArgumentNullException(nameof(durable));
            }

            ReachyProviderSelection[] providers =
            {
                BuildProvider(
                    ReachyProviderKind.Asr,
                    SanitizeProviderExecution(durable.AsrExecution)),
                BuildProvider(
                    ReachyProviderKind.Tts,
                    SanitizeProviderExecution(durable.TtsExecution)),
                BuildProvider(
                    ReachyProviderKind.Llm,
                    SanitizeProviderExecution(
                        durable.LlmExecution,
                        allowAndroidService: false)),
                BuildProvider(
                    ReachyProviderKind.Vlm,
                    SanitizeProviderExecution(
                        durable.VlmExecution,
                        allowAndroidService: false)),
            };
            ReachyCameraFacing cameraFacing =
                Enum.IsDefined(
                    typeof(ReachyCameraFacing),
                    durable.PreferredCameraFacing)
                    ? (ReachyCameraFacing)durable.PreferredCameraFacing
                    : ReachyCameraFacing.Unconfigured;
            ReachySimulationFidelity fidelity =
                Enum.IsDefined(
                    typeof(ReachySimulationFidelity),
                    durable.SimulationFidelity)
                    ? (ReachySimulationFidelity)durable.SimulationFidelity
                    : ReachySimulationFidelity.Standard;
            string language = Contains(
                SpeechLanguages,
                durable.SpeechLanguage)
                    ? durable.SpeechLanguage
                    : SpeechLanguages[0];
            string voice = Contains(
                SpeechVoices,
                durable.SpeechVoice)
                    ? durable.SpeechVoice
                    : SpeechVoices[0];
            int memory = Contains(
                MemoryBudgetsMb,
                durable.LocalModelMemoryBudgetMb)
                    ? durable.LocalModelMemoryBudgetMb
                    : 1024;
            int context = Contains(
                ContextLengths,
                durable.LocalModelContextTokens)
                    ? durable.LocalModelContextTokens
                    : 4096;
            int retention = Contains(
                RetentionPeriods,
                durable.RetentionDays)
                    ? durable.RetentionDays
                    : 30;

            Publish(
                providers: providers,
                preferredCameraFacing: cameraFacing,
                speechLanguage: language,
                speechVoice: voice,
                speechNetworkStatus: BuildSpeechNetworkStatus(providers, voice),
                localModelMemoryBudgetMb: memory,
                localModelContextTokens: context,
                simulationFidelity: fidelity,
                historyEnabled: durable.HistoryEnabled,
                retentionDays: retention,
                privacyCloudSummary: BuildPrivacySummary(providers),
                statusMessage: "Durable settings restored.");
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

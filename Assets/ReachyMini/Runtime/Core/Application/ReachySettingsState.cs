#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
    public sealed class ReachyProviderSelection
    {
        public ReachyProviderSelection(
            ReachyProviderKind kind,
            string providerId,
            string displayName,
            ReachyProviderExecution execution,
            ReachyConnectivityRequirement connectivity,
            bool available,
            string status)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "A provider selection requires an identifier.",
                    nameof(providerId));
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A provider selection requires a display name.",
                    nameof(displayName));
            }
            if (string.IsNullOrWhiteSpace(status))
            {
                throw new ArgumentException(
                    "A provider selection requires a status explanation.",
                    nameof(status));
            }
            if (execution == ReachyProviderExecution.Unconfigured &&
                connectivity != ReachyConnectivityRequirement.Unavailable)
            {
                throw new ArgumentException(
                    "An unconfigured provider cannot claim connectivity capability.",
                    nameof(connectivity));
            }
            if ((execution == ReachyProviderExecution.AndroidService ||
                 execution == ReachyProviderExecution.Cloud) &&
                connectivity != ReachyConnectivityRequirement.NetworkRequired)
            {
                throw new ArgumentException(
                    "Network-backed provider selections must be labeled network required.",
                    nameof(connectivity));
            }

            Kind = kind;
            ProviderId = providerId;
            DisplayName = displayName;
            Execution = execution;
            Connectivity = connectivity;
            Available = available;
            Status = status;
        }

        public ReachyProviderKind Kind { get; }

        public string ProviderId { get; }

        public string DisplayName { get; }

        public ReachyProviderExecution Execution { get; }

        public ReachyConnectivityRequirement Connectivity { get; }

        public bool Available { get; }

        public string Status { get; }

        public bool SendsDataOffDevice =>
            Execution == ReachyProviderExecution.AndroidService ||
            Execution == ReachyProviderExecution.Cloud ||
            Connectivity == ReachyConnectivityRequirement.NetworkRequired;
    }

    public sealed class ReachyLicenseNotice
    {
        public ReachyLicenseNotice(
            string component,
            string attribution,
            string licenseReference)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                throw new ArgumentException(
                    "A license notice requires a component.",
                    nameof(component));
            }
            if (string.IsNullOrWhiteSpace(attribution))
            {
                throw new ArgumentException(
                    "A license notice requires attribution.",
                    nameof(attribution));
            }
            if (string.IsNullOrWhiteSpace(licenseReference))
            {
                throw new ArgumentException(
                    "A license notice requires a license reference.",
                    nameof(licenseReference));
            }

            Component = component;
            Attribution = attribution;
            LicenseReference = licenseReference;
        }

        public string Component { get; }

        public string Attribution { get; }

        public string LicenseReference { get; }
    }

    public sealed class ReachyDurableSettings
    {
        public int AsrExecution { get; set; }

        public int TtsExecution { get; set; }

        public int LlmExecution { get; set; }

        public int VlmExecution { get; set; }

        public int PreferredCameraFacing { get; set; }

        public string SpeechLanguage { get; set; } = "System default";

        public string SpeechVoice { get; set; } = "System default";

        public int LocalModelMemoryBudgetMb { get; set; } = 1024;

        public int LocalModelContextTokens { get; set; } = 4096;

        public int SimulationFidelity { get; set; }

        public bool HistoryEnabled { get; set; }

        public int RetentionDays { get; set; } = 30;
    }

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

    public sealed class ReachySettingsStateStore
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

        public static string GetSectionLabel(ReachySettingsSection section)
        {
            return section switch
            {
                ReachySettingsSection.Providers => "Providers",
                ReachySettingsSection.Camera => "Camera",
                ReachySettingsSection.Speech => "Speech",
                ReachySettingsSection.LocalModel => "Local model",
                ReachySettingsSection.Simulation => "Simulation",
                ReachySettingsSection.Privacy => "Privacy",
                ReachySettingsSection.Licenses => "Licenses",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(section),
                    section,
                    null),
            };
        }

        public static string GetProviderKindLabel(ReachyProviderKind kind)
        {
            return kind switch
            {
                ReachyProviderKind.Asr => "ASR",
                ReachyProviderKind.Tts => "TTS",
                ReachyProviderKind.Llm => "LLM",
                ReachyProviderKind.Vlm => "VLM",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
        }

        public static string GetExecutionLabel(ReachyProviderExecution execution)
        {
            return execution switch
            {
                ReachyProviderExecution.Unconfigured => "Not configured",
                ReachyProviderExecution.OnDevice => "On device",
                ReachyProviderExecution.AndroidService => "Android service",
                ReachyProviderExecution.Cloud => "Cloud",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(execution),
                    execution,
                    null),
            };
        }

        public static string GetConnectivityLabel(
            ReachyConnectivityRequirement connectivity)
        {
            return connectivity switch
            {
                ReachyConnectivityRequirement.Unavailable => "Unavailable",
                ReachyConnectivityRequirement.OfflineCapable => "Offline capable",
                ReachyConnectivityRequirement.NetworkRequired => "Network required",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(connectivity),
                    connectivity,
                    null),
            };
        }

        public static string GetCameraFacingLabel(ReachyCameraFacing facing)
        {
            return facing switch
            {
                ReachyCameraFacing.Unconfigured => "Not configured",
                ReachyCameraFacing.Front => "Front",
                ReachyCameraFacing.Rear => "Rear",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(facing),
                    facing,
                    null),
            };
        }

        public static string GetSimulationFidelityLabel(
            ReachySimulationFidelity fidelity)
        {
            return fidelity switch
            {
                ReachySimulationFidelity.Standard => "Standard",
                ReachySimulationFidelity.HighFidelity => "High fidelity",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(fidelity),
                    fidelity,
                    null),
            };
        }

        public static ReachyLicenseNotice[] GetLicenseNotices()
        {
            return new[]
            {
                new ReachyLicenseNotice(
                    "Weachy Mini",
                    "Project contributors",
                    "Repository LICENSE"),
                new ReachyLicenseNotice(
                    "Reachy Mini model and robot identity",
                    "Pollen Robotics",
                    "Upstream Reachy Mini notices and license"),
                new ReachyLicenseNotice(
                    "MuJoCo simulation runtime",
                    "Google DeepMind",
                    "MuJoCo Apache-2.0 license and notices"),
                new ReachyLicenseNotice(
                    "Unity runtime",
                    "Unity Technologies",
                    "Unity software terms and third-party notices"),
            };
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

        private static ReachyProviderSelection[] CreateDefaultProviders()
        {
            return new[]
            {
                BuildProvider(
                    ReachyProviderKind.Asr,
                    ReachyProviderExecution.Unconfigured),
                BuildProvider(
                    ReachyProviderKind.Tts,
                    ReachyProviderExecution.Unconfigured),
                BuildProvider(
                    ReachyProviderKind.Llm,
                    ReachyProviderExecution.Unconfigured),
                BuildProvider(
                    ReachyProviderKind.Vlm,
                    ReachyProviderExecution.Unconfigured),
            };
        }

        private static ReachyProviderSelection NextProvider(
            ReachyProviderSelection currentSelection)
        {
            ReachyProviderExecution next;
            if (currentSelection.Kind == ReachyProviderKind.Asr ||
                currentSelection.Kind == ReachyProviderKind.Tts)
            {
                next = currentSelection.Execution switch
                {
                    ReachyProviderExecution.Unconfigured =>
                        ReachyProviderExecution.OnDevice,
                    ReachyProviderExecution.OnDevice =>
                        ReachyProviderExecution.AndroidService,
                    ReachyProviderExecution.AndroidService =>
                        ReachyProviderExecution.Cloud,
                    _ => ReachyProviderExecution.Unconfigured,
                };
            }
            else
            {
                next = currentSelection.Execution switch
                {
                    ReachyProviderExecution.Unconfigured =>
                        ReachyProviderExecution.OnDevice,
                    ReachyProviderExecution.OnDevice =>
                        ReachyProviderExecution.Cloud,
                    _ => ReachyProviderExecution.Unconfigured,
                };
            }
            return BuildProvider(currentSelection.Kind, next);
        }

        private static ReachyProviderSelection BuildProvider(
            ReachyProviderKind kind,
            ReachyProviderExecution execution)
        {
            string kindLabel = GetProviderKindLabel(kind);
            return execution switch
            {
                ReachyProviderExecution.Unconfigured =>
                    new ReachyProviderSelection(
                        kind,
                        $"{kindLabel.ToLowerInvariant()}-unconfigured",
                        "Not configured",
                        execution,
                        ReachyConnectivityRequirement.Unavailable,
                        false,
                        $"{kindLabel} has no selected provider."),
                ReachyProviderExecution.OnDevice =>
                    new ReachyProviderSelection(
                        kind,
                        $"{kindLabel.ToLowerInvariant()}-local-preference",
                        "Local model preference",
                        execution,
                        ReachyConnectivityRequirement.OfflineCapable,
                        false,
                        "The preference is stored, but no compatible local model is installed."),
                ReachyProviderExecution.AndroidService =>
                    new ReachyProviderSelection(
                        kind,
                        $"{kindLabel.ToLowerInvariant()}-android-service",
                        "Android service preference",
                        execution,
                        ReachyConnectivityRequirement.NetworkRequired,
                        false,
                        "The Android service preference is stored but not integrated. " +
                        "The selected service may send data over the network and is never labeled offline."),
                ReachyProviderExecution.Cloud =>
                    new ReachyProviderSelection(
                        kind,
                        $"{kindLabel.ToLowerInvariant()}-cloud",
                        "Cloud API preference",
                        execution,
                        ReachyConnectivityRequirement.NetworkRequired,
                        false,
                        "The cloud preference is stored, but credentials and provider integration are unavailable."),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(execution),
                    execution,
                    null),
            };
        }

        private static ReachyProviderExecution SanitizeProviderExecution(
            int value,
            bool allowAndroidService = true)
        {
            if (!Enum.IsDefined(typeof(ReachyProviderExecution), value))
            {
                return ReachyProviderExecution.Unconfigured;
            }
            ReachyProviderExecution execution = (ReachyProviderExecution)value;
            if (!allowAndroidService &&
                execution == ReachyProviderExecution.AndroidService)
            {
                return ReachyProviderExecution.Unconfigured;
            }
            return execution;
        }

        private static string BuildSpeechNetworkStatus(
            ReachyProviderSelection[] providers,
            string voice)
        {
            ReachyProviderSelection asr =
                providers[(int)ReachyProviderKind.Asr];
            ReachyProviderSelection tts =
                providers[(int)ReachyProviderKind.Tts];
            bool voiceNetworkPreference =
                string.Equals(
                    voice,
                    "Network voice preference",
                    StringComparison.Ordinal);
            if (asr.Connectivity ==
                    ReachyConnectivityRequirement.NetworkRequired ||
                tts.Connectivity ==
                    ReachyConnectivityRequirement.NetworkRequired ||
                voiceNetworkPreference)
            {
                return "Network required by the selected speech configuration.";
            }
            if (asr.Execution == ReachyProviderExecution.Unconfigured &&
                tts.Execution == ReachyProviderExecution.Unconfigured)
            {
                return "No speech provider is configured.";
            }
            return "Selected speech preferences are offline capable, but unavailable until models are installed.";
        }

        private static string BuildPrivacySummary(
            ReachyProviderSelection[] providers)
        {
            string summary = string.Empty;
            for (int index = 0; index < providers.Length; ++index)
            {
                ReachyProviderSelection provider = providers[index];
                if (!provider.SendsDataOffDevice)
                {
                    continue;
                }
                if (summary.Length > 0)
                {
                    summary += "; ";
                }
                summary +=
                    $"{GetProviderKindLabel(provider.Kind)}: " +
                    $"{provider.DisplayName} ({GetConnectivityLabel(provider.Connectivity)})";
            }
            return summary.Length == 0
                ? "No selected provider is configured to send data off device."
                : $"Cloud/network-bound selections — {summary}.";
        }

        private static string NextString(string[] values, string currentValue)
        {
            for (int index = 0; index < values.Length; ++index)
            {
                if (string.Equals(
                        values[index],
                        currentValue,
                        StringComparison.Ordinal))
                {
                    return values[(index + 1) % values.Length];
                }
            }
            return values[0];
        }

        private static int NextInt(int[] values, int currentValue)
        {
            for (int index = 0; index < values.Length; ++index)
            {
                if (values[index] == currentValue)
                {
                    return values[(index + 1) % values.Length];
                }
            }
            return values[0];
        }

        private static bool Contains(string[] values, string? value)
        {
            if (value == null)
            {
                return false;
            }
            for (int index = 0; index < values.Length; ++index)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(int[] values, int value)
        {
            for (int index = 0; index < values.Length; ++index)
            {
                if (values[index] == value)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

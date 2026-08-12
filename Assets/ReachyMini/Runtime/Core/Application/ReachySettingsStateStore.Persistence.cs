#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
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
    }
}

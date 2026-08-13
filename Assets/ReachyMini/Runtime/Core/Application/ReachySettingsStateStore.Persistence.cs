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

        public static void ValidateDurableSettings(ReachyDurableSettings durable)
        {
            if (durable == null)
            {
                throw new ArgumentNullException(nameof(durable));
            }

            ValidateProviderExecution(
                durable.AsrExecution,
                allowAndroidService: true,
                nameof(durable.AsrExecution));
            ValidateProviderExecution(
                durable.TtsExecution,
                allowAndroidService: true,
                nameof(durable.TtsExecution));
            ValidateProviderExecution(
                durable.LlmExecution,
                allowAndroidService: false,
                nameof(durable.LlmExecution));
            ValidateProviderExecution(
                durable.VlmExecution,
                allowAndroidService: false,
                nameof(durable.VlmExecution));

            if (!Enum.IsDefined(
                    typeof(ReachyCameraFacing),
                    durable.PreferredCameraFacing))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durable),
                    durable.PreferredCameraFacing,
                    "The persisted PreferredCameraFacing value is outside the supported contract.");
            }
            if (!Enum.IsDefined(
                    typeof(ReachySimulationFidelity),
                    durable.SimulationFidelity))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durable),
                    durable.SimulationFidelity,
                    "The persisted SimulationFidelity value is outside the supported contract.");
            }
            if (!Contains(SpeechLanguages, durable.SpeechLanguage))
            {
                throw new ArgumentException(
                    "The persisted SpeechLanguage value is not a supported settings value.",
                    nameof(durable));
            }
            if (!Contains(SpeechVoices, durable.SpeechVoice))
            {
                throw new ArgumentException(
                    "The persisted SpeechVoice value is not a supported settings value.",
                    nameof(durable));
            }
            if (!Contains(MemoryBudgetsMb, durable.LocalModelMemoryBudgetMb))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durable),
                    durable.LocalModelMemoryBudgetMb,
                    "The persisted LocalModelMemoryBudgetMb value is unsupported.");
            }
            if (!Contains(ContextLengths, durable.LocalModelContextTokens))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durable),
                    durable.LocalModelContextTokens,
                    "The persisted LocalModelContextTokens value is unsupported.");
            }
            if (!Contains(RetentionPeriods, durable.RetentionDays))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durable),
                    durable.RetentionDays,
                    "The persisted RetentionDays value is unsupported.");
            }
        }

        public void ApplyDurableSettings(ReachyDurableSettings durable)
        {
            ValidateDurableSettings(durable);

            ReachyProviderSelection[] providers =
            {
                BuildProvider(
                    ReachyProviderKind.Asr,
                    (ReachyProviderExecution)durable.AsrExecution),
                BuildProvider(
                    ReachyProviderKind.Tts,
                    (ReachyProviderExecution)durable.TtsExecution),
                BuildProvider(
                    ReachyProviderKind.Llm,
                    (ReachyProviderExecution)durable.LlmExecution),
                BuildProvider(
                    ReachyProviderKind.Vlm,
                    (ReachyProviderExecution)durable.VlmExecution),
            };

            Publish(
                providers: providers,
                preferredCameraFacing:
                    (ReachyCameraFacing)durable.PreferredCameraFacing,
                speechLanguage: durable.SpeechLanguage,
                speechVoice: durable.SpeechVoice,
                speechNetworkStatus: BuildSpeechNetworkStatus(
                    providers,
                    durable.SpeechVoice),
                localModelMemoryBudgetMb: durable.LocalModelMemoryBudgetMb,
                localModelContextTokens: durable.LocalModelContextTokens,
                simulationFidelity:
                    (ReachySimulationFidelity)durable.SimulationFidelity,
                historyEnabled: durable.HistoryEnabled,
                retentionDays: durable.RetentionDays,
                privacyCloudSummary: BuildPrivacySummary(providers),
                statusMessage: "Durable settings restored.");
        }

        private static void ValidateProviderExecution(
            int value,
            bool allowAndroidService,
            string parameterName)
        {
            if (!Enum.IsDefined(typeof(ReachyProviderExecution), value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "The persisted provider execution mode is unsupported.");
            }
            if (!allowAndroidService &&
                value == (int)ReachyProviderExecution.AndroidService)
            {
                throw new ArgumentException(
                    "Android-service execution is not supported for this provider role.",
                    parameterName);
            }
        }
    }
}

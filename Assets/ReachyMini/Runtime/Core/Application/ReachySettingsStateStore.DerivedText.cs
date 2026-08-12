#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
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
    }
}

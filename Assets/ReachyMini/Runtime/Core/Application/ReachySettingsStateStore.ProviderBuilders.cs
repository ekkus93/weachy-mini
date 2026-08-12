#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
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
    }
}

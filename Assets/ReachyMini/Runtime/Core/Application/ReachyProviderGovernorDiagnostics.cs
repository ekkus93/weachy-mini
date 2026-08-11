#nullable enable

using System;
using ReachyMini.LocalModels;

namespace ReachyMini.AppState
{
    public interface IReachyProviderGovernorDiagnosticsSource
    {
        LocalLlmGovernorDiagnosticsSnapshot GovernorDiagnostics { get; }
    }

    public sealed class ReachyProviderGovernorMainScreenProjection
    {
        private ReachyProviderGovernorMainScreenProjection(
            string activeProvider,
            ReachyProviderLocation providerLocation,
            bool overrideInteraction,
            ReachyInteractionState interactionState,
            string detail,
            bool governorOwnsInteractionState)
        {
            if (string.IsNullOrWhiteSpace(activeProvider))
            {
                throw new ArgumentException(
                    "The provider projection requires an active-provider label.",
                    nameof(activeProvider));
            }
            if (string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "The provider projection requires a user-visible detail.",
                    nameof(detail));
            }

            ActiveProvider = activeProvider;
            ProviderLocation = providerLocation;
            OverrideInteraction = overrideInteraction;
            InteractionState = interactionState;
            Detail = detail;
            GovernorOwnsInteractionState = governorOwnsInteractionState;
        }

        public string ActiveProvider { get; }
        public ReachyProviderLocation ProviderLocation { get; }
        public bool OverrideInteraction { get; }
        public ReachyInteractionState InteractionState { get; }
        public string Detail { get; }
        public bool GovernorOwnsInteractionState { get; }

        public static ReachyProviderGovernorMainScreenProjection Create(
            LocalLlmGovernorDiagnosticsSnapshot diagnostics,
            ReachyInteractionState currentInteractionState,
            bool governorOwnsInteractionState)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (!Enum.IsDefined(typeof(ReachyInteractionState), currentInteractionState))
            {
                throw new ArgumentOutOfRangeException(nameof(currentInteractionState));
            }

            switch (diagnostics.PresentationState)
            {
                case LocalLlmGovernorPresentationState.Unavailable:
                {
                    bool mayOverride = currentInteractionState != ReachyInteractionState.Error &&
                        (currentInteractionState == ReachyInteractionState.Idle ||
                         currentInteractionState == ReachyInteractionState.Unavailable ||
                         governorOwnsInteractionState);
                    return new ReachyProviderGovernorMainScreenProjection(
                        "Local LLM (resource status unavailable)",
                        ReachyProviderLocation.Local,
                        mayOverride,
                        ReachyInteractionState.Unavailable,
                        diagnostics.UserMessage,
                        mayOverride);
                }
                case LocalLlmGovernorPresentationState.Ready:
                    return new ReachyProviderGovernorMainScreenProjection(
                        "Local LLM",
                        ReachyProviderLocation.Local,
                        governorOwnsInteractionState,
                        ReachyInteractionState.Idle,
                        diagnostics.UserMessage,
                        false);
                case LocalLlmGovernorPresentationState.Throttled:
                {
                    bool mayOverride = currentInteractionState == ReachyInteractionState.Idle ||
                        governorOwnsInteractionState;
                    return new ReachyProviderGovernorMainScreenProjection(
                        "Local LLM (throttled)",
                        ReachyProviderLocation.Local,
                        mayOverride,
                        ReachyInteractionState.Idle,
                        diagnostics.UserMessage,
                        mayOverride);
                }
                case LocalLlmGovernorPresentationState.Suspended:
                {
                    bool mayOverride = currentInteractionState != ReachyInteractionState.Error;
                    return new ReachyProviderGovernorMainScreenProjection(
                        "Local LLM (suspended)",
                        ReachyProviderLocation.Local,
                        mayOverride,
                        ReachyInteractionState.Unavailable,
                        diagnostics.UserMessage,
                        mayOverride);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(diagnostics));
            }
        }
    }
}

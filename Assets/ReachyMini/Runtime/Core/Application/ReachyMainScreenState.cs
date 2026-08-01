#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyInteractionState
    {
        Idle = 0,
        Listening = 1,
        Transcribing = 2,
        Thinking = 3,
        Speaking = 4,
        Interrupted = 5,
        Unavailable = 6,
        Error = 7,
    }

    public enum ReachyProviderLocation
    {
        Unavailable = 0,
        Local = 1,
        Cloud = 2,
    }

    public sealed class ReachyMainScreenSnapshot
    {
        public ReachyMainScreenSnapshot(
            ReachyInteractionState interactionState,
            string detail,
            string activeCamera,
            bool cameraSelectionAvailable,
            string activeProvider,
            ReachyProviderLocation providerLocation,
            bool microphoneAvailable,
            bool settingsVisible,
            bool diagnosticsVisible,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "The main-screen detail must be present.",
                    nameof(detail));
            }
            if (string.IsNullOrWhiteSpace(activeCamera))
            {
                throw new ArgumentException(
                    "The active camera label must be present.",
                    nameof(activeCamera));
            }
            if (string.IsNullOrWhiteSpace(activeProvider))
            {
                throw new ArgumentException(
                    "The active provider label must be present.",
                    nameof(activeProvider));
            }
            if (settingsVisible && diagnosticsVisible)
            {
                throw new ArgumentException(
                    "Settings and diagnostics cannot be visible simultaneously.");
            }

            InteractionState = interactionState;
            Detail = detail;
            ActiveCamera = activeCamera;
            CameraSelectionAvailable = cameraSelectionAvailable;
            ActiveProvider = activeProvider;
            ProviderLocation = providerLocation;
            MicrophoneAvailable = microphoneAvailable;
            SettingsVisible = settingsVisible;
            DiagnosticsVisible = diagnosticsVisible;
            Revision = revision;
        }

        public ReachyInteractionState InteractionState { get; }

        public string Detail { get; }

        public string ActiveCamera { get; }

        public bool CameraSelectionAvailable { get; }

        public string ActiveProvider { get; }

        public ReachyProviderLocation ProviderLocation { get; }

        public bool MicrophoneAvailable { get; }

        public bool SettingsVisible { get; }

        public bool DiagnosticsVisible { get; }

        public ulong Revision { get; }
    }

    public sealed class ReachyMainScreenChangedEventArgs : EventArgs
    {
        public ReachyMainScreenChangedEventArgs(ReachyMainScreenSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyMainScreenSnapshot Snapshot { get; }
    }

    public sealed class ReachyMainScreenStateStore
    {
        private ReachyMainScreenSnapshot current = new ReachyMainScreenSnapshot(
            ReachyInteractionState.Idle,
            "Application shell is starting.",
            "Fixed front / three-quarter",
            false,
            "Not configured",
            ReachyProviderLocation.Unavailable,
            false,
            false,
            false,
            0UL);

        public ReachyMainScreenSnapshot Current => current;

        public event EventHandler<ReachyMainScreenChangedEventArgs>? Changed;

        public void SetInteraction(
            ReachyInteractionState interactionState,
            string detail)
        {
            Publish(
                interactionState,
                detail,
                current.ActiveCamera,
                current.CameraSelectionAvailable,
                current.ActiveProvider,
                current.ProviderLocation,
                current.MicrophoneAvailable,
                current.SettingsVisible,
                current.DiagnosticsVisible);
        }

        public void SetCapabilities(
            string activeCamera,
            bool cameraSelectionAvailable,
            string activeProvider,
            ReachyProviderLocation providerLocation,
            bool microphoneAvailable)
        {
            Publish(
                current.InteractionState,
                current.Detail,
                activeCamera,
                cameraSelectionAvailable,
                activeProvider,
                providerLocation,
                microphoneAvailable,
                current.SettingsVisible,
                current.DiagnosticsVisible);
        }

        public void ShowSettings(string detail)
        {
            Publish(
                current.InteractionState,
                detail,
                current.ActiveCamera,
                current.CameraSelectionAvailable,
                current.ActiveProvider,
                current.ProviderLocation,
                current.MicrophoneAvailable,
                true,
                false);
        }

        public void ShowDiagnostics(string detail)
        {
            Publish(
                current.InteractionState,
                detail,
                current.ActiveCamera,
                current.CameraSelectionAvailable,
                current.ActiveProvider,
                current.ProviderLocation,
                current.MicrophoneAvailable,
                false,
                true);
        }

        public void HidePanels(string detail)
        {
            Publish(
                current.InteractionState,
                detail,
                current.ActiveCamera,
                current.CameraSelectionAvailable,
                current.ActiveProvider,
                current.ProviderLocation,
                current.MicrophoneAvailable,
                false,
                false);
        }

        public void ReportUnavailableAction(string action, string explanation)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException(
                    "An unavailable action requires a name.",
                    nameof(action));
            }
            if (string.IsNullOrWhiteSpace(explanation))
            {
                throw new ArgumentException(
                    "An unavailable action requires an explanation.",
                    nameof(explanation));
            }

            SetInteraction(
                ReachyInteractionState.Unavailable,
                $"{action} unavailable: {explanation}");
        }

        public static string GetInteractionLabel(ReachyInteractionState state)
        {
            return state switch
            {
                ReachyInteractionState.Idle => "Idle",
                ReachyInteractionState.Listening => "Listening",
                ReachyInteractionState.Transcribing => "Transcribing",
                ReachyInteractionState.Thinking => "Thinking",
                ReachyInteractionState.Speaking => "Speaking",
                ReachyInteractionState.Interrupted => "Interrupted",
                ReachyInteractionState.Unavailable => "Unavailable",
                ReachyInteractionState.Error => "Error",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
            };
        }

        public static string GetProviderLocationLabel(ReachyProviderLocation location)
        {
            return location switch
            {
                ReachyProviderLocation.Unavailable => "Unavailable",
                ReachyProviderLocation.Local => "Local",
                ReachyProviderLocation.Cloud => "Cloud",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(location),
                    location,
                    null),
            };
        }

        private void Publish(
            ReachyInteractionState interactionState,
            string detail,
            string activeCamera,
            bool cameraSelectionAvailable,
            string activeProvider,
            ReachyProviderLocation providerLocation,
            bool microphoneAvailable,
            bool settingsVisible,
            bool diagnosticsVisible)
        {
            ReachyMainScreenSnapshot next = new ReachyMainScreenSnapshot(
                interactionState,
                detail,
                activeCamera,
                cameraSelectionAvailable,
                activeProvider,
                providerLocation,
                microphoneAvailable,
                settingsVisible,
                diagnosticsVisible,
                checked(current.Revision + 1UL));
            current = next;
            Changed?.Invoke(this, new ReachyMainScreenChangedEventArgs(next));
        }
    }
}

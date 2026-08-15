#nullable enable

namespace ReachyMini.AppState
{
    public sealed partial class ReachyMainScreenStateStore
    {
        private bool lifecycleOwnsInteractionState;

        public ReachyMainScreenSnapshot PauseForApplicationInterruption()
        {
            if (current.InteractionState == ReachyInteractionState.Error ||
                current.InteractionState == ReachyInteractionState.Unavailable)
            {
                lifecycleOwnsInteractionState = false;
                return current;
            }

            lifecycleOwnsInteractionState = true;
            Publish(
                ReachyInteractionState.Interrupted,
                "Application paused; active interaction work was cancelled.",
                current.ActiveCamera,
                current.CameraSelectionAvailable,
                current.ActiveProvider,
                current.ProviderLocation,
                current.MicrophoneAvailable,
                settingsVisible: false,
                diagnosticsVisible: false);
            return current;
        }

        public ReachyMainScreenSnapshot ResumeAfterApplicationInterruption()
        {
            if (lifecycleOwnsInteractionState &&
                current.InteractionState == ReachyInteractionState.Interrupted)
            {
                Publish(
                    ReachyInteractionState.Idle,
                    "Application resumed; start a new interaction to continue.",
                    current.ActiveCamera,
                    current.CameraSelectionAvailable,
                    current.ActiveProvider,
                    current.ProviderLocation,
                    current.MicrophoneAvailable,
                    settingsVisible: false,
                    diagnosticsVisible: false);
            }
            lifecycleOwnsInteractionState = false;
            return current;
        }
    }
}

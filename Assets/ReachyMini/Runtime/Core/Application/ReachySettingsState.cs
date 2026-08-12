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
    }
}

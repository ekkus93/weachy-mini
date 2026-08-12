#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
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
    }
}

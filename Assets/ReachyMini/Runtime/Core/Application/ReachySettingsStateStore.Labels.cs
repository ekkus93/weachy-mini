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
                ReachySettingsSection.CloudLlm => "Cloud LLM",
                ReachySettingsSection.CloudVlm => "Cloud VLM",
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
            // RMA-012: keep the installed, offline notice surface aligned with
            // third_party/inventory.json. The first two entries are first-party
            // project disclosures; every remaining Component exactly matches an
            // inventory entry name so scripts/tests/test_rma012_licenses.py can
            // fail closed if the repository inventory and packaged UI drift.
            return new[]
            {
                new ReachyLicenseNotice(
                    "Weachy Mini project status",
                    "Unofficial community project; not endorsed by Pollen Robotics, " +
                        "Hugging Face, Google DeepMind, Unity, Google, or OpenAI.",
                    "Intended to be free and noncommercial."),
                new ReachyLicenseNotice(
                    "Weachy Mini",
                    "Project contributors",
                    "Repository LICENSE and third_party/THIRD_PARTY_NOTICES.md"),
                new ReachyLicenseNotice(
                    "Unity 6.3 LTS Editor",
                    "Unity Technologies",
                    "Unity Software Terms; developer tool, not redistributed."),
                new ReachyLicenseNotice(
                    "MuJoCo",
                    "Google DeepMind",
                    "Apache-2.0; upstream NOTICE retained for packaged runtime."),
                new ReachyLicenseNotice(
                    "Reachy Mini software",
                    "Pollen Robotics / Hugging Face",
                    "Apache-2.0; exact imported paths retain upstream notices."),
                new ReachyLicenseNotice(
                    "Reachy Mini hardware and model assets",
                    "Pollen Robotics",
                    "CC BY-NC-SA; Unity presentation meshes are coordinate-converted " +
                        "derivatives; attribution, noncommercial, ShareAlike, and " +
                        "modification notices apply."),
                new ReachyLicenseNotice(
                    "llama.cpp",
                    "ggml-org contributors",
                    "MIT License; pinned upstream license retained."),
                new ReachyLicenseNotice(
                    "Qwen3 0.6B Q4_K_M",
                    "Qwen project contributors",
                    "Apache-2.0; benchmark-selected upstream GGUF is not bundled; " +
                        "installation/import remains explicit and integrity-checked."),
                new ReachyLicenseNotice(
                    "AndroidX CameraX",
                    "Android Open Source Project / Google",
                    "Apache-2.0; packaged CameraX module notices retained."),
                new ReachyLicenseNotice(
                    "Google ML Kit bundled face detection",
                    "Google",
                    "Google ML Kit Terms of Service and generated dependency notices."),
                new ReachyLicenseNotice(
                    "Google ML Kit bundled selfie segmentation",
                    "Google",
                    "Google ML Kit Terms of Service and generated dependency notices."),
                new ReachyLicenseNotice(
                    "Barack Obama United States Senate portrait thumbnail",
                    "United States Senate",
                    "Public domain (PD-USGov-Congress); packaged test fixture only."),
            };
        }
    }
}

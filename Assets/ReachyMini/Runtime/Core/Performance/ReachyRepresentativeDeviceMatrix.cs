#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReachyMini.LocalModels;

namespace ReachyMini.Performance
{
    public enum ReachyAndroidPerformanceClass
    {
        Low = 0,
        Mid = 1,
        High = 2,
    }

    public enum ReachyDeviceSupportStatus
    {
        Unsupported = 0,
        SupportedWithLimitations = 1,
        Supported = 2,
    }

    public enum ReachyOfflineSpeechCapability
    {
        Unknown = 0,
        Unavailable = 1,
        Available = 2,
    }

    public sealed class ReachyRepresentativeDeviceProfile
    {
        public const int MinimumLongRunSeconds = 1800;
        public const double MaximumPhysicsP95Milliseconds = 2.0;
        public const double MaximumStateLagGrowthSeconds = 0.002;
        public const double MinimumLocalLlmDecodeTokensPerSecond = 1.0;

        private ReachyRepresentativeDeviceProfile(
            ReachyAndroidPerformanceClass performanceClass,
            int targetFramesPerSecond,
            LocalLlmDeviceProfileKind localLlmProfile,
            long maximumMemoryGrowthBytes)
        {
            PerformanceClass = performanceClass;
            TargetFramesPerSecond = targetFramesPerSecond;
            LocalLlmProfile = localLlmProfile;
            MaximumMemoryGrowthBytes = maximumMemoryGrowthBytes;
            MaximumRenderP95Milliseconds =
                (1000.0 / targetFramesPerSecond) * 1.15;
        }

        public ReachyAndroidPerformanceClass PerformanceClass { get; }

        public int TargetFramesPerSecond { get; }

        public double MaximumRenderP95Milliseconds { get; }

        public LocalLlmDeviceProfileKind LocalLlmProfile { get; }

        public long MaximumMemoryGrowthBytes { get; }

        public static ReachyRepresentativeDeviceProfile ForClass(
            ReachyAndroidPerformanceClass performanceClass)
        {
            switch (performanceClass)
            {
                case ReachyAndroidPerformanceClass.Low:
                    return new ReachyRepresentativeDeviceProfile(
                        performanceClass,
                        targetFramesPerSecond: 30,
                        localLlmProfile: LocalLlmDeviceProfileKind.Conservative,
                        maximumMemoryGrowthBytes: 128L * 1024L * 1024L);
                case ReachyAndroidPerformanceClass.Mid:
                    return new ReachyRepresentativeDeviceProfile(
                        performanceClass,
                        targetFramesPerSecond: 30,
                        localLlmProfile: LocalLlmDeviceProfileKind.Balanced,
                        maximumMemoryGrowthBytes: 192L * 1024L * 1024L);
                case ReachyAndroidPerformanceClass.High:
                    return new ReachyRepresentativeDeviceProfile(
                        performanceClass,
                        targetFramesPerSecond: 60,
                        localLlmProfile: LocalLlmDeviceProfileKind.Performance,
                        maximumMemoryGrowthBytes: 256L * 1024L * 1024L);
                default:
                    throw new ArgumentOutOfRangeException(nameof(performanceClass));
            }
        }
    }

    public sealed class ReachyRepresentativeDeviceCapabilities
    {
        public ReachyRepresentativeDeviceCapabilities(
            int androidApiLevel,
            long totalMemoryBytes,
            int logicalProcessorCount,
            string graphicsApi,
            bool rearCameraAvailable,
            bool frontCameraAvailable,
            ReachyOfflineSpeechCapability onDeviceAsr,
            ReachyOfflineSpeechCapability offlineTts)
        {
            if (androidApiLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(androidApiLevel));
            }
            if (totalMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(totalMemoryBytes));
            }
            if (logicalProcessorCount < 0 || logicalProcessorCount > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalProcessorCount));
            }
            if (string.IsNullOrWhiteSpace(graphicsApi))
            {
                throw new ArgumentException(
                    "Representative-device graphics API must be explicit.",
                    nameof(graphicsApi));
            }
            if (!Enum.IsDefined(typeof(ReachyOfflineSpeechCapability), onDeviceAsr))
            {
                throw new ArgumentOutOfRangeException(nameof(onDeviceAsr));
            }
            if (!Enum.IsDefined(typeof(ReachyOfflineSpeechCapability), offlineTts))
            {
                throw new ArgumentOutOfRangeException(nameof(offlineTts));
            }

            AndroidApiLevel = androidApiLevel;
            TotalMemoryBytes = totalMemoryBytes;
            LogicalProcessorCount = logicalProcessorCount;
            GraphicsApi = graphicsApi.Trim();
            RearCameraAvailable = rearCameraAvailable;
            FrontCameraAvailable = frontCameraAvailable;
            OnDeviceAsr = onDeviceAsr;
            OfflineTts = offlineTts;
        }

        public int AndroidApiLevel { get; }

        public long TotalMemoryBytes { get; }

        public int LogicalProcessorCount { get; }

        public string GraphicsApi { get; }

        public bool RearCameraAvailable { get; }

        public bool FrontCameraAvailable { get; }

        public ReachyOfflineSpeechCapability OnDeviceAsr { get; }

        public ReachyOfflineSpeechCapability OfflineTts { get; }
    }

    public sealed class ReachyDeviceSupportAssessment
    {
        internal ReachyDeviceSupportAssessment(
            ReachyDeviceSupportStatus status,
            string diagnostic)
        {
            Status = status;
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        }

        public ReachyDeviceSupportStatus Status { get; }

        public string Diagnostic { get; }

        public bool CoreRuntimeSupported => Status != ReachyDeviceSupportStatus.Unsupported;

        public bool FullOfflineInteractionSupported =>
            Status == ReachyDeviceSupportStatus.Supported;
    }

    public static class ReachyRepresentativeDeviceSupportPolicy
    {
        public const int MinimumAndroidApiLevel = 26;
        public const int MinimumOfflineAsrApiLevel = 31;
        public const long MinimumMemoryBytes = 3L * 1024L * 1024L * 1024L;
        public const int MinimumLogicalProcessorCount = 4;

        public static ReachyDeviceSupportAssessment Evaluate(
            ReachyRepresentativeDeviceCapabilities capabilities)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            var failures = new List<string>();
            if (capabilities.AndroidApiLevel < MinimumAndroidApiLevel)
            {
                failures.Add("android_api_below_26");
            }
            if (capabilities.TotalMemoryBytes < MinimumMemoryBytes)
            {
                failures.Add("ram_below_3_gib");
            }
            if (capabilities.LogicalProcessorCount < MinimumLogicalProcessorCount)
            {
                failures.Add("cpu_below_4_logical_processors");
            }
            if (!IsSupportedGraphicsApi(capabilities.GraphicsApi))
            {
                failures.Add("graphics_api_not_vulkan_or_gles3");
            }
            if (!capabilities.RearCameraAvailable)
            {
                failures.Add("rear_camera_unavailable");
            }

            if (failures.Count > 0)
            {
                return new ReachyDeviceSupportAssessment(
                    ReachyDeviceSupportStatus.Unsupported,
                    "Unsupported core runtime: " + string.Join(",", failures) + ".");
            }

            var limitations = new List<string>();
            if (!capabilities.FrontCameraAvailable)
            {
                limitations.Add("front_camera_unavailable");
            }
            if (capabilities.AndroidApiLevel < MinimumOfflineAsrApiLevel)
            {
                limitations.Add("explicit_on_device_asr_requires_api_31");
            }
            else if (capabilities.OnDeviceAsr != ReachyOfflineSpeechCapability.Available)
            {
                limitations.Add("explicit_on_device_asr_unavailable");
            }
            if (capabilities.OfflineTts != ReachyOfflineSpeechCapability.Available)
            {
                limitations.Add("offline_tts_unavailable");
            }

            if (limitations.Count > 0)
            {
                return new ReachyDeviceSupportAssessment(
                    ReachyDeviceSupportStatus.SupportedWithLimitations,
                    "Core runtime supported; full offline interaction limited by " +
                    string.Join(",", limitations) + ".");
            }

            return new ReachyDeviceSupportAssessment(
                ReachyDeviceSupportStatus.Supported,
                "Core runtime and full offline speech interaction are supported.");
        }

        private static bool IsSupportedGraphicsApi(string graphicsApi) =>
            string.Equals(graphicsApi, "Vulkan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(graphicsApi, "OpenGLES3", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(graphicsApi, "OpenGL ES 3", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ReachyRepresentativeDeviceQualificationObservation
    {
        public ReachyRepresentativeDeviceQualificationObservation(
            double durationSeconds,
            double renderP95Milliseconds,
            double physicsP95Milliseconds,
            long initialUnityAllocatedMemoryBytes,
            long finalUnityAllocatedMemoryBytes,
            double initialStateLagSeconds,
            double finalStateLagSeconds,
            double localLlmDecodeTokensPerSecond,
            bool thermalDegradationOrderValid)
        {
            RequireFiniteNonNegative(durationSeconds, nameof(durationSeconds));
            RequireFiniteNonNegative(renderP95Milliseconds, nameof(renderP95Milliseconds));
            RequireFiniteNonNegative(physicsP95Milliseconds, nameof(physicsP95Milliseconds));
            if (initialUnityAllocatedMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(initialUnityAllocatedMemoryBytes));
            }
            if (finalUnityAllocatedMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(finalUnityAllocatedMemoryBytes));
            }
            RequireFiniteNonNegative(initialStateLagSeconds, nameof(initialStateLagSeconds));
            RequireFiniteNonNegative(finalStateLagSeconds, nameof(finalStateLagSeconds));
            RequireFiniteNonNegative(
                localLlmDecodeTokensPerSecond,
                nameof(localLlmDecodeTokensPerSecond));

            DurationSeconds = durationSeconds;
            RenderP95Milliseconds = renderP95Milliseconds;
            PhysicsP95Milliseconds = physicsP95Milliseconds;
            InitialUnityAllocatedMemoryBytes = initialUnityAllocatedMemoryBytes;
            FinalUnityAllocatedMemoryBytes = finalUnityAllocatedMemoryBytes;
            InitialStateLagSeconds = initialStateLagSeconds;
            FinalStateLagSeconds = finalStateLagSeconds;
            LocalLlmDecodeTokensPerSecond = localLlmDecodeTokensPerSecond;
            ThermalDegradationOrderValid = thermalDegradationOrderValid;
        }

        public double DurationSeconds { get; }
        public double RenderP95Milliseconds { get; }
        public double PhysicsP95Milliseconds { get; }
        public long InitialUnityAllocatedMemoryBytes { get; }
        public long FinalUnityAllocatedMemoryBytes { get; }
        public double InitialStateLagSeconds { get; }
        public double FinalStateLagSeconds { get; }
        public double LocalLlmDecodeTokensPerSecond { get; }
        public bool ThermalDegradationOrderValid { get; }

        private static void RequireFiniteNonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public sealed class ReachyRepresentativeDeviceQualificationResult
    {
        internal ReachyRepresentativeDeviceQualificationResult(
            ReachyAndroidPerformanceClass performanceClass,
            IReadOnlyList<string> failures)
        {
            PerformanceClass = performanceClass;
            Failures = new ReadOnlyCollection<string>(
                new List<string>(failures ?? throw new ArgumentNullException(nameof(failures))));
        }

        public ReachyAndroidPerformanceClass PerformanceClass { get; }

        public IReadOnlyList<string> Failures { get; }

        public bool Passed => Failures.Count == 0;
    }

    public static class ReachyRepresentativeDeviceQualificationPolicy
    {
        public static ReachyRepresentativeDeviceQualificationResult Evaluate(
            ReachyAndroidPerformanceClass performanceClass,
            ReachyRepresentativeDeviceQualificationObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            ReachyRepresentativeDeviceProfile profile =
                ReachyRepresentativeDeviceProfile.ForClass(performanceClass);
            var failures = new List<string>();

            if (observation.DurationSeconds < ReachyRepresentativeDeviceProfile.MinimumLongRunSeconds)
            {
                failures.Add("long_run_shorter_than_1800_seconds");
            }
            if (observation.RenderP95Milliseconds > profile.MaximumRenderP95Milliseconds)
            {
                failures.Add("render_p95_exceeds_profile_budget");
            }
            if (observation.PhysicsP95Milliseconds >
                ReachyRepresentativeDeviceProfile.MaximumPhysicsP95Milliseconds)
            {
                failures.Add("physics_p95_exceeds_2ms_timestep");
            }

            long memoryGrowth = Math.Max(
                0L,
                observation.FinalUnityAllocatedMemoryBytes -
                observation.InitialUnityAllocatedMemoryBytes);
            if (memoryGrowth > profile.MaximumMemoryGrowthBytes)
            {
                failures.Add("unity_memory_growth_exceeds_profile_budget");
            }

            double lagGrowth = Math.Max(
                0.0,
                observation.FinalStateLagSeconds - observation.InitialStateLagSeconds);
            if (lagGrowth > ReachyRepresentativeDeviceProfile.MaximumStateLagGrowthSeconds)
            {
                failures.Add("simulation_state_lag_accumulates");
            }
            if (observation.LocalLlmDecodeTokensPerSecond <
                ReachyRepresentativeDeviceProfile.MinimumLocalLlmDecodeTokensPerSecond)
            {
                failures.Add("local_llm_decode_below_1_token_per_second");
            }
            if (!observation.ThermalDegradationOrderValid)
            {
                failures.Add("thermal_degradation_order_invalid");
            }

            return new ReachyRepresentativeDeviceQualificationResult(
                performanceClass,
                failures);
        }
    }
}

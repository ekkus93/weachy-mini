#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Core;
using ReachyMini.Diagnostics;
using ReachyMini.LocalModels;
using ReachyMini.Rendering;
using ReachyMini.Simulation;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public sealed class ReachyDiagnosticsScreenSource : IDisposable
    {
        private const double MinimumRateSampleSeconds = 0.10;
        private const double Megabyte = 1024.0 * 1024.0;

        private readonly ReachyProductionAuthoritativeRuntime runtime;
        private readonly ReachySettingsStateStore settings;
        private readonly ReachyAndroidCameraDiscovery cameraDiscovery;
        private readonly IReachyApplicationService[] services;
        private ReachyAndroidCameraAcquisition? cameraAcquisition;
        private ReachyAndroidLocalLlmResourceSignalSource? androidResources;
        private double previousSimulationSampleTime;
        private ulong previousSimulationStepCount;
        private double previousCameraSampleTime;
        private ulong previousCameraFrameCount;
        private string resourceUnavailableReason =
            "Android resource telemetry has not been sampled.";
        private bool disposed;

        public ReachyDiagnosticsScreenSource(
            ReachyProductionAuthoritativeRuntime productionRuntime,
            ReachySettingsStateStore settingsStore,
            ReachyAndroidCameraDiscovery discovery,
            IReadOnlyList<IReachyApplicationService> applicationServices)
        {
            runtime = productionRuntime ??
                throw new ArgumentNullException(nameof(productionRuntime));
            settings = settingsStore ??
                throw new ArgumentNullException(nameof(settingsStore));
            cameraDiscovery = discovery ??
                throw new ArgumentNullException(nameof(discovery));
            if (applicationServices == null)
            {
                throw new ArgumentNullException(nameof(applicationServices));
            }
            services = new IReachyApplicationService[applicationServices.Count];
            for (int index = 0; index < applicationServices.Count; ++index)
            {
                services[index] = applicationServices[index] ??
                    throw new ArgumentException(
                        "Diagnostics service health cannot contain null entries.",
                        nameof(applicationServices));
            }
        }

        public ReachyDiagnosticsScreenSnapshot Capture()
        {
            ThrowIfDisposed();
            double now = Time.realtimeSinceStartupAsDouble;
            ReachySettingsSnapshot currentSettings = settings.Current;
            LocalLlmResourceSnapshot? resources = TryCaptureResources();

            return new ReachyDiagnosticsScreenSnapshot(
                BuildSimulation(now),
                BuildRendering(resources),
                BuildCamera(now),
                BuildProviders(currentSettings),
                BuildVersions(currentSettings),
                BuildDevice(resources));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            androidResources?.Dispose();
            androidResources = null;
        }

        private ReachyDiagnosticsSection BuildSimulation(double now)
        {
            var metrics = new List<ReachyDiagnosticsMetric>(8)
            {
                new ReachyDiagnosticsMetric(
                    "Runtime",
                    runtime.Status.ToString(),
                    runtime.Status == ReachyProductionRuntimeStatus.Running ||
                    runtime.Status == ReachyProductionRuntimeStatus.Paused
                        ? ReachyDiagnosticsAvailability.Available
                        : ReachyDiagnosticsAvailability.Degraded,
                    runtime.Status == ReachyProductionRuntimeStatus.Running ||
                    runtime.Status == ReachyProductionRuntimeStatus.Paused
                        ? string.Empty
                        : "The authoritative simulation runtime is not currently running."),
                new ReachyDiagnosticsMetric(
                    "Target physics frequency",
                    $"{1.0 / ProjectMetadata.InitialPhysicsTimestepSeconds:F1} Hz"),
            };

            if (!runtime.TryGetLatestTimingSnapshot(
                    out ReachySimulationTimingSnapshot timing))
            {
                const string reason =
                    "The authoritative worker has not published a timing snapshot.";
                metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                    "Observed physics frequency", reason));
                metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                    "Last / max step time", reason));
                metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                    "Missed deadlines", reason));
                metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                    "Accumulated lag", reason));
                metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                    "Constraint health", reason));
            }
            else
            {
                double sampleSeconds = now - previousSimulationSampleTime;
                if (previousSimulationSampleTime > 0.0 &&
                    sampleSeconds >= MinimumRateSampleSeconds &&
                    timing.TotalStepCount >= previousSimulationStepCount)
                {
                    ulong deltaSteps =
                        timing.TotalStepCount - previousSimulationStepCount;
                    metrics.Add(new ReachyDiagnosticsMetric(
                        "Observed physics frequency",
                        $"{deltaSteps / sampleSeconds:F1} Hz"));
                }
                else
                {
                    metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                        "Observed physics frequency",
                        "A second monotonic timing sample is required."));
                }

                previousSimulationSampleTime = now;
                previousSimulationStepCount = timing.TotalStepCount;
                metrics.Add(new ReachyDiagnosticsMetric(
                    "Last / max step time",
                    $"{timing.LastStepDurationSeconds * 1000.0:F3} / " +
                    $"{timing.MaximumStepDurationSeconds * 1000.0:F3} ms"));
                metrics.Add(timing.DeadlineMissCount == 0UL
                    ? new ReachyDiagnosticsMetric("Missed deadlines", "0")
                    : ReachyDiagnosticsMetric.Degraded(
                        "Missed deadlines",
                        timing.DeadlineMissCount.ToString(),
                        "The physics worker has missed one or more fixed-step deadlines."));
                metrics.Add(timing.AccumulatedLagSeconds <= 0.0
                    ? new ReachyDiagnosticsMetric("Accumulated lag", "0.000 ms")
                    : ReachyDiagnosticsMetric.Degraded(
                        "Accumulated lag",
                        $"{timing.AccumulatedLagSeconds * 1000.0:F3} ms",
                        "The fixed-step worker reports accumulated scheduling lag."));
                metrics.Add(timing.SolverWarningCount == 0UL
                    ? new ReachyDiagnosticsMetric("Constraint health", "healthy")
                    : ReachyDiagnosticsMetric.Degraded(
                        "Constraint health",
                        $"{timing.SolverWarningCount} solver warning(s)",
                        "MuJoCo reported one or more solver-warning episodes."));
            }

            int faultedServices = 0;
            int unavailableServices = 0;
            for (int index = 0; index < services.Length; ++index)
            {
                ReachyServiceState state = services[index].Health.State;
                if (state == ReachyServiceState.Faulted)
                {
                    ++faultedServices;
                }
                else if (state == ReachyServiceState.Unavailable)
                {
                    ++unavailableServices;
                }
            }
            metrics.Add(faultedServices == 0 && unavailableServices == 0
                ? new ReachyDiagnosticsMetric("Service faults", "none")
                : ReachyDiagnosticsMetric.Degraded(
                    "Service faults",
                    $"faulted={faultedServices}; unavailable={unavailableServices}",
                    "One or more application services are not healthy; service details remain in structured diagnostics."));

            metrics.Add(string.IsNullOrWhiteSpace(runtime.Fault)
                ? new ReachyDiagnosticsMetric("Fault", "none")
                : ReachyDiagnosticsMetric.Degraded(
                    "Fault",
                    "present",
                    "The authoritative runtime has a retained fault; inspect structured logs for the redacted event."));
            return new ReachyDiagnosticsSection("Simulation", metrics);
        }

        private ReachyDiagnosticsSection BuildRendering(
            LocalLlmResourceSnapshot? resources)
        {
            var metrics = new List<ReachyDiagnosticsMetric>(5)
            {
                new ReachyDiagnosticsMetric(
                    "Renderer",
                    runtime.RendererStatus.ToString(),
                    runtime.RendererStatus == ReachyAuthoritativeRendererStatus.Rendering
                        ? ReachyDiagnosticsAvailability.Available
                        : ReachyDiagnosticsAvailability.Degraded,
                    runtime.RendererStatus == ReachyAuthoritativeRendererStatus.Rendering
                        ? string.Empty
                        : "The authoritative renderer is not in its ready state."),
            };

            float frameSeconds = Time.unscaledDeltaTime;
            metrics.Add(frameSeconds > 0f
                ? new ReachyDiagnosticsMetric(
                    "Render FPS",
                    $"{1.0 / frameSeconds:F1}")
                : ReachyDiagnosticsMetric.Unavailable(
                    "Render FPS",
                    "Unity has not published a positive unscaled frame interval."));

            long allocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
            metrics.Add(allocatedBytes >= 0L
                ? new ReachyDiagnosticsMetric(
                    "Allocated memory",
                    $"{allocatedBytes / Megabyte:F1} MiB")
                : ReachyDiagnosticsMetric.Unavailable(
                    "Allocated memory",
                    "Unity memory telemetry is unavailable."));

            metrics.Add(resources == null
                ? ReachyDiagnosticsMetric.Unavailable(
                    "Thermal state",
                    resourceUnavailableReason)
                : new ReachyDiagnosticsMetric(
                    "Thermal state",
                    resources.ThermalStatus.ToString(),
                    resources.ThermalStatus == LocalLlmThermalStatus.None
                        ? ReachyDiagnosticsAvailability.Available
                        : ReachyDiagnosticsAvailability.Degraded,
                    resources.ThermalStatus == LocalLlmThermalStatus.None
                        ? string.Empty
                        : "The Android resource signal reports thermal pressure or unavailable telemetry."));

            LocalLlmDeviceProfile deviceProfile = LocalLlmDeviceProfile.Select(
                GetTotalMemoryBytes(resources),
                GetProcessorCount(resources));
            metrics.Add(new ReachyDiagnosticsMetric(
                "Device profile",
                deviceProfile.Kind.ToString()));
            return new ReachyDiagnosticsSection("Rendering", metrics);
        }

        private ReachyDiagnosticsSection BuildCamera(double now)
        {
            cameraAcquisition ??=
                Object.FindAnyObjectByType<ReachyAndroidCameraAcquisition>();
            ReachyCameraAcquisitionSnapshot? acquisition =
                cameraAcquisition?.State.Current;
            var metrics = new List<ReachyDiagnosticsMetric>(5);

            if (acquisition == null)
            {
                const string reason =
                    "The live camera acquisition service is not present in this composition.";
                metrics.Add(ReachyDiagnosticsMetric.Unavailable("Camera FPS", reason));
                metrics.Add(ReachyDiagnosticsMetric.Unavailable("Active camera", reason));
            }
            else
            {
                double sampleSeconds = now - previousCameraSampleTime;
                if (previousCameraSampleTime > 0.0 &&
                    sampleSeconds >= MinimumRateSampleSeconds &&
                    acquisition.AcceptedFrameCount >= previousCameraFrameCount)
                {
                    ulong deltaFrames =
                        acquisition.AcceptedFrameCount - previousCameraFrameCount;
                    metrics.Add(new ReachyDiagnosticsMetric(
                        "Camera FPS",
                        $"{deltaFrames / sampleSeconds:F1}"));
                }
                else
                {
                    metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                        "Camera FPS",
                        "A second live camera frame-count sample is required."));
                }
                previousCameraSampleTime = now;
                previousCameraFrameCount = acquisition.AcceptedFrameCount;
                metrics.Add(acquisition.IsActive
                    ? new ReachyDiagnosticsMetric(
                        "Active camera",
                        $"{acquisition.CameraId} ({acquisition.RequestedFacing})")
                    : ReachyDiagnosticsMetric.Unavailable(
                        "Active camera",
                        acquisition.Message));
            }

            metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                "Reprojection time",
                "The production camera path does not yet publish a reprojection timing snapshot."));
            metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                "Valid coverage",
                "No production homography-coverage snapshot is bound to the application shell."));
            metrics.Add(new ReachyDiagnosticsMetric(
                "Discovery",
                cameraDiscovery.State.Current.Summary,
                cameraDiscovery.State.Current.AvailableCameraCount > 0
                    ? ReachyDiagnosticsAvailability.Available
                    : ReachyDiagnosticsAvailability.Degraded,
                cameraDiscovery.State.Current.AvailableCameraCount > 0
                    ? string.Empty
                    : cameraDiscovery.State.Current.Message));
            return new ReachyDiagnosticsSection("Camera", metrics);
        }

        private static ReachyDiagnosticsSection BuildProviders(
            ReachySettingsSnapshot currentSettings)
        {
            var metrics = new List<ReachyDiagnosticsMetric>(4);
            foreach (ReachyProviderKind kind in Enum.GetValues(
                         typeof(ReachyProviderKind)))
            {
                ReachyProviderSelection provider = currentSettings.GetProvider(kind);
                string value =
                    $"{provider.DisplayName}; {provider.Execution}; {provider.Connectivity}";
                if (provider.Available)
                {
                    metrics.Add(new ReachyDiagnosticsMetric(kind.ToString(), value));
                }
                else if (provider.Execution == ReachyProviderExecution.Unconfigured)
                {
                    metrics.Add(ReachyDiagnosticsMetric.Unavailable(
                        kind.ToString(),
                        provider.Status));
                }
                else
                {
                    metrics.Add(ReachyDiagnosticsMetric.Degraded(
                        kind.ToString(),
                        value,
                        provider.Status));
                }
            }
            return new ReachyDiagnosticsSection("Providers", metrics);
        }

        private ReachyDiagnosticsSection BuildVersions(
            ReachySettingsSnapshot currentSettings)
        {
            var metrics = new List<ReachyDiagnosticsMetric>(7)
            {
                runtime.ModelHash == 0UL
                    ? ReachyDiagnosticsMetric.Unavailable(
                        "Simulation model",
                        "The authoritative runtime has not published a model hash.")
                    : new ReachyDiagnosticsMetric(
                        "Simulation model",
                        $"0x{runtime.ModelHash:x16}"),
                new ReachyDiagnosticsMetric(
                    "Local model",
                    currentSettings.ActiveLocalModel),
                new ReachyDiagnosticsMetric(
                    "Calibration",
                    currentSettings.CameraCalibrationProfile),
                new ReachyDiagnosticsMetric(
                    "Native ABI",
                    ProjectMetadata.NativeAbiVersion.ToString()),
                new ReachyDiagnosticsMetric(
                    "MuJoCo",
                    ReachyProductionAuthoritativeRuntime.RequiredMujocoVersion),
                string.IsNullOrWhiteSpace(runtime.ReachyAssetSourceHash)
                    ? ReachyDiagnosticsMetric.Unavailable(
                        "Reachy asset",
                        "The presentation root has not published its source-model hash.")
                    : new ReachyDiagnosticsMetric(
                        "Reachy asset",
                        runtime.ReachyAssetSourceHash),
                new ReachyDiagnosticsMetric(
                    "App",
                    string.IsNullOrWhiteSpace(Application.version)
                        ? "development"
                        : Application.version),
            };
            return new ReachyDiagnosticsSection("Versions", metrics);
        }

        private static ReachyDiagnosticsSection BuildDevice(
            LocalLlmResourceSnapshot? resources)
        {
            long totalMemoryBytes = GetTotalMemoryBytes(resources);
            int processors = GetProcessorCount(resources);
            LocalLlmDeviceProfile deviceProfile =
                LocalLlmDeviceProfile.Select(totalMemoryBytes, processors);
            var metrics = new List<ReachyDiagnosticsMetric>(6)
            {
                new ReachyDiagnosticsMetric("Model", SystemInfo.deviceModel),
                new ReachyDiagnosticsMetric("OS", SystemInfo.operatingSystem),
                new ReachyDiagnosticsMetric(
                    "Graphics",
                    $"{SystemInfo.graphicsDeviceType}; {SystemInfo.graphicsDeviceName}"),
                new ReachyDiagnosticsMetric(
                    "Memory",
                    totalMemoryBytes > 0L
                        ? $"{totalMemoryBytes / Megabyte:F0} MiB total"
                        : "unknown",
                    totalMemoryBytes > 0L
                        ? ReachyDiagnosticsAvailability.Available
                        : ReachyDiagnosticsAvailability.Degraded,
                    totalMemoryBytes > 0L
                        ? string.Empty
                        : "The runtime did not expose total physical memory."),
                new ReachyDiagnosticsMetric(
                    "CPU",
                    $"{processors} logical processor(s)"),
                new ReachyDiagnosticsMetric(
                    "Resource profile",
                    deviceProfile.Kind.ToString()),
            };
            return new ReachyDiagnosticsSection("Device", metrics);
        }

        private LocalLlmResourceSnapshot? TryCaptureResources()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                androidResources ??=
                    new ReachyAndroidLocalLlmResourceSignalSource();
                LocalLlmResourceSnapshot snapshot = androidResources.Capture(
                    LocalLlmPhysicsBudgetState.Unavailable);
                resourceUnavailableReason = string.Empty;
                return snapshot;
            }
            catch (Exception exception)
            {
                resourceUnavailableReason =
                    "Android resource telemetry failed (" +
                    exception.GetType().Name + ").";
                return null;
            }
#else
            resourceUnavailableReason =
                "Android thermal telemetry is unavailable outside an Android player.";
            return null;
#endif
        }

        private static long GetTotalMemoryBytes(LocalLlmResourceSnapshot? resources)
        {
            if (resources != null && resources.TotalMemoryBytes > 0L)
            {
                return resources.TotalMemoryBytes;
            }
            return SystemInfo.systemMemorySize > 0
                ? checked((long)SystemInfo.systemMemorySize * 1024L * 1024L)
                : 0L;
        }

        private static int GetProcessorCount(LocalLlmResourceSnapshot? resources)
        {
            if (resources != null && resources.LogicalProcessorCount > 0)
            {
                return resources.LogicalProcessorCount;
            }
            return Math.Max(1, SystemInfo.processorCount);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyDiagnosticsScreenSource));
            }
        }
    }
}

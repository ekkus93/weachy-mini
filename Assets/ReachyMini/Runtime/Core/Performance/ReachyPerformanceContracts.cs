#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Performance
{
    public enum ReachyPerformanceWorkload
    {
        NativePhysics = 0,
        UnityRendering = 1,
        CameraAcquisition = 2,
        CameraWarp = 3,
        LightweightTracking = 4,
        LocalLlm = 5,
        Audio = 6,
        Network = 7,
    }

    public enum ReachyPerformanceMetricAvailability
    {
        Available = 0,
        Unavailable = 1,
    }

    public sealed class ReachyPerformanceTimingSummary
    {
        internal ReachyPerformanceTimingSummary(
            ReachyPerformanceWorkload workload,
            ReachyPerformanceMetricAvailability availability,
            string availabilityReason,
            long sampleCount,
            bool percentilesApproximate,
            double medianMilliseconds,
            double p95Milliseconds,
            double p99Milliseconds,
            double maximumMilliseconds)
        {
            Workload = workload;
            Availability = availability;
            AvailabilityReason = availabilityReason ?? string.Empty;
            SampleCount = sampleCount;
            PercentilesApproximate = percentilesApproximate;
            MedianMilliseconds = medianMilliseconds;
            P95Milliseconds = p95Milliseconds;
            P99Milliseconds = p99Milliseconds;
            MaximumMilliseconds = maximumMilliseconds;
        }

        public ReachyPerformanceWorkload Workload { get; }

        public ReachyPerformanceMetricAvailability Availability { get; }

        public string AvailabilityReason { get; }

        public long SampleCount { get; }

        public bool PercentilesApproximate { get; }

        public double MedianMilliseconds { get; }

        public double P95Milliseconds { get; }

        public double P99Milliseconds { get; }

        public double MaximumMilliseconds { get; }
    }

    public sealed class ReachyPerformanceResourceSample
    {
        public ReachyPerformanceResourceSample(
            double monotonicSeconds,
            long? unityAllocatedMemoryBytes,
            long? systemAvailableMemoryBytes,
            double? batteryLevelFraction,
            int? thermalSeverity,
            string thermalState,
            string unavailableReason = "")
        {
            RequireFiniteNonNegative(monotonicSeconds, nameof(monotonicSeconds));
            if (unityAllocatedMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(unityAllocatedMemoryBytes));
            }
            if (systemAvailableMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(systemAvailableMemoryBytes));
            }
            if (batteryLevelFraction.HasValue &&
                (double.IsNaN(batteryLevelFraction.Value) ||
                 double.IsInfinity(batteryLevelFraction.Value) ||
                 batteryLevelFraction.Value < 0.0 ||
                 batteryLevelFraction.Value > 1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(batteryLevelFraction));
            }
            if (thermalSeverity.HasValue &&
                (thermalSeverity.Value < 0 || thermalSeverity.Value > 32))
            {
                throw new ArgumentOutOfRangeException(nameof(thermalSeverity));
            }
            if (string.IsNullOrWhiteSpace(thermalState))
            {
                throw new ArgumentException(
                    "Performance resource samples require a thermal state label.",
                    nameof(thermalState));
            }
            if (unavailableReason != null && unavailableReason.Length > 512)
            {
                throw new ArgumentOutOfRangeException(nameof(unavailableReason));
            }

            MonotonicSeconds = monotonicSeconds;
            UnityAllocatedMemoryBytes = unityAllocatedMemoryBytes;
            SystemAvailableMemoryBytes = systemAvailableMemoryBytes;
            BatteryLevelFraction = batteryLevelFraction;
            ThermalSeverity = thermalSeverity;
            ThermalState = thermalState.Trim();
            UnavailableReason = (unavailableReason ?? string.Empty).Trim();
        }

        public double MonotonicSeconds { get; }

        public long? UnityAllocatedMemoryBytes { get; }

        public long? SystemAvailableMemoryBytes { get; }

        public double? BatteryLevelFraction { get; }

        public int? ThermalSeverity { get; }

        public string ThermalState { get; }

        public string UnavailableReason { get; }

        private static void RequireFiniteNonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public sealed class ReachyPerformanceResourceSummary
    {
        internal ReachyPerformanceResourceSummary(
            long sampleCount,
            long droppedSampleCount,
            long? initialUnityAllocatedMemoryBytes,
            long? finalUnityAllocatedMemoryBytes,
            long? maximumUnityAllocatedMemoryBytes,
            long? minimumSystemAvailableMemoryBytes,
            double? initialBatteryLevelFraction,
            double? finalBatteryLevelFraction,
            double? batteryDischargeFraction,
            string peakThermalState,
            int? peakThermalSeverity)
        {
            SampleCount = sampleCount;
            DroppedSampleCount = droppedSampleCount;
            InitialUnityAllocatedMemoryBytes = initialUnityAllocatedMemoryBytes;
            FinalUnityAllocatedMemoryBytes = finalUnityAllocatedMemoryBytes;
            MaximumUnityAllocatedMemoryBytes = maximumUnityAllocatedMemoryBytes;
            MinimumSystemAvailableMemoryBytes = minimumSystemAvailableMemoryBytes;
            InitialBatteryLevelFraction = initialBatteryLevelFraction;
            FinalBatteryLevelFraction = finalBatteryLevelFraction;
            BatteryDischargeFraction = batteryDischargeFraction;
            PeakThermalState = peakThermalState ?? "unavailable";
            PeakThermalSeverity = peakThermalSeverity;
        }

        public long SampleCount { get; }

        public long DroppedSampleCount { get; }

        public long? InitialUnityAllocatedMemoryBytes { get; }

        public long? FinalUnityAllocatedMemoryBytes { get; }

        public long? MaximumUnityAllocatedMemoryBytes { get; }

        public long? MinimumSystemAvailableMemoryBytes { get; }

        public double? InitialBatteryLevelFraction { get; }

        public double? FinalBatteryLevelFraction { get; }

        public double? BatteryDischargeFraction { get; }

        public string PeakThermalState { get; }

        public int? PeakThermalSeverity { get; }
    }

    public sealed class ReachyPerformanceReport
    {
        internal ReachyPerformanceReport(
            int schemaVersion,
            string label,
            int targetFramesPerSecond,
            double durationSeconds,
            ReachyPerformanceTimingSummary[] timings,
            ReachyPerformanceResourceSummary resources,
            ReachyPerformanceResourceSample[] resourceSamples)
        {
            SchemaVersion = schemaVersion;
            Label = label;
            TargetFramesPerSecond = targetFramesPerSecond;
            DurationSeconds = durationSeconds;
            Timings = new ReadOnlyCollection<ReachyPerformanceTimingSummary>(timings);
            Resources = resources;
            ResourceSamples = new ReadOnlyCollection<ReachyPerformanceResourceSample>(
                resourceSamples);
        }

        public int SchemaVersion { get; }

        public string Label { get; }

        public int TargetFramesPerSecond { get; }

        public double DurationSeconds { get; }

        public IReadOnlyList<ReachyPerformanceTimingSummary> Timings { get; }

        public ReachyPerformanceResourceSummary Resources { get; }

        public IReadOnlyList<ReachyPerformanceResourceSample> ResourceSamples { get; }

        public ReachyPerformanceTimingSummary FindTiming(
            ReachyPerformanceWorkload workload)
        {
            for (int index = 0; index < Timings.Count; ++index)
            {
                if (Timings[index].Workload == workload)
                {
                    return Timings[index];
                }
            }
            throw new InvalidOperationException(
                "Performance report is missing workload " + workload + ".");
        }
    }
}

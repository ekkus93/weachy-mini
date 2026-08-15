#nullable enable

using System;
using System.Diagnostics;
using System.Threading;

namespace ReachyMini.Performance
{
    public static class ReachyPerformanceTelemetry
    {
        public const int PercentileReservoirCapacity = 4096;
        public const int MaximumResourceSamples = 2048;
        public const int SchemaVersion = 1;

        private static ReachyPerformanceSession? activeSession;

        public static bool IsSessionActive =>
            Volatile.Read(ref activeSession) != null;

        public static ReachyPerformanceSession StartSession(
            int targetFramesPerSecond,
            string label)
        {
            RequireFrameRate(targetFramesPerSecond);
            string boundedLabel = RequireLabel(label);
            var session = new ReachyPerformanceSession(
                targetFramesPerSecond,
                boundedLabel,
                Stopwatch.GetTimestamp());
            if (Interlocked.CompareExchange(
                    ref activeSession,
                    session,
                    null) != null)
            {
                throw new InvalidOperationException(
                    "A Reachy performance session is already active.");
            }
            return session;
        }

        public static ReachyPerformanceMeasurement Measure(
            ReachyPerformanceWorkload workload)
        {
            RequireWorkload(workload);
            ReachyPerformanceSession? session = Volatile.Read(ref activeSession);
            return session == null
                ? default
                : new ReachyPerformanceMeasurement(
                    session,
                    workload,
                    Stopwatch.GetTimestamp());
        }

        public static void RecordDurationSeconds(
            ReachyPerformanceWorkload workload,
            double durationSeconds)
        {
            RequireWorkload(workload);
            if (double.IsNaN(durationSeconds) ||
                double.IsInfinity(durationSeconds) ||
                durationSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }

            ReachyPerformanceSession? session = Volatile.Read(ref activeSession);
            session?.RecordDurationMilliseconds(
                workload,
                durationSeconds * 1000.0);
        }

        public static void RecordResourceSample(
            ReachyPerformanceResourceSample sample)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }
            ReachyPerformanceSession? session = Volatile.Read(ref activeSession);
            session?.RecordResourceSample(sample);
        }

        internal static void Complete(ReachyPerformanceSession session)
        {
            Interlocked.CompareExchange(ref activeSession, null, session);
        }

        internal static void RecordElapsed(
            ReachyPerformanceSession session,
            ReachyPerformanceWorkload workload,
            long startTimestamp)
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            if (endTimestamp < startTimestamp)
            {
                return;
            }
            double elapsedMilliseconds =
                (endTimestamp - startTimestamp) *
                (1000.0 / Stopwatch.Frequency);
            session.RecordDurationMilliseconds(workload, elapsedMilliseconds);
        }

        internal static void RequireWorkload(ReachyPerformanceWorkload workload)
        {
            if ((uint)workload > (uint)ReachyPerformanceWorkload.Network)
            {
                throw new ArgumentOutOfRangeException(nameof(workload));
            }
        }

        private static void RequireFrameRate(int targetFramesPerSecond)
        {
            if (targetFramesPerSecond != 30 && targetFramesPerSecond != 60)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetFramesPerSecond),
                    "RMA-180 performance sessions support 30 FPS or 60 FPS profiles.");
            }
        }

        private static string RequireLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException(
                    "Performance sessions require a nonempty label.",
                    nameof(label));
            }
            string trimmed = label.Trim();
            if (trimmed.Length > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(label));
            }
            for (int index = 0; index < trimmed.Length; ++index)
            {
                char value = trimmed[index];
                bool accepted =
                    (value >= 'a' && value <= 'z') ||
                    (value >= 'A' && value <= 'Z') ||
                    (value >= '0' && value <= '9') ||
                    value == '-' ||
                    value == '_' ||
                    value == '.';
                if (!accepted)
                {
                    throw new ArgumentException(
                        "Performance session labels are identifier-only and cannot contain private text.",
                        nameof(label));
                }
            }
            return trimmed;
        }
    }

    public readonly struct ReachyPerformanceMeasurement : IDisposable
    {
        private readonly ReachyPerformanceSession? session;
        private readonly ReachyPerformanceWorkload workload;
        private readonly long startTimestamp;

        internal ReachyPerformanceMeasurement(
            ReachyPerformanceSession session,
            ReachyPerformanceWorkload workload,
            long startTimestamp)
        {
            this.session = session;
            this.workload = workload;
            this.startTimestamp = startTimestamp;
        }

        public void Dispose()
        {
            ReachyPerformanceSession? owner = session;
            if (owner != null && !owner.IsCompleted)
            {
                ReachyPerformanceTelemetry.RecordElapsed(
                    owner,
                    workload,
                    startTimestamp);
            }
        }
    }

    public sealed class ReachyPerformanceSession : IDisposable
    {
        private const string UnexercisedReason =
            "Workload was not exercised during this performance session.";

        private readonly ReachyPerformanceAccumulator[] accumulators;
        private readonly object resourceGate = new object();
        private readonly ReachyPerformanceResourceSample?[] resourceSamples;
        private readonly long startTimestamp;
        private int resourceStartIndex;
        private int resourceCount;
        private long droppedResourceSamples;
        private long? initialUnityAllocatedMemoryBytes;
        private long? finalUnityAllocatedMemoryBytes;
        private long? maximumUnityAllocatedMemoryBytes;
        private long? minimumSystemAvailableMemoryBytes;
        private double? initialBatteryLevelFraction;
        private double? finalBatteryLevelFraction;
        private int? peakThermalSeverity;
        private string peakThermalState = "unavailable";
        private readonly object completionGate = new object();
        private int completed;
        private ReachyPerformanceReport? report;

        internal ReachyPerformanceSession(
            int targetFramesPerSecond,
            string label,
            long startTimestamp)
        {
            TargetFramesPerSecond = targetFramesPerSecond;
            Label = label;
            this.startTimestamp = startTimestamp;
            Array values = Enum.GetValues(typeof(ReachyPerformanceWorkload));
            accumulators = new ReachyPerformanceAccumulator[values.Length];
            for (int index = 0; index < accumulators.Length; ++index)
            {
                accumulators[index] = new ReachyPerformanceAccumulator(
                    ReachyPerformanceTelemetry.PercentileReservoirCapacity,
                    unchecked((ulong)(index + 1) * 0x9E3779B97F4A7C15UL));
            }
            resourceSamples = new ReachyPerformanceResourceSample?[
                ReachyPerformanceTelemetry.MaximumResourceSamples];
        }

        public string Label { get; }

        public int TargetFramesPerSecond { get; }

        public bool IsCompleted => Volatile.Read(ref completed) != 0;

        public ReachyPerformanceReport Complete()
        {
            lock (completionGate)
            {
                if (report != null)
                {
                    return report;
                }

                Volatile.Write(ref completed, 1);
                long endTimestamp = Stopwatch.GetTimestamp();
                ReachyPerformanceTelemetry.Complete(this);
                double durationSeconds = Math.Max(
                    0.0,
                    (endTimestamp - startTimestamp) /
                    (double)Stopwatch.Frequency);

                var timings = new ReachyPerformanceTimingSummary[accumulators.Length];
                for (int index = 0; index < accumulators.Length; ++index)
                {
                    ReachyPerformanceWorkload workload =
                        (ReachyPerformanceWorkload)index;
                    timings[index] = accumulators[index].Snapshot(
                        workload,
                        UnexercisedReason);
                }

                ReachyPerformanceResourceSample[] resourceSnapshot =
                    CaptureResourceSamples();
                ReachyPerformanceResourceSummary resourceSummary =
                    CaptureResourceSummary();
                report = new ReachyPerformanceReport(
                    ReachyPerformanceTelemetry.SchemaVersion,
                    Label,
                    TargetFramesPerSecond,
                    durationSeconds,
                    timings,
                    resourceSummary,
                    resourceSnapshot);
                return report;
            }
        }

        public void Dispose()
        {
            Complete();
        }

        internal void RecordDurationMilliseconds(
            ReachyPerformanceWorkload workload,
            double durationMilliseconds)
        {
            if (IsCompleted)
            {
                return;
            }
            ReachyPerformanceTelemetry.RequireWorkload(workload);
            if (double.IsNaN(durationMilliseconds) ||
                double.IsInfinity(durationMilliseconds) ||
                durationMilliseconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
            }
            accumulators[(int)workload].Add(durationMilliseconds);
        }

        internal void RecordResourceSample(ReachyPerformanceResourceSample sample)
        {
            if (IsCompleted)
            {
                return;
            }
            lock (resourceGate)
            {
                if (IsCompleted)
                {
                    return;
                }
                UpdateResourceSummary(sample);
                if (resourceCount < resourceSamples.Length)
                {
                    int index = (resourceStartIndex + resourceCount) %
                        resourceSamples.Length;
                    resourceSamples[index] = sample;
                    ++resourceCount;
                    return;
                }

                resourceSamples[resourceStartIndex] = sample;
                resourceStartIndex = (resourceStartIndex + 1) %
                    resourceSamples.Length;
                ++droppedResourceSamples;
            }
        }

        private ReachyPerformanceResourceSample[] CaptureResourceSamples()
        {
            lock (resourceGate)
            {
                var copy = new ReachyPerformanceResourceSample[resourceCount];
                for (int index = 0; index < resourceCount; ++index)
                {
                    ReachyPerformanceResourceSample? sample = resourceSamples[
                        (resourceStartIndex + index) % resourceSamples.Length];
                    copy[index] = sample ?? throw new InvalidOperationException(
                        "Performance resource ring contains an empty retained slot.");
                }
                return copy;
            }
        }

        private void UpdateResourceSummary(ReachyPerformanceResourceSample sample)
        {
            if (sample.UnityAllocatedMemoryBytes.HasValue)
            {
                long value = sample.UnityAllocatedMemoryBytes.Value;
                if (!initialUnityAllocatedMemoryBytes.HasValue)
                {
                    initialUnityAllocatedMemoryBytes = value;
                }
                finalUnityAllocatedMemoryBytes = value;
                maximumUnityAllocatedMemoryBytes =
                    !maximumUnityAllocatedMemoryBytes.HasValue
                        ? value
                        : Math.Max(maximumUnityAllocatedMemoryBytes.Value, value);
            }
            if (sample.SystemAvailableMemoryBytes.HasValue)
            {
                long value = sample.SystemAvailableMemoryBytes.Value;
                minimumSystemAvailableMemoryBytes =
                    !minimumSystemAvailableMemoryBytes.HasValue
                        ? value
                        : Math.Min(minimumSystemAvailableMemoryBytes.Value, value);
            }
            if (sample.BatteryLevelFraction.HasValue)
            {
                double value = sample.BatteryLevelFraction.Value;
                if (!initialBatteryLevelFraction.HasValue)
                {
                    initialBatteryLevelFraction = value;
                }
                finalBatteryLevelFraction = value;
            }
            if (sample.ThermalSeverity.HasValue &&
                (!peakThermalSeverity.HasValue ||
                 sample.ThermalSeverity.Value > peakThermalSeverity.Value))
            {
                peakThermalSeverity = sample.ThermalSeverity.Value;
                peakThermalState = sample.ThermalState;
            }
        }

        private ReachyPerformanceResourceSummary CaptureResourceSummary()
        {
            lock (resourceGate)
            {
                double? discharge = null;
                if (initialBatteryLevelFraction.HasValue &&
                    finalBatteryLevelFraction.HasValue)
                {
                    discharge = Math.Max(
                        0.0,
                        initialBatteryLevelFraction.Value -
                        finalBatteryLevelFraction.Value);
                }

                return new ReachyPerformanceResourceSummary(
                    resourceCount,
                    droppedResourceSamples,
                    initialUnityAllocatedMemoryBytes,
                    finalUnityAllocatedMemoryBytes,
                    maximumUnityAllocatedMemoryBytes,
                    minimumSystemAvailableMemoryBytes,
                    initialBatteryLevelFraction,
                    finalBatteryLevelFraction,
                    discharge,
                    peakThermalState,
                    peakThermalSeverity);
            }
        }
    }

    internal sealed class ReachyPerformanceAccumulator
    {
        private readonly object gate = new object();
        private readonly double[] reservoir;
        private ulong randomState;
        private long sampleCount;
        private double maximumMilliseconds;

        public ReachyPerformanceAccumulator(int capacity, ulong seed)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            reservoir = new double[capacity];
            randomState = seed == 0UL ? 1UL : seed;
        }

        public void Add(double milliseconds)
        {
            lock (gate)
            {
                ++sampleCount;
                if (sampleCount == 1L || milliseconds > maximumMilliseconds)
                {
                    maximumMilliseconds = milliseconds;
                }
                if (sampleCount <= reservoir.Length)
                {
                    reservoir[checked((int)sampleCount - 1)] = milliseconds;
                    return;
                }

                randomState = unchecked(
                    (randomState * 6364136223846793005UL) +
                    1442695040888963407UL);
                ulong selected = randomState % (ulong)sampleCount;
                if (selected < (ulong)reservoir.Length)
                {
                    reservoir[(int)selected] = milliseconds;
                }
            }
        }

        public ReachyPerformanceTimingSummary Snapshot(
            ReachyPerformanceWorkload workload,
            string unavailableReason)
        {
            lock (gate)
            {
                if (sampleCount == 0L)
                {
                    return new ReachyPerformanceTimingSummary(
                        workload,
                        ReachyPerformanceMetricAvailability.Unavailable,
                        unavailableReason,
                        0L,
                        percentilesApproximate: false,
                        medianMilliseconds: 0.0,
                        p95Milliseconds: 0.0,
                        p99Milliseconds: 0.0,
                        maximumMilliseconds: 0.0);
                }

                int retained = (int)Math.Min(sampleCount, reservoir.Length);
                var copy = new double[retained];
                Array.Copy(reservoir, copy, retained);
                Array.Sort(copy);
                return new ReachyPerformanceTimingSummary(
                    workload,
                    ReachyPerformanceMetricAvailability.Available,
                    string.Empty,
                    sampleCount,
                    sampleCount > reservoir.Length,
                    Percentile(copy, 0.50),
                    Percentile(copy, 0.95),
                    Percentile(copy, 0.99),
                    maximumMilliseconds);
            }
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted.Length == 0)
            {
                throw new ArgumentException(
                    "Percentiles require at least one sample.",
                    nameof(sorted));
            }
            int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
            index = Math.Max(0, Math.Min(sorted.Length - 1, index));
            return sorted[index];
        }
    }
}

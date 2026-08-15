#nullable enable

using System;
using System.Globalization;
using System.Threading;
using ReachyMini.Performance;

namespace ReachyMini.LocalModels
{
    public enum LocalLlmThermalStatus
    {
        Unavailable = 0,
        None = 1,
        Light = 2,
        Moderate = 3,
        Severe = 4,
        Critical = 5,
        Emergency = 6,
        Shutdown = 7,
    }

    public enum LocalLlmPhysicsBudgetState
    {
        Unavailable = 0,
        Healthy = 1,
        AtRisk = 2,
        Exceeded = 3,
    }

    public enum LocalLlmGovernorMode
    {
        Nominal = 0,
        Reduced = 1,
        Minimal = 2,
        Suspended = 3,
    }

    public enum LocalLlmDeviceProfileKind
    {
        Conservative = 0,
        Balanced = 1,
        Performance = 2,
    }

    [Flags]
    public enum LocalLlmGovernorReason
    {
        None = 0,
        DeviceProfileLimit = 1 << 0,
        ThermalSignalUnavailable = 1 << 1,
        MemorySignalUnavailable = 1 << 2,
        PhysicsSignalUnavailable = 1 << 3,
        ThermalLight = 1 << 4,
        ThermalModerate = 1 << 5,
        ThermalSevereOrWorse = 1 << 6,
        MemoryPressure = 1 << 7,
        MemoryCritical = 1 << 8,
        AndroidLowMemory = 1 << 9,
        PhysicsAtRisk = 1 << 10,
        PhysicsBudgetExceeded = 1 << 11,
        RecoveryHold = 1 << 12,
        RecentOutOfMemory = 1 << 13,
        ProfileIncompatible = 1 << 14,
        PriorityDegradationPolicy = 1 << 15,
    }

    public sealed class LocalLlmResourceSnapshot
    {
        public LocalLlmResourceSnapshot(
            long totalMemoryBytes,
            long availableMemoryBytes,
            long lowMemoryThresholdBytes,
            bool systemReportsLowMemory,
            int logicalProcessorCount,
            LocalLlmThermalStatus thermalStatus,
            LocalLlmPhysicsBudgetState physicsBudgetState,
            bool recentOutOfMemory = false)
        {
            if (totalMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(totalMemoryBytes));
            }
            if (availableMemoryBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(availableMemoryBytes));
            }
            if (lowMemoryThresholdBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(lowMemoryThresholdBytes));
            }
            if (totalMemoryBytes == 0L &&
                (availableMemoryBytes != 0L || lowMemoryThresholdBytes != 0L))
            {
                throw new ArgumentException(
                    "Unknown total memory requires unavailable memory fields to be zero.");
            }
            if (totalMemoryBytes > 0L && availableMemoryBytes > totalMemoryBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(availableMemoryBytes));
            }
            if (totalMemoryBytes > 0L && lowMemoryThresholdBytes > totalMemoryBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(lowMemoryThresholdBytes));
            }
            if (logicalProcessorCount < 0 || logicalProcessorCount > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalProcessorCount));
            }
            if (!Enum.IsDefined(typeof(LocalLlmThermalStatus), thermalStatus))
            {
                throw new ArgumentOutOfRangeException(nameof(thermalStatus));
            }
            if (!Enum.IsDefined(typeof(LocalLlmPhysicsBudgetState), physicsBudgetState))
            {
                throw new ArgumentOutOfRangeException(nameof(physicsBudgetState));
            }

            TotalMemoryBytes = totalMemoryBytes;
            AvailableMemoryBytes = availableMemoryBytes;
            LowMemoryThresholdBytes = lowMemoryThresholdBytes;
            SystemReportsLowMemory = systemReportsLowMemory;
            LogicalProcessorCount = logicalProcessorCount;
            ThermalStatus = thermalStatus;
            PhysicsBudgetState = physicsBudgetState;
            RecentOutOfMemory = recentOutOfMemory;
        }

        public long TotalMemoryBytes { get; }
        public long AvailableMemoryBytes { get; }
        public long LowMemoryThresholdBytes { get; }
        public bool SystemReportsLowMemory { get; }
        public int LogicalProcessorCount { get; }
        public LocalLlmThermalStatus ThermalStatus { get; }
        public LocalLlmPhysicsBudgetState PhysicsBudgetState { get; }
        public bool RecentOutOfMemory { get; }
    }

    public interface ILocalLlmResourceSignalSource
    {
        LocalLlmResourceSnapshot Capture(LocalLlmPhysicsBudgetState physicsBudgetState);
    }

    public sealed class LocalLlmDeviceProfile
    {
        private const long Gibibyte = 1024L * 1024L * 1024L;

        private LocalLlmDeviceProfile(
            LocalLlmDeviceProfileKind kind,
            int maximumContextTokens,
            int maximumBatchTokens,
            int maximumMicroBatchTokens,
            int maximumThreads,
            int maximumBatchThreads)
        {
            Kind = kind;
            MaximumContextTokens = maximumContextTokens;
            MaximumBatchTokens = maximumBatchTokens;
            MaximumMicroBatchTokens = maximumMicroBatchTokens;
            MaximumThreads = maximumThreads;
            MaximumBatchThreads = maximumBatchThreads;
        }

        public LocalLlmDeviceProfileKind Kind { get; }
        public int MaximumContextTokens { get; }
        public int MaximumBatchTokens { get; }
        public int MaximumMicroBatchTokens { get; }
        public int MaximumThreads { get; }
        public int MaximumBatchThreads { get; }

        public static LocalLlmDeviceProfile Select(
            long totalMemoryBytes,
            int logicalProcessorCount)
        {
            if (totalMemoryBytes <= 0L || logicalProcessorCount <= 0 ||
                totalMemoryBytes < 6L * Gibibyte || logicalProcessorCount <= 4)
            {
                return new LocalLlmDeviceProfile(
                    LocalLlmDeviceProfileKind.Conservative,
                    maximumContextTokens: 1024,
                    maximumBatchTokens: 128,
                    maximumMicroBatchTokens: 64,
                    maximumThreads: 2,
                    maximumBatchThreads: 2);
            }

            if (totalMemoryBytes < 10L * Gibibyte || logicalProcessorCount < 8)
            {
                return new LocalLlmDeviceProfile(
                    LocalLlmDeviceProfileKind.Balanced,
                    maximumContextTokens: 1536,
                    maximumBatchTokens: 192,
                    maximumMicroBatchTokens: 64,
                    maximumThreads: 3,
                    maximumBatchThreads: 3);
            }

            return new LocalLlmDeviceProfile(
                LocalLlmDeviceProfileKind.Performance,
                maximumContextTokens: 2048,
                maximumBatchTokens: 256,
                maximumMicroBatchTokens: 64,
                maximumThreads: 4,
                maximumBatchThreads: 4);
        }
    }

    public sealed class LocalLlmGovernorDecision
    {
        internal LocalLlmGovernorDecision(
            LocalLlmGovernorMode mode,
            LocalLlmGovernorReason reasons,
            LocalLlmDeviceProfile deviceProfile,
            LocalLlmExecutionProfile? effectiveProfile,
            string diagnostic)
        {
            Mode = mode;
            Reasons = reasons;
            DeviceProfile = deviceProfile ??
                throw new ArgumentNullException(nameof(deviceProfile));
            EffectiveProfile = effectiveProfile;
            Diagnostic = LocalLlmGenerationResult.BoundDiagnostic(diagnostic);
        }

        public LocalLlmGovernorMode Mode { get; }
        public LocalLlmGovernorReason Reasons { get; }
        public LocalLlmDeviceProfile DeviceProfile { get; }
        public LocalLlmExecutionProfile? EffectiveProfile { get; }
        public string Diagnostic { get; }
        public bool InferenceAllowed => Mode != LocalLlmGovernorMode.Suspended;
    }

    public sealed class LocalLlmResourceGovernor : IReachyPriorityDegradationTarget
    {
        public const int RecoverySamplesRequired = 3;

        private LocalLlmGovernorMode currentMode = LocalLlmGovernorMode.Nominal;
        private int recoverySamples;
        private bool outOfMemoryLatched;
        private int priorityMinimumMode;

        public LocalLlmGovernorMode CurrentMode => currentMode;
        public bool OutOfMemoryLatched => outOfMemoryLatched;

        public LocalLlmGovernorDecision Evaluate(
            LocalLlmExecutionProfile baselineProfile,
            LocalLlmResourceSnapshot snapshot)
        {
            if (baselineProfile == null)
            {
                throw new ArgumentNullException(nameof(baselineProfile));
            }
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            LocalLlmDeviceProfile deviceProfile = LocalLlmDeviceProfile.Select(
                snapshot.TotalMemoryBytes,
                snapshot.LogicalProcessorCount);
            LocalLlmGovernorReason reasons = LocalLlmGovernorReason.None;
            LocalLlmGovernorMode requestedMode = ClassifyPressure(snapshot, ref reasons);
            LocalLlmGovernorMode policyMinimumMode =
                (LocalLlmGovernorMode)Volatile.Read(ref priorityMinimumMode);
            if (policyMinimumMode != LocalLlmGovernorMode.Nominal)
            {
                reasons |= LocalLlmGovernorReason.PriorityDegradationPolicy;
                requestedMode = Stronger(requestedMode, policyMinimumMode);
            }

            if (baselineProfile.MaximumGeneratedTokens >= deviceProfile.MaximumContextTokens)
            {
                reasons |= LocalLlmGovernorReason.ProfileIncompatible;
                requestedMode = LocalLlmGovernorMode.Suspended;
            }

            if (IsDeviceLimited(baselineProfile, deviceProfile))
            {
                reasons |= LocalLlmGovernorReason.DeviceProfileLimit;
            }

            LocalLlmGovernorMode appliedMode;
            if (outOfMemoryLatched && requestedMode != LocalLlmGovernorMode.Nominal)
            {
                reasons |= LocalLlmGovernorReason.RecentOutOfMemory;
                currentMode = LocalLlmGovernorMode.Suspended;
                recoverySamples = 0;
                appliedMode = LocalLlmGovernorMode.Suspended;
            }
            else
            {
                appliedMode = ApplyHysteresis(requestedMode, ref reasons);
                if (outOfMemoryLatched)
                {
                    if (appliedMode == LocalLlmGovernorMode.Suspended)
                    {
                        reasons |= LocalLlmGovernorReason.RecentOutOfMemory;
                    }
                    else
                    {
                        outOfMemoryLatched = false;
                    }
                }
            }
            LocalLlmExecutionProfile? effectiveProfile = appliedMode == LocalLlmGovernorMode.Suspended
                ? null
                : BuildEffectiveProfile(
                    baselineProfile,
                    deviceProfile,
                    appliedMode);
            return new LocalLlmGovernorDecision(
                appliedMode,
                reasons,
                deviceProfile,
                effectiveProfile,
                BuildDiagnostic(appliedMode, reasons, deviceProfile, effectiveProfile));
        }

        public void RecordOutOfMemory()
        {
            currentMode = LocalLlmGovernorMode.Suspended;
            recoverySamples = 0;
            outOfMemoryLatched = true;
        }

        public void ApplyPriorityDegradation(
            ReachyPriorityDegradationDecision decision)
        {
            if (decision == null)
            {
                throw new ArgumentNullException(nameof(decision));
            }
            Volatile.Write(
                ref priorityMinimumMode,
                (int)decision.MinimumLocalLlmMode);
        }

        public void Reset()
        {
            currentMode = LocalLlmGovernorMode.Nominal;
            recoverySamples = 0;
            outOfMemoryLatched = false;
        }

        private static LocalLlmGovernorMode ClassifyPressure(
            LocalLlmResourceSnapshot snapshot,
            ref LocalLlmGovernorReason reasons)
        {
            LocalLlmGovernorMode mode = LocalLlmGovernorMode.Nominal;

            if (snapshot.RecentOutOfMemory)
            {
                reasons |= LocalLlmGovernorReason.RecentOutOfMemory;
                mode = LocalLlmGovernorMode.Suspended;
            }

            switch (snapshot.PhysicsBudgetState)
            {
                case LocalLlmPhysicsBudgetState.Unavailable:
                    reasons |= LocalLlmGovernorReason.PhysicsSignalUnavailable;
                    mode = LocalLlmGovernorMode.Suspended;
                    break;
                case LocalLlmPhysicsBudgetState.Healthy:
                    break;
                case LocalLlmPhysicsBudgetState.AtRisk:
                    reasons |= LocalLlmGovernorReason.PhysicsAtRisk;
                    mode = Stronger(mode, LocalLlmGovernorMode.Minimal);
                    break;
                case LocalLlmPhysicsBudgetState.Exceeded:
                    reasons |= LocalLlmGovernorReason.PhysicsBudgetExceeded;
                    mode = LocalLlmGovernorMode.Suspended;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot));
            }

            switch (snapshot.ThermalStatus)
            {
                case LocalLlmThermalStatus.Unavailable:
                    reasons |= LocalLlmGovernorReason.ThermalSignalUnavailable;
                    break;
                case LocalLlmThermalStatus.None:
                    break;
                case LocalLlmThermalStatus.Light:
                    reasons |= LocalLlmGovernorReason.ThermalLight;
                    mode = Stronger(mode, LocalLlmGovernorMode.Reduced);
                    break;
                case LocalLlmThermalStatus.Moderate:
                    reasons |= LocalLlmGovernorReason.ThermalModerate;
                    mode = Stronger(mode, LocalLlmGovernorMode.Minimal);
                    break;
                case LocalLlmThermalStatus.Severe:
                case LocalLlmThermalStatus.Critical:
                case LocalLlmThermalStatus.Emergency:
                case LocalLlmThermalStatus.Shutdown:
                    reasons |= LocalLlmGovernorReason.ThermalSevereOrWorse;
                    mode = LocalLlmGovernorMode.Suspended;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(snapshot));
            }

            if (snapshot.TotalMemoryBytes <= 0L)
            {
                reasons |= LocalLlmGovernorReason.MemorySignalUnavailable;
                return mode;
            }

            if (snapshot.SystemReportsLowMemory)
            {
                reasons |= LocalLlmGovernorReason.AndroidLowMemory;
                reasons |= LocalLlmGovernorReason.MemoryCritical;
                return LocalLlmGovernorMode.Suspended;
            }

            long criticalFloor = Math.Max(
                snapshot.LowMemoryThresholdBytes,
                snapshot.TotalMemoryBytes / 12L);
            long moderateFloor = Math.Max(
                SaturatingMultiply(snapshot.LowMemoryThresholdBytes, 2L),
                snapshot.TotalMemoryBytes / 100L * 15L);
            long reducedFloor = Math.Max(
                SaturatingMultiply(snapshot.LowMemoryThresholdBytes, 3L),
                snapshot.TotalMemoryBytes / 4L);

            if (snapshot.AvailableMemoryBytes <= criticalFloor)
            {
                reasons |= LocalLlmGovernorReason.MemoryCritical;
                return LocalLlmGovernorMode.Suspended;
            }
            if (snapshot.AvailableMemoryBytes <= moderateFloor)
            {
                reasons |= LocalLlmGovernorReason.MemoryPressure;
                return Stronger(mode, LocalLlmGovernorMode.Minimal);
            }
            if (snapshot.AvailableMemoryBytes <= reducedFloor)
            {
                reasons |= LocalLlmGovernorReason.MemoryPressure;
                return Stronger(mode, LocalLlmGovernorMode.Reduced);
            }
            return mode;
        }

        private LocalLlmGovernorMode ApplyHysteresis(
            LocalLlmGovernorMode requestedMode,
            ref LocalLlmGovernorReason reasons)
        {
            if (requestedMode >= currentMode)
            {
                currentMode = requestedMode;
                recoverySamples = 0;
                return currentMode;
            }

            ++recoverySamples;
            if (recoverySamples < RecoverySamplesRequired)
            {
                reasons |= LocalLlmGovernorReason.RecoveryHold;
                return currentMode;
            }

            currentMode = requestedMode;
            recoverySamples = 0;
            return currentMode;
        }

        private static LocalLlmExecutionProfile BuildEffectiveProfile(
            LocalLlmExecutionProfile baseline,
            LocalLlmDeviceProfile device,
            LocalLlmGovernorMode mode)
        {
            int context = Math.Min(baseline.ContextTokens, device.MaximumContextTokens);
            int batch = Math.Min(baseline.BatchTokens, device.MaximumBatchTokens);
            int microBatch = Math.Min(
                baseline.MicroBatchTokens,
                device.MaximumMicroBatchTokens);
            int threads = Math.Min(baseline.Threads, device.MaximumThreads);
            int batchThreads = Math.Min(
                baseline.BatchThreads,
                device.MaximumBatchThreads);

            if (mode == LocalLlmGovernorMode.Reduced)
            {
                context = ScaleContext(context, baseline.MaximumGeneratedTokens, 3, 4);
                batch = Math.Max(1, batch / 2);
                threads = Math.Max(1, threads - 1);
                batchThreads = Math.Max(1, batchThreads - 1);
            }
            else if (mode == LocalLlmGovernorMode.Minimal)
            {
                context = ScaleContext(context, baseline.MaximumGeneratedTokens, 1, 2);
                batch = Math.Max(1, batch / 4);
                threads = 1;
                batchThreads = 1;
            }

            batch = Math.Min(batch, context);
            microBatch = Math.Min(microBatch, batch);
            microBatch = Math.Max(1, microBatch);

            return new LocalLlmExecutionProfile(
                context,
                batch,
                microBatch,
                baseline.MaximumGeneratedTokens,
                threads,
                batchThreads,
                baseline.Temperature,
                baseline.MinP,
                baseline.Seed,
                baseline.StreamQueueCapacity,
                baseline.MaximumConversationMessages,
                baseline.MaximumMessageCharacters,
                baseline.MaximumResponseUtf8Bytes);
        }

        private static int ScaleContext(
            int context,
            int maximumGeneratedTokens,
            int numerator,
            int denominator)
        {
            int minimum = checked(maximumGeneratedTokens + 1);
            long scaled = (long)context * numerator / denominator;
            return Math.Min(context, Math.Max(minimum, checked((int)scaled)));
        }

        private static bool IsDeviceLimited(
            LocalLlmExecutionProfile baseline,
            LocalLlmDeviceProfile device)
        {
            return baseline.ContextTokens > device.MaximumContextTokens ||
                baseline.BatchTokens > device.MaximumBatchTokens ||
                baseline.MicroBatchTokens > device.MaximumMicroBatchTokens ||
                baseline.Threads > device.MaximumThreads ||
                baseline.BatchThreads > device.MaximumBatchThreads;
        }

        private static LocalLlmGovernorMode Stronger(
            LocalLlmGovernorMode left,
            LocalLlmGovernorMode right)
        {
            return left >= right ? left : right;
        }

        private static long SaturatingMultiply(long value, long factor)
        {
            if (value <= 0L || factor <= 0L)
            {
                return 0L;
            }
            return value > long.MaxValue / factor
                ? long.MaxValue
                : value * factor;
        }

        private static string BuildDiagnostic(
            LocalLlmGovernorMode mode,
            LocalLlmGovernorReason reasons,
            LocalLlmDeviceProfile device,
            LocalLlmExecutionProfile? effectiveProfile)
        {
            string profile = effectiveProfile == null
                ? "suspended"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "ctx={0} batch={1} ubatch={2} threads={3}/{4}",
                    effectiveProfile.ContextTokens,
                    effectiveProfile.BatchTokens,
                    effectiveProfile.MicroBatchTokens,
                    effectiveProfile.Threads,
                    effectiveProfile.BatchThreads);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Local LLM governor mode={0}; device={1}; reasons={2}; {3}.",
                mode,
                device.Kind,
                reasons,
                profile);
        }
    }
}

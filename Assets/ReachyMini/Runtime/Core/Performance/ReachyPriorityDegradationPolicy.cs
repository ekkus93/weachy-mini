#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReachyMini.LocalModels;

namespace ReachyMini.Performance
{
    public enum ReachyPriorityDegradationLevel
    {
        Nominal = 0,
        RenderReduced = 1,
        CameraReduced = 2,
        VlmSuspended = 3,
        LlmReduced = 4,
        Critical = 5,
    }

    [Flags]
    public enum ReachyPriorityDegradationReason
    {
        None = 0,
        RenderFramePressure = 1 << 0,
        ThermalLight = 1 << 1,
        ThermalModerate = 1 << 2,
        ThermalSevereOrWorse = 1 << 3,
        ThermalSignalUnavailable = 1 << 4,
        MemoryPressure = 1 << 5,
        MemoryCritical = 1 << 6,
        MemorySignalUnavailable = 1 << 7,
        AndroidLowMemory = 1 << 8,
        PhysicsAtRisk = 1 << 9,
        PhysicsBudgetExceeded = 1 << 10,
        PhysicsSignalUnavailable = 1 << 11,
        RecoveryHold = 1 << 12,
    }

    public sealed class ReachyPriorityDegradationSignals
    {
        public ReachyPriorityDegradationSignals(
            long totalMemoryBytes,
            long availableMemoryBytes,
            long lowMemoryThresholdBytes,
            bool systemReportsLowMemory,
            LocalLlmThermalStatus thermalStatus,
            LocalLlmPhysicsBudgetState physicsBudgetState,
            double? recentRenderP95Milliseconds = null)
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
            if (!Enum.IsDefined(typeof(LocalLlmThermalStatus), thermalStatus))
            {
                throw new ArgumentOutOfRangeException(nameof(thermalStatus));
            }
            if (!Enum.IsDefined(typeof(LocalLlmPhysicsBudgetState), physicsBudgetState))
            {
                throw new ArgumentOutOfRangeException(nameof(physicsBudgetState));
            }
            if (recentRenderP95Milliseconds.HasValue &&
                (double.IsNaN(recentRenderP95Milliseconds.Value) ||
                 double.IsInfinity(recentRenderP95Milliseconds.Value) ||
                 recentRenderP95Milliseconds.Value <= 0.0 ||
                 recentRenderP95Milliseconds.Value > 60_000.0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(recentRenderP95Milliseconds));
            }

            TotalMemoryBytes = totalMemoryBytes;
            AvailableMemoryBytes = availableMemoryBytes;
            LowMemoryThresholdBytes = lowMemoryThresholdBytes;
            SystemReportsLowMemory = systemReportsLowMemory;
            ThermalStatus = thermalStatus;
            PhysicsBudgetState = physicsBudgetState;
            RecentRenderP95Milliseconds = recentRenderP95Milliseconds;
        }

        public long TotalMemoryBytes { get; }

        public long AvailableMemoryBytes { get; }

        public long LowMemoryThresholdBytes { get; }

        public bool SystemReportsLowMemory { get; }

        public LocalLlmThermalStatus ThermalStatus { get; }

        public LocalLlmPhysicsBudgetState PhysicsBudgetState { get; }

        public double? RecentRenderP95Milliseconds { get; }

        public static ReachyPriorityDegradationSignals FromResourceSnapshot(
            LocalLlmResourceSnapshot snapshot,
            double? recentRenderP95Milliseconds = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new ReachyPriorityDegradationSignals(
                snapshot.TotalMemoryBytes,
                snapshot.AvailableMemoryBytes,
                snapshot.LowMemoryThresholdBytes,
                snapshot.SystemReportsLowMemory,
                snapshot.ThermalStatus,
                snapshot.PhysicsBudgetState,
                recentRenderP95Milliseconds);
        }
    }

    public sealed class ReachyPriorityDegradationDecision
    {
        private readonly double physicsTimestepSeconds;
        private readonly bool physicsStepSkippingAllowed;
        private readonly bool audioInteractionPreserved;

        internal ReachyPriorityDegradationDecision(
            ReachyPriorityDegradationLevel level,
            ReachyPriorityDegradationReason reasons,
            int targetRenderFps,
            bool expensiveVisualEffectsAllowed,
            int trackingMaximumDimension,
            long trackingMinimumIntervalNanoseconds,
            bool vlmAllowed,
            LocalLlmGovernorMode minimumLocalLlmMode)
        {
            Level = level;
            Reasons = reasons;
            TargetRenderFps = targetRenderFps;
            ExpensiveVisualEffectsAllowed = expensiveVisualEffectsAllowed;
            TrackingMaximumDimension = trackingMaximumDimension;
            TrackingMinimumIntervalNanoseconds =
                trackingMinimumIntervalNanoseconds;
            VlmAllowed = vlmAllowed;
            MinimumLocalLlmMode = minimumLocalLlmMode;
            physicsTimestepSeconds =
                ReachyMini.Core.ProjectMetadata.InitialPhysicsTimestepSeconds;
            physicsStepSkippingAllowed = false;
            audioInteractionPreserved = true;
        }

        public ReachyPriorityDegradationLevel Level { get; }

        public ReachyPriorityDegradationReason Reasons { get; }

        public int TargetRenderFps { get; }

        public bool ExpensiveVisualEffectsAllowed { get; }

        public int TrackingMaximumDimension { get; }

        public long TrackingMinimumIntervalNanoseconds { get; }

        public bool VlmAllowed { get; }

        public LocalLlmGovernorMode MinimumLocalLlmMode { get; }

        public double PhysicsTimestepSeconds => physicsTimestepSeconds;

        public bool PhysicsStepSkippingAllowed => physicsStepSkippingAllowed;

        public bool AudioInteractionPreserved => audioInteractionPreserved;

        public string Diagnostic =>
            "RMA-181 degradation level=" + Level +
            "; reasons=" + Reasons +
            "; render_fps=" + TargetRenderFps +
            "; tracking_dimension=" + TrackingMaximumDimension +
            "; tracking_interval_ns=" + TrackingMinimumIntervalNanoseconds +
            "; vlm_allowed=" + VlmAllowed +
            "; llm_floor=" + MinimumLocalLlmMode +
            "; physics_timestep_s=" + PhysicsTimestepSeconds +
            "; physics_step_skipping=false.";
    }

    public interface IReachyPriorityDegradationTarget
    {
        void ApplyPriorityDegradation(
            ReachyPriorityDegradationDecision decision);
    }

    public sealed class ReachyPriorityDegradationCoordinator
    {
        public const int MaximumTargets = 16;

        private readonly ReachyPriorityDegradationPolicy policy;
        private readonly ReadOnlyCollection<IReachyPriorityDegradationTarget> targets;

        public ReachyPriorityDegradationCoordinator(
            ReachyPriorityDegradationPolicy policy,
            IReadOnlyList<IReachyPriorityDegradationTarget> targets)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }
            if (targets.Count > MaximumTargets)
            {
                throw new ArgumentOutOfRangeException(nameof(targets));
            }

            var copy = new IReachyPriorityDegradationTarget[targets.Count];
            for (int index = 0; index < targets.Count; ++index)
            {
                copy[index] = targets[index] ??
                    throw new ArgumentException(
                        "Degradation targets cannot contain null entries.",
                        nameof(targets));
            }
            this.targets = Array.AsReadOnly(copy);
            CurrentDecision = policy.CurrentDecision;
        }

        public ReachyPriorityDegradationDecision CurrentDecision { get; private set; }

        public ReachyPriorityDegradationDecision EvaluateAndApply(
            ReachyPriorityDegradationSignals signals)
        {
            ReachyPriorityDegradationDecision decision = policy.Evaluate(signals);
            for (int index = 0; index < targets.Count; ++index)
            {
                targets[index].ApplyPriorityDegradation(decision);
            }
            CurrentDecision = decision;
            return decision;
        }
    }

    public sealed class ReachyPriorityDegradationPolicy
    {
        public const int RecoverySamplesRequired = 3;
        public const int NominalTrackingMaximumDimension = 640;

        private const long NanosecondsPerSecond = 1_000_000_000L;
        private readonly int nominalRenderFps;
        private ReachyPriorityDegradationLevel currentLevel;
        private int recoverySamples;

        public ReachyPriorityDegradationPolicy(int nominalRenderFps = 60)
        {
            if (nominalRenderFps != 30 && nominalRenderFps != 60)
            {
                throw new ArgumentOutOfRangeException(nameof(nominalRenderFps));
            }
            this.nominalRenderFps = nominalRenderFps;
            CurrentDecision = BuildDecision(
                ReachyPriorityDegradationLevel.Nominal,
                ReachyPriorityDegradationReason.None);
        }

        public int NominalRenderFps => nominalRenderFps;

        public ReachyPriorityDegradationLevel CurrentLevel => currentLevel;

        public ReachyPriorityDegradationDecision CurrentDecision { get; private set; }

        public ReachyPriorityDegradationDecision Evaluate(
            ReachyPriorityDegradationSignals signals)
        {
            if (signals == null)
            {
                throw new ArgumentNullException(nameof(signals));
            }

            ReachyPriorityDegradationReason reasons =
                ReachyPriorityDegradationReason.None;
            ReachyPriorityDegradationLevel requested =
                Classify(signals, ref reasons);
            ReachyPriorityDegradationLevel applied =
                ApplyHysteresis(requested, ref reasons);
            CurrentDecision = BuildDecision(applied, reasons);
            return CurrentDecision;
        }

        public void Reset()
        {
            currentLevel = ReachyPriorityDegradationLevel.Nominal;
            recoverySamples = 0;
            CurrentDecision = BuildDecision(
                currentLevel,
                ReachyPriorityDegradationReason.None);
        }

        private ReachyPriorityDegradationLevel Classify(
            ReachyPriorityDegradationSignals signals,
            ref ReachyPriorityDegradationReason reasons)
        {
            ReachyPriorityDegradationLevel level =
                ReachyPriorityDegradationLevel.Nominal;

            if (signals.RecentRenderP95Milliseconds.HasValue)
            {
                double frameBudgetMilliseconds = 1000.0 / nominalRenderFps;
                double p95 = signals.RecentRenderP95Milliseconds.Value;
                if (p95 > frameBudgetMilliseconds * 1.50)
                {
                    reasons |= ReachyPriorityDegradationReason.RenderFramePressure;
                    level = Stronger(
                        level,
                        ReachyPriorityDegradationLevel.CameraReduced);
                }
                else if (p95 > frameBudgetMilliseconds * 1.15)
                {
                    reasons |= ReachyPriorityDegradationReason.RenderFramePressure;
                    level = Stronger(
                        level,
                        ReachyPriorityDegradationLevel.RenderReduced);
                }
            }

            switch (signals.PhysicsBudgetState)
            {
                case LocalLlmPhysicsBudgetState.Unavailable:
                    reasons |= ReachyPriorityDegradationReason.PhysicsSignalUnavailable;
                    break;
                case LocalLlmPhysicsBudgetState.Healthy:
                    break;
                case LocalLlmPhysicsBudgetState.AtRisk:
                    reasons |= ReachyPriorityDegradationReason.PhysicsAtRisk;
                    level = Stronger(
                        level,
                        ReachyPriorityDegradationLevel.LlmReduced);
                    break;
                case LocalLlmPhysicsBudgetState.Exceeded:
                    reasons |= ReachyPriorityDegradationReason.PhysicsBudgetExceeded;
                    level = ReachyPriorityDegradationLevel.Critical;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(signals));
            }

            switch (signals.ThermalStatus)
            {
                case LocalLlmThermalStatus.Unavailable:
                    reasons |= ReachyPriorityDegradationReason.ThermalSignalUnavailable;
                    break;
                case LocalLlmThermalStatus.None:
                    break;
                case LocalLlmThermalStatus.Light:
                    reasons |= ReachyPriorityDegradationReason.ThermalLight;
                    level = Stronger(
                        level,
                        ReachyPriorityDegradationLevel.RenderReduced);
                    break;
                case LocalLlmThermalStatus.Moderate:
                    reasons |= ReachyPriorityDegradationReason.ThermalModerate;
                    level = Stronger(
                        level,
                        ReachyPriorityDegradationLevel.VlmSuspended);
                    break;
                case LocalLlmThermalStatus.Severe:
                case LocalLlmThermalStatus.Critical:
                case LocalLlmThermalStatus.Emergency:
                case LocalLlmThermalStatus.Shutdown:
                    reasons |= ReachyPriorityDegradationReason.ThermalSevereOrWorse;
                    level = ReachyPriorityDegradationLevel.Critical;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(signals));
            }

            if (signals.TotalMemoryBytes <= 0L)
            {
                reasons |= ReachyPriorityDegradationReason.MemorySignalUnavailable;
                return level;
            }

            if (signals.SystemReportsLowMemory)
            {
                reasons |= ReachyPriorityDegradationReason.AndroidLowMemory;
                reasons |= ReachyPriorityDegradationReason.MemoryCritical;
                return ReachyPriorityDegradationLevel.Critical;
            }

            long criticalFloor = Math.Max(
                signals.LowMemoryThresholdBytes,
                signals.TotalMemoryBytes / 12L);
            long llmFloor = Math.Max(
                SaturatingMultiply(signals.LowMemoryThresholdBytes, 2L),
                signals.TotalMemoryBytes / 100L * 15L);
            long cameraFloor = Math.Max(
                SaturatingMultiply(signals.LowMemoryThresholdBytes, 3L),
                signals.TotalMemoryBytes / 4L);

            if (signals.AvailableMemoryBytes <= criticalFloor)
            {
                reasons |= ReachyPriorityDegradationReason.MemoryCritical;
                return ReachyPriorityDegradationLevel.Critical;
            }
            if (signals.AvailableMemoryBytes <= llmFloor)
            {
                reasons |= ReachyPriorityDegradationReason.MemoryPressure;
                return Stronger(
                    level,
                    ReachyPriorityDegradationLevel.LlmReduced);
            }
            if (signals.AvailableMemoryBytes <= cameraFloor)
            {
                reasons |= ReachyPriorityDegradationReason.MemoryPressure;
                return Stronger(
                    level,
                    ReachyPriorityDegradationLevel.CameraReduced);
            }
            return level;
        }

        private ReachyPriorityDegradationLevel ApplyHysteresis(
            ReachyPriorityDegradationLevel requested,
            ref ReachyPriorityDegradationReason reasons)
        {
            if (requested >= currentLevel)
            {
                currentLevel = requested;
                recoverySamples = 0;
                return currentLevel;
            }

            ++recoverySamples;
            if (recoverySamples < RecoverySamplesRequired)
            {
                reasons |= ReachyPriorityDegradationReason.RecoveryHold;
                return currentLevel;
            }

            currentLevel = requested;
            recoverySamples = 0;
            return currentLevel;
        }

        private ReachyPriorityDegradationDecision BuildDecision(
            ReachyPriorityDegradationLevel level,
            ReachyPriorityDegradationReason reasons)
        {
            int reducedRenderFps = nominalRenderFps == 60 ? 30 : 20;
            switch (level)
            {
                case ReachyPriorityDegradationLevel.Nominal:
                    return new ReachyPriorityDegradationDecision(
                        level,
                        reasons,
                        nominalRenderFps,
                        expensiveVisualEffectsAllowed: true,
                        NominalTrackingMaximumDimension,
                        trackingMinimumIntervalNanoseconds: 0L,
                        vlmAllowed: true,
                        LocalLlmGovernorMode.Nominal);
                case ReachyPriorityDegradationLevel.RenderReduced:
                    return new ReachyPriorityDegradationDecision(
                        level,
                        reasons,
                        reducedRenderFps,
                        expensiveVisualEffectsAllowed: false,
                        NominalTrackingMaximumDimension,
                        trackingMinimumIntervalNanoseconds: 0L,
                        vlmAllowed: true,
                        LocalLlmGovernorMode.Nominal);
                case ReachyPriorityDegradationLevel.CameraReduced:
                    return new ReachyPriorityDegradationDecision(
                        level,
                        reasons,
                        reducedRenderFps,
                        expensiveVisualEffectsAllowed: false,
                        trackingMaximumDimension: 480,
                        trackingMinimumIntervalNanoseconds:
                            NanosecondsPerSecond / 15L,
                        vlmAllowed: true,
                        LocalLlmGovernorMode.Nominal);
                case ReachyPriorityDegradationLevel.VlmSuspended:
                    return new ReachyPriorityDegradationDecision(
                        level,
                        reasons,
                        reducedRenderFps,
                        expensiveVisualEffectsAllowed: false,
                        trackingMaximumDimension: 320,
                        trackingMinimumIntervalNanoseconds:
                            NanosecondsPerSecond / 10L,
                        vlmAllowed: false,
                        LocalLlmGovernorMode.Nominal);
                case ReachyPriorityDegradationLevel.LlmReduced:
                    return new ReachyPriorityDegradationDecision(
                        level,
                        reasons,
                        reducedRenderFps,
                        expensiveVisualEffectsAllowed: false,
                        trackingMaximumDimension: 256,
                        trackingMinimumIntervalNanoseconds:
                            NanosecondsPerSecond / 8L,
                        vlmAllowed: false,
                        LocalLlmGovernorMode.Minimal);
                case ReachyPriorityDegradationLevel.Critical:
                    return new ReachyPriorityDegradationDecision(
                        level,
                        reasons,
                        targetRenderFps: 15,
                        expensiveVisualEffectsAllowed: false,
                        trackingMaximumDimension: 192,
                        trackingMinimumIntervalNanoseconds:
                            NanosecondsPerSecond / 5L,
                        vlmAllowed: false,
                        LocalLlmGovernorMode.Suspended);
                default:
                    throw new ArgumentOutOfRangeException(nameof(level));
            }
        }

        private static ReachyPriorityDegradationLevel Stronger(
            ReachyPriorityDegradationLevel left,
            ReachyPriorityDegradationLevel right)
        {
            return left >= right ? left : right;
        }

        private static long SaturatingMultiply(long value, long multiplier)
        {
            if (value <= 0L || multiplier <= 0L)
            {
                return 0L;
            }
            if (value > long.MaxValue / multiplier)
            {
                return long.MaxValue;
            }
            return value * multiplier;
        }
    }
}

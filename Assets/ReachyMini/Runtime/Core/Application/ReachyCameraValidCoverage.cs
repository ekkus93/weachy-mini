#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyCameraCoverageState
    {
        Unavailable = 0,
        Normal = 1,
        Degraded = 2,
        Unusable = 3,
    }

    public enum ReachyCameraCoveragePublishStatus
    {
        Accepted = 0,
        Duplicate = 1,
        StaleFrame = 2,
        IdentityMismatch = 3,
    }

    public sealed class ReachyCameraCoveragePolicy
    {
        public static ReachyCameraCoveragePolicy EngineeringBaseline { get; } =
            new ReachyCameraCoveragePolicy(
                unusableEnterMaximum: 0.25,
                unusableExitMinimum: 0.35,
                normalExitMinimum: 0.65,
                normalEnterMinimum: 0.75,
                stopVisionDrivenTurningMaximum: 0.35);

        public ReachyCameraCoveragePolicy(
            double unusableEnterMaximum,
            double unusableExitMinimum,
            double normalExitMinimum,
            double normalEnterMinimum,
            double stopVisionDrivenTurningMaximum)
        {
            RequireFraction(
                unusableEnterMaximum,
                nameof(unusableEnterMaximum));
            RequireFraction(
                unusableExitMinimum,
                nameof(unusableExitMinimum));
            RequireFraction(
                normalExitMinimum,
                nameof(normalExitMinimum));
            RequireFraction(
                normalEnterMinimum,
                nameof(normalEnterMinimum));
            RequireFraction(
                stopVisionDrivenTurningMaximum,
                nameof(stopVisionDrivenTurningMaximum));

            if (unusableEnterMaximum >= unusableExitMinimum ||
                unusableExitMinimum > normalExitMinimum ||
                normalExitMinimum >= normalEnterMinimum)
            {
                throw new ArgumentException(
                    "Coverage hysteresis thresholds must be ordered as " +
                    "unusable-enter < unusable-exit <= normal-exit < normal-enter.");
            }
            if (stopVisionDrivenTurningMaximum <= unusableEnterMaximum ||
                stopVisionDrivenTurningMaximum > normalExitMinimum)
            {
                throw new ArgumentException(
                    "The vision-turning stop threshold must act before the " +
                    "unusable threshold and no later than the normal-exit threshold.");
            }

            UnusableEnterMaximum = unusableEnterMaximum;
            UnusableExitMinimum = unusableExitMinimum;
            NormalExitMinimum = normalExitMinimum;
            NormalEnterMinimum = normalEnterMinimum;
            StopVisionDrivenTurningMaximum =
                stopVisionDrivenTurningMaximum;
        }

        public double UnusableEnterMaximum { get; }

        public double UnusableExitMinimum { get; }

        public double NormalExitMinimum { get; }

        public double NormalEnterMinimum { get; }

        public double StopVisionDrivenTurningMaximum { get; }

        public ReachyCameraCoverageState Classify(
            double coverageFraction,
            ReachyCameraCoverageState previous)
        {
            RequireFraction(coverageFraction, nameof(coverageFraction));
            switch (previous)
            {
                case ReachyCameraCoverageState.Normal:
                    if (coverageFraction <= UnusableEnterMaximum)
                    {
                        return ReachyCameraCoverageState.Unusable;
                    }
                    return coverageFraction < NormalExitMinimum
                        ? ReachyCameraCoverageState.Degraded
                        : ReachyCameraCoverageState.Normal;

                case ReachyCameraCoverageState.Degraded:
                    if (coverageFraction <= UnusableEnterMaximum)
                    {
                        return ReachyCameraCoverageState.Unusable;
                    }
                    return coverageFraction >= NormalEnterMinimum
                        ? ReachyCameraCoverageState.Normal
                        : ReachyCameraCoverageState.Degraded;

                case ReachyCameraCoverageState.Unusable:
                    if (coverageFraction < UnusableExitMinimum)
                    {
                        return ReachyCameraCoverageState.Unusable;
                    }
                    return coverageFraction >= NormalEnterMinimum
                        ? ReachyCameraCoverageState.Normal
                        : ReachyCameraCoverageState.Degraded;

                case ReachyCameraCoverageState.Unavailable:
                    if (coverageFraction <= UnusableEnterMaximum)
                    {
                        return ReachyCameraCoverageState.Unusable;
                    }
                    return coverageFraction >= NormalEnterMinimum
                        ? ReachyCameraCoverageState.Normal
                        : ReachyCameraCoverageState.Degraded;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(previous),
                        previous,
                        "Unknown coverage state.");
            }
        }

        public bool ShouldStopVisionDrivenTurning(
            double coverageFraction,
            ReachyCameraCoverageState state)
        {
            RequireFraction(coverageFraction, nameof(coverageFraction));
            return state == ReachyCameraCoverageState.Unavailable ||
                state == ReachyCameraCoverageState.Unusable ||
                coverageFraction <= StopVisionDrivenTurningMaximum;
        }

        private static void RequireFraction(double value, string name)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0.0 ||
                value > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Coverage values must be finite fractions in [0, 1].");
            }
        }
    }

    public sealed class ReachyCameraCoverageMeasurement
    {
        internal ReachyCameraCoverageMeasurement(
            ReachyCameraHomographyPlan plan,
            long validPixelCount,
            long totalPixelCount)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (totalPixelCount <= 0L ||
                validPixelCount < 0L ||
                validPixelCount > totalPixelCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(validPixelCount),
                    validPixelCount,
                    "Coverage counts must satisfy 0 <= valid <= total.");
            }

            CalibrationProfileId = plan.CalibrationProfileId;
            CameraId = plan.CameraId;
            Facing = plan.Facing;
            ModelCompatibility = plan.ModelCompatibility;
            SourceSessionId = plan.SourceSessionId;
            SourceSequence = plan.SourceSequence;
            SourceTimestampNanoseconds =
                plan.SourceTimestampNanoseconds;
            OutputWidth = plan.OutputWidth;
            OutputHeight = plan.OutputHeight;
            ModelHash = plan.ModelHash;
            AuthoritativeSequence = plan.AuthoritativeSequence;
            SimulationTimeSeconds = plan.SimulationTimeSeconds;
            ContinuityId = plan.ContinuityId;
            PhoneOrientationTimestampNanoseconds =
                plan.PhoneOrientationTimestampNanoseconds;
            CameraBodyId = plan.CameraBodyId;
            ReachyToPhonePixels = plan.ReachyToPhonePixels;
            ValidPixelCount = validPixelCount;
            TotalPixelCount = totalPixelCount;
            CoverageFraction =
                (double)validPixelCount / totalPixelCount;
        }

        public string CalibrationProfileId { get; }

        public string CameraId { get; }

        public ReachyDeviceCameraFacing Facing { get; }

        public string ModelCompatibility { get; }

        public ulong SourceSessionId { get; }

        public ulong SourceSequence { get; }

        public long SourceTimestampNanoseconds { get; }

        public int OutputWidth { get; }

        public int OutputHeight { get; }

        public ulong ModelHash { get; }

        public ulong AuthoritativeSequence { get; }

        public double SimulationTimeSeconds { get; }

        public uint ContinuityId { get; }

        public long PhoneOrientationTimestampNanoseconds { get; }

        public uint CameraBodyId { get; }

        public ReachyMatrix3x3 ReachyToPhonePixels { get; }

        public long ValidPixelCount { get; }

        public long TotalPixelCount { get; }

        public double CoverageFraction { get; }

        public double CoveragePercent => CoverageFraction * 100.0;
    }

    public static class ReachyCameraValidCoverageCalculator
    {
        public const double ShaderDepthEpsilon = 1.0e-6;

        public static ReachyCameraCoverageMeasurement Calculate(
            ReachyCameraHomographyPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            long totalPixelCount = checked(
                (long)plan.OutputWidth * plan.OutputHeight);
            long validPixelCount = 0L;
            ReachyMatrix3x3 matrix = plan.ReachyToPhonePixels;
            double sourceMaximumX = plan.SourceWidth - 1.0;
            double sourceMaximumY = plan.SourceHeight - 1.0;

            for (int y = 0; y < plan.OutputHeight; ++y)
            {
                int lower = 0;
                int upper = plan.OutputWidth - 1;

                if (!ApplyConstraint(
                        ref lower,
                        ref upper,
                        matrix.M20,
                        matrix.M21 * y + matrix.M22 -
                            ReachyCameraValidCoverageCalculator
                                .ShaderDepthEpsilon,
                        strict: true) ||
                    !ApplyConstraint(
                        ref lower,
                        ref upper,
                        matrix.M00,
                        matrix.M01 * y + matrix.M02,
                        strict: false) ||
                    !ApplyConstraint(
                        ref lower,
                        ref upper,
                        sourceMaximumX * matrix.M20 - matrix.M00,
                        (sourceMaximumX * matrix.M21 - matrix.M01) * y +
                            sourceMaximumX * matrix.M22 - matrix.M02,
                        strict: false) ||
                    !ApplyConstraint(
                        ref lower,
                        ref upper,
                        matrix.M10,
                        matrix.M11 * y + matrix.M12,
                        strict: false) ||
                    !ApplyConstraint(
                        ref lower,
                        ref upper,
                        sourceMaximumY * matrix.M20 - matrix.M10,
                        (sourceMaximumY * matrix.M21 - matrix.M11) * y +
                            sourceMaximumY * matrix.M22 - matrix.M12,
                        strict: false))
                {
                    continue;
                }

                validPixelCount = checked(
                    validPixelCount + upper - lower + 1L);
            }

            return new ReachyCameraCoverageMeasurement(
                plan,
                validPixelCount,
                totalPixelCount);
        }

        private static bool ApplyConstraint(
            ref int lower,
            ref int upper,
            double xCoefficient,
            double rowConstant,
            bool strict)
        {
            if (lower > upper)
            {
                return false;
            }

            if (xCoefficient == 0.0)
            {
                return IsSatisfied(rowConstant, strict);
            }

            if (xCoefficient > 0.0)
            {
                if (IsSatisfied(
                        xCoefficient * lower + rowConstant,
                        strict))
                {
                    return true;
                }
                if (!IsSatisfied(
                        xCoefficient * upper + rowConstant,
                        strict))
                {
                    return false;
                }

                int low = lower;
                int high = upper;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (IsSatisfied(
                            xCoefficient * middle + rowConstant,
                            strict))
                    {
                        high = middle;
                    }
                    else
                    {
                        low = middle + 1;
                    }
                }
                lower = low;
                return lower <= upper;
            }

            if (IsSatisfied(
                    xCoefficient * upper + rowConstant,
                    strict))
            {
                return true;
            }
            if (!IsSatisfied(
                    xCoefficient * lower + rowConstant,
                    strict))
            {
                return false;
            }

            int lastLow = lower;
            int lastHigh = upper;
            while (lastLow < lastHigh)
            {
                int middle =
                    lastLow + (lastHigh - lastLow + 1) / 2;
                if (IsSatisfied(
                        xCoefficient * middle + rowConstant,
                        strict))
                {
                    lastLow = middle;
                }
                else
                {
                    lastHigh = middle - 1;
                }
            }
            upper = lastLow;
            return lower <= upper;
        }

        private static bool IsSatisfied(double value, bool strict)
        {
            return strict ? value > 0.0 : value >= 0.0;
        }
    }

    public sealed class ReachyCameraCoverageSnapshot
    {
        private ReachyCameraCoverageSnapshot(
            ReachyCameraCoverageState state,
            ReachyCameraCoverageMeasurement? measurement,
            bool shouldStopVisionDrivenTurning,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Coverage snapshots require diagnostics.",
                    nameof(message));
            }
            bool available = measurement != null;
            if (available ==
                (state == ReachyCameraCoverageState.Unavailable))
            {
                throw new ArgumentException(
                    "Coverage state and measurement availability disagree.",
                    nameof(state));
            }

            State = state;
            Measurement = measurement;
            ShouldStopVisionDrivenTurning =
                shouldStopVisionDrivenTurning;
            Message = message;
        }

        public ReachyCameraCoverageState State { get; }

        public ReachyCameraCoverageMeasurement? Measurement { get; }

        public bool HasCoverage => Measurement != null;

        public bool HasValidityMask => HasCoverage;

        public bool CanCreateVisualObservations =>
            State == ReachyCameraCoverageState.Normal ||
            State == ReachyCameraCoverageState.Degraded;

        public bool CoverageDisclosureRequired =>
            State != ReachyCameraCoverageState.Normal;

        public bool ShouldStopVisionDrivenTurning { get; }

        public string Message { get; }

        internal static ReachyCameraCoverageSnapshot Available(
            ReachyCameraCoverageState state,
            ReachyCameraCoverageMeasurement measurement,
            ReachyCameraCoveragePolicy policy)
        {
            if (state == ReachyCameraCoverageState.Unavailable)
            {
                throw new ArgumentException(
                    "An available coverage sample cannot be unavailable.",
                    nameof(state));
            }
            return new ReachyCameraCoverageSnapshot(
                state,
                measurement,
                policy.ShouldStopVisionDrivenTurning(
                    measurement.CoverageFraction,
                    state),
                $"Valid coverage is {measurement.CoveragePercent:F2}% " +
                $"({state}).");
        }

        public static ReachyCameraCoverageSnapshot Unavailable(
            string message)
        {
            return new ReachyCameraCoverageSnapshot(
                ReachyCameraCoverageState.Unavailable,
                null,
                shouldStopVisionDrivenTurning: true,
                message);
        }
    }

    public sealed class ReachyCameraCoveragePublishResult
    {
        internal ReachyCameraCoveragePublishResult(
            ReachyCameraCoveragePublishStatus status,
            ReachyCameraCoverageSnapshot snapshot,
            string message)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Coverage publication requires diagnostics.",
                    nameof(message));
            }

            Status = status;
            Snapshot = snapshot;
            Message = message;
        }

        public ReachyCameraCoveragePublishStatus Status { get; }

        public ReachyCameraCoverageSnapshot Snapshot { get; }

        public string Message { get; }

        public bool Succeeded =>
            Status == ReachyCameraCoveragePublishStatus.Accepted ||
            Status == ReachyCameraCoveragePublishStatus.Duplicate;
    }

    public sealed class ReachyCameraCoverageStateMachine
    {
        private readonly ReachyCameraCoveragePolicy policy;
        private ReachyCameraCoverageSnapshot current;

        public ReachyCameraCoverageStateMachine(
            ReachyCameraCoveragePolicy? policy = null)
        {
            this.policy =
                policy ?? ReachyCameraCoveragePolicy.EngineeringBaseline;
            current = ReachyCameraCoverageSnapshot.Unavailable(
                "No transformed camera frame has published coverage.");
        }

        public ReachyCameraCoverageSnapshot Current => current;

        public ReachyCameraCoveragePublishResult Publish(
            ReachyCameraCoverageMeasurement measurement)
        {
            if (measurement == null)
            {
                throw new ArgumentNullException(nameof(measurement));
            }

            ReachyCameraCoverageMeasurement? previous =
                current.Measurement;
            bool sameEpoch = previous != null &&
                previous.SourceSessionId ==
                    measurement.SourceSessionId &&
                previous.ContinuityId == measurement.ContinuityId;
            if (sameEpoch)
            {
                ReachyCameraCoveragePublishResult? rejection =
                    ValidateSameEpoch(previous!, measurement);
                if (rejection != null)
                {
                    return rejection;
                }

                if (previous!.SourceSequence ==
                        measurement.SourceSequence &&
                    previous.AuthoritativeSequence ==
                        measurement.AuthoritativeSequence)
                {
                    if (previous.ValidPixelCount !=
                            measurement.ValidPixelCount ||
                        previous.TotalPixelCount !=
                            measurement.TotalPixelCount ||
                        previous.ReachyToPhonePixels !=
                            measurement.ReachyToPhonePixels)
                    {
                        return Rejected(
                            ReachyCameraCoveragePublishStatus
                                .IdentityMismatch,
                            "An identical camera/simulation identity " +
                            "produced different coverage.");
                    }
                    return new ReachyCameraCoveragePublishResult(
                        ReachyCameraCoveragePublishStatus.Duplicate,
                        current,
                        "The identical coverage identity was already published.");
                }
            }

            ReachyCameraCoverageState previousState =
                sameEpoch
                    ? current.State
                    : ReachyCameraCoverageState.Unavailable;
            ReachyCameraCoverageState state = policy.Classify(
                measurement.CoverageFraction,
                previousState);
            current = ReachyCameraCoverageSnapshot.Available(
                state,
                measurement,
                policy);
            return new ReachyCameraCoveragePublishResult(
                ReachyCameraCoveragePublishStatus.Accepted,
                current,
                "Published current-frame coverage and consumer policy.");
        }

        public ReachyCameraCoverageSnapshot Reset(string message)
        {
            current = ReachyCameraCoverageSnapshot.Unavailable(message);
            return current;
        }

        private ReachyCameraCoveragePublishResult? ValidateSameEpoch(
            ReachyCameraCoverageMeasurement previous,
            ReachyCameraCoverageMeasurement next)
        {
            if (!string.Equals(
                    previous.CalibrationProfileId,
                    next.CalibrationProfileId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previous.CameraId,
                    next.CameraId,
                    StringComparison.Ordinal) ||
                previous.Facing != next.Facing ||
                !string.Equals(
                    previous.ModelCompatibility,
                    next.ModelCompatibility,
                    StringComparison.Ordinal) ||
                previous.ModelHash != next.ModelHash ||
                previous.OutputWidth != next.OutputWidth ||
                previous.OutputHeight != next.OutputHeight ||
                previous.CameraBodyId != next.CameraBodyId)
            {
                return Rejected(
                    ReachyCameraCoveragePublishStatus.IdentityMismatch,
                    "Coverage identity changed without a new camera " +
                    "session or simulation continuity.");
            }

            if (next.SourceSequence < previous.SourceSequence ||
                next.AuthoritativeSequence <
                    previous.AuthoritativeSequence ||
                next.SimulationTimeSeconds <
                    previous.SimulationTimeSeconds)
            {
                return Rejected(
                    ReachyCameraCoveragePublishStatus.StaleFrame,
                    "Coverage publication regressed camera or " +
                    "authoritative simulation ordering.");
            }

            if (next.SourceSequence == previous.SourceSequence)
            {
                if (next.SourceTimestampNanoseconds !=
                        previous.SourceTimestampNanoseconds ||
                    next.PhoneOrientationTimestampNanoseconds !=
                        previous.PhoneOrientationTimestampNanoseconds)
                {
                    return Rejected(
                        ReachyCameraCoveragePublishStatus
                            .IdentityMismatch,
                        "One camera sequence was associated with " +
                        "different timestamps.");
                }
            }
            else if (next.SourceTimestampNanoseconds <=
                previous.SourceTimestampNanoseconds)
            {
                return Rejected(
                    ReachyCameraCoveragePublishStatus.StaleFrame,
                    "A newer camera sequence did not advance its timestamp.");
            }

            return null;
        }

        private ReachyCameraCoveragePublishResult Rejected(
            ReachyCameraCoveragePublishStatus status,
            string message)
        {
            return new ReachyCameraCoveragePublishResult(
                status,
                current,
                message);
        }
    }
}

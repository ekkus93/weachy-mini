#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Behavior
{
    public static class ReachyBehaviorPlannerActuators
    {
        public const int Count = 9;
        public const int BodyYaw = 0;
        public const int Stewart1 = 1;
        public const int Stewart2 = 2;
        public const int Stewart3 = 3;
        public const int Stewart4 = 4;
        public const int Stewart5 = 5;
        public const int Stewart6 = 6;
        public const int RightAntenna = 7;
        public const int LeftAntenna = 8;
    }

    public enum ReachyBehaviorPlannerStatus
    {
        Planned = 0,
        Cancelled = 1,
        WorldSnapshotStale = 2,
        GazeTargetNotFound = 3,
        GazeTargetNotVisible = 4,
        GazeTargetLowConfidence = 5,
        GazeCoverageBlocked = 6,
        SafetyInterlockActive = 7,
        MotionStateInvalid = 8,
        TrajectoryConstraintRejected = 9,
    }

    public enum ReachyBehaviorTrajectoryPurpose
    {
        Intent = 0,
        SafeRest = 1,
    }

    public sealed class ReachyBehaviorActuatorLimit
    {
        public ReachyBehaviorActuatorLimit(
            double minimumPositionRadians,
            double maximumPositionRadians,
            double maximumVelocityRadiansPerSecond,
            double maximumAccelerationRadiansPerSecondSquared)
        {
            RequireFinite(minimumPositionRadians, nameof(minimumPositionRadians));
            RequireFinite(maximumPositionRadians, nameof(maximumPositionRadians));
            RequirePositive(
                maximumVelocityRadiansPerSecond,
                nameof(maximumVelocityRadiansPerSecond));
            RequirePositive(
                maximumAccelerationRadiansPerSecondSquared,
                nameof(maximumAccelerationRadiansPerSecondSquared));
            if (minimumPositionRadians >= maximumPositionRadians)
            {
                throw new ArgumentException(
                    "Actuator minimum position must be below maximum position.");
            }

            MinimumPositionRadians = minimumPositionRadians;
            MaximumPositionRadians = maximumPositionRadians;
            MaximumVelocityRadiansPerSecond = maximumVelocityRadiansPerSecond;
            MaximumAccelerationRadiansPerSecondSquared =
                maximumAccelerationRadiansPerSecondSquared;
        }

        public double MinimumPositionRadians { get; }

        public double MaximumPositionRadians { get; }

        public double MaximumVelocityRadiansPerSecond { get; }

        public double MaximumAccelerationRadiansPerSecondSquared { get; }

        public bool Contains(double positionRadians)
        {
            return IsFinite(positionRadians) &&
                positionRadians >= MinimumPositionRadians &&
                positionRadians <= MaximumPositionRadians;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void RequireFinite(double value, string name)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequirePositive(double value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public sealed class ReachyBehaviorPlannerPolicy
    {
        private readonly ReadOnlyCollection<ReachyBehaviorActuatorLimit>
            actuatorLimits;

        public ReachyBehaviorPlannerPolicy(
            double minimumGazeConfidence,
            double minimumValidCoverageFraction,
            long maximumWorldSnapshotAgeNanoseconds,
            int minimumSegmentMilliseconds,
            int commandIntervalMilliseconds,
            int maximumTrajectoryFrameCount,
            int maximumPlanDurationMilliseconds,
            double maximumGazeBodyYawRadians,
            double maximumGazeHeadYawRadians,
            double maximumGazeHeadPitchRadians,
            IReadOnlyList<ReachyBehaviorActuatorLimit> actuatorLimits)
        {
            RequireUnit(minimumGazeConfidence, nameof(minimumGazeConfidence));
            RequireUnit(
                minimumValidCoverageFraction,
                nameof(minimumValidCoverageFraction));
            if (maximumWorldSnapshotAgeNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumWorldSnapshotAgeNanoseconds));
            }
            if (minimumSegmentMilliseconds <= 0 ||
                minimumSegmentMilliseconds > 10_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumSegmentMilliseconds));
            }
            if (commandIntervalMilliseconds <= 0 ||
                commandIntervalMilliseconds > minimumSegmentMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commandIntervalMilliseconds));
            }
            if (maximumTrajectoryFrameCount <= 0 ||
                maximumTrajectoryFrameCount > 512)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTrajectoryFrameCount));
            }
            int representablePlanDurationMilliseconds = checked(
                maximumTrajectoryFrameCount * commandIntervalMilliseconds);
            if (maximumPlanDurationMilliseconds < minimumSegmentMilliseconds ||
                maximumPlanDurationMilliseconds > representablePlanDurationMilliseconds ||
                maximumPlanDurationMilliseconds >
                    ReachyBehaviorIntentPolicy.MaximumDurationMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPlanDurationMilliseconds));
            }
            RequirePositive(
                maximumGazeBodyYawRadians,
                nameof(maximumGazeBodyYawRadians));
            RequirePositive(
                maximumGazeHeadYawRadians,
                nameof(maximumGazeHeadYawRadians));
            RequirePositive(
                maximumGazeHeadPitchRadians,
                nameof(maximumGazeHeadPitchRadians));
            if (actuatorLimits == null ||
                actuatorLimits.Count != ReachyBehaviorPlannerActuators.Count)
            {
                throw new ArgumentException(
                    "Behavior planner policy must define exactly nine actuator limits.",
                    nameof(actuatorLimits));
            }

            var copy = new List<ReachyBehaviorActuatorLimit>(actuatorLimits.Count);
            for (int index = 0; index < actuatorLimits.Count; ++index)
            {
                copy.Add(
                    actuatorLimits[index] ??
                    throw new ArgumentException(
                        "Actuator limits cannot contain null entries.",
                        nameof(actuatorLimits)));
            }

            MinimumGazeConfidence = minimumGazeConfidence;
            MinimumValidCoverageFraction = minimumValidCoverageFraction;
            MaximumWorldSnapshotAgeNanoseconds = maximumWorldSnapshotAgeNanoseconds;
            MinimumSegmentMilliseconds = minimumSegmentMilliseconds;
            CommandIntervalMilliseconds = commandIntervalMilliseconds;
            MaximumTrajectoryFrameCount = maximumTrajectoryFrameCount;
            MaximumPlanDurationMilliseconds = maximumPlanDurationMilliseconds;
            MaximumGazeBodyYawRadians = maximumGazeBodyYawRadians;
            MaximumGazeHeadYawRadians = maximumGazeHeadYawRadians;
            MaximumGazeHeadPitchRadians = maximumGazeHeadPitchRadians;
            this.actuatorLimits = copy.AsReadOnly();
        }

        public double MinimumGazeConfidence { get; }

        public double MinimumValidCoverageFraction { get; }

        public long MaximumWorldSnapshotAgeNanoseconds { get; }

        public int MinimumSegmentMilliseconds { get; }

        public int CommandIntervalMilliseconds { get; }

        public int MaximumTrajectoryFrameCount { get; }

        public int MaximumPlanDurationMilliseconds { get; }

        public double MaximumGazeBodyYawRadians { get; }

        public double MaximumGazeHeadYawRadians { get; }

        public double MaximumGazeHeadPitchRadians { get; }

        public IReadOnlyList<ReachyBehaviorActuatorLimit> ActuatorLimits =>
            actuatorLimits;

        public static ReachyBehaviorPlannerPolicy CreateMobileDefault()
        {
            const double inset = 0.015;
            return new ReachyBehaviorPlannerPolicy(
                minimumGazeConfidence: 0.65,
                minimumValidCoverageFraction: 0.50,
                maximumWorldSnapshotAgeNanoseconds: 1_000_000_000L,
                minimumSegmentMilliseconds: 120,
                commandIntervalMilliseconds: 50,
                maximumTrajectoryFrameCount: 128,
                maximumPlanDurationMilliseconds: 6_400,
                maximumGazeBodyYawRadians: 0.35,
                maximumGazeHeadYawRadians: 0.18,
                maximumGazeHeadPitchRadians: 0.16,
                new ReachyBehaviorActuatorLimit[]
                {
                    new ReachyBehaviorActuatorLimit(
                        -2.792526803190975 + inset,
                        2.792526803190879 - inset,
                        0.80,
                        1.60),
                    new ReachyBehaviorActuatorLimit(
                        -0.8377580409572196 + inset,
                        1.3962634015955222 - inset,
                        0.90,
                        1.80),
                    new ReachyBehaviorActuatorLimit(
                        -1.396263401595614 + inset,
                        1.2217304763958803 - inset,
                        0.90,
                        1.80),
                    new ReachyBehaviorActuatorLimit(
                        -0.8377580409572173 + inset,
                        1.3962634015955244 - inset,
                        0.90,
                        1.80),
                    new ReachyBehaviorActuatorLimit(
                        -1.3962634015953894 + inset,
                        0.8377580409573525 - inset,
                        0.90,
                        1.80),
                    new ReachyBehaviorActuatorLimit(
                        -1.2217304763962082 + inset,
                        1.396263401595286 - inset,
                        0.90,
                        1.80),
                    new ReachyBehaviorActuatorLimit(
                        -1.3962634015954123 + inset,
                        0.8377580409573296 - inset,
                        0.90,
                        1.80),
                    new ReachyBehaviorActuatorLimit(-3.05, 3.05, 1.20, 2.40),
                    new ReachyBehaviorActuatorLimit(-3.05, 3.05, 1.20, 2.40),
                });
        }

        private static void RequireUnit(double value, string name)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0.0 ||
                value > 1.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequirePositive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}

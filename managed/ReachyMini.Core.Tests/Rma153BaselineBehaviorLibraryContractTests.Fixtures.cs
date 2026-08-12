#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma153BaselineBehaviorLibraryContractTests
    {
        private static ReachyBaselineBehaviorLibrary CreateLibrary()
        {
            var planner = new ReachyDeterministicBehaviorPlanner(
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
            return ReachyBaselineBehaviorLibrary.CreateMobileDefault(planner);
        }

        private static ReachyBaselineBehaviorPlanResult Plan(
            ReachyBaselineBehaviorLibrary library,
            ReachyBaselineBehaviorRequest request,
            WorldModelSnapshot? worldSnapshot = null,
            ReachyBehaviorMotionSnapshot? motionSnapshot = null)
        {
            return library.Plan(
                request,
                worldSnapshot,
                motionSnapshot ?? NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp);
        }

        private static ReachyBehaviorMotionSnapshot NeutralMotion()
        {
            return new ReachyBehaviorMotionSnapshot(
                new double[ReachyBehaviorPlannerActuators.Count],
                new double[ReachyBehaviorPlannerActuators.Count]);
        }

        private static ReachyBehaviorMotionSnapshot MotionAt(double bodyYaw)
        {
            var positions = new double[ReachyBehaviorPlannerActuators.Count];
            positions[ReachyBehaviorPlannerActuators.BodyYaw] = bodyYaw;
            return new ReachyBehaviorMotionSnapshot(
                positions,
                new double[ReachyBehaviorPlannerActuators.Count]);
        }

        private static ReachyBehaviorSafetySnapshot SafeMotion()
        {
            return new ReachyBehaviorSafetySnapshot(
                motionPathAvailable: true,
                workspaceClear: true,
                activeFault: false,
                activeCollision: false,
                activeHardStop: false,
                loadLimitActive: false);
        }

        private static WorldCoverageContext Coverage(
            VisionCoverageState state,
            double validFraction,
            bool stopTurning)
        {
            return new WorldCoverageContext(
                state,
                validFraction,
                stopTurning,
                "rma153-current-coverage");
        }

        private static WorldModelSnapshot CreateWorldSnapshot(
            string entityId,
            bool visible,
            double confidence,
            VisionCoverageState coverageState,
            double validFraction,
            bool stopTurning,
            double centerX,
            double centerY)
        {
            var bounds = new NormalizedVisionBounds(
                Math.Max(0.0, centerX - 0.05),
                Math.Max(0.0, centerY - 0.05),
                0.10,
                0.10);
            var frame = new ReachyVisionFrameIdentity(
                "reachy-eye",
                sourceSessionId: 2UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: SnapshotTimestamp,
                authoritativeSequence: 1UL,
                continuityId: 1U);
            var coverage = new WorldCoverageContext(
                coverageState,
                validFraction,
                stopTurning,
                "rma153-test-coverage");
            var entity = new WorldEntitySnapshot(
                entityId,
                "track-153",
                "face",
                "tracker-153",
                frame,
                visible
                    ? WorldEntityVisibility.CurrentlyVisible
                    : WorldEntityVisibility.RecentlySeen,
                firstSeenTimestampNanoseconds: SnapshotTimestamp - 200_000_000L,
                lastSeenTimestampNanoseconds: SnapshotTimestamp,
                confidence,
                bounds,
                WorldPositionEstimate.UnknownFromTwoDimensionalTracking(),
                WorldDirectionEstimate.FromBounds(bounds),
                coverage,
                Array.Empty<WorldObservationSnapshot>(),
                Array.Empty<WorldDescriptionSnapshot>(),
                SnapshotTimestamp,
                droppedObservationCount: 0L,
                droppedDescriptionCount: 0L);
            var diagnostics = new WorldModelDiagnosticsSnapshot(
                acceptedTrackingBatchCount: 0L,
                duplicateTrackingBatchCount: 0L,
                staleTrackingBatchCount: 0L,
                invalidCoverageBatchCount: 0L,
                capacityRejectedBatchCount: 0L,
                classificationConflictCount: 0L,
                acceptedDescriptionCount: 0L,
                duplicateDescriptionCount: 0L,
                rejectedDescriptionCount: 0L,
                expiredEntityCount: 0L,
                activeScopeCursorCount: 0,
                droppedScopeCursorCount: 0L);
            return new WorldModelSnapshot(
                SnapshotTimestamp,
                new[] { entity },
                diagnostics);
        }

        private static ReachyBehaviorTrajectoryPlan RequirePlan(
            ReachyBaselineBehaviorPlanResult result,
            string description)
        {
            return result.PlannerResult.Plan ??
                throw new InvalidOperationException(
                    "RMA-153 missing plan for " + description + ".");
        }

        private static void RequireSucceeded(
            ReachyBaselineBehaviorPlanResult result,
            string description)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "RMA-153 planning failed for " + description + ": " +
                    result.PlannerResult.Status + "/" +
                    result.PlannerResult.DiagnosticCode + ".");
            }
        }

        private static void AssertReturnsToSource(
            ReachyBehaviorTrajectoryPlan plan,
            IReadOnlyList<double> source,
            string description)
        {
            if (plan.Frames.Count == 0)
            {
                return;
            }
            ReachyBehaviorTrajectoryFrame final =
                plan.Frames[plan.Frames.Count - 1];
            for (int index = 0; index < source.Count; ++index)
            {
                Equal(source[index], final.TargetPositionsRadians[index], description);
            }
        }

        private static double PeakAntennaMagnitude(
            ReachyBehaviorTrajectoryPlan plan)
        {
            double peak = 0.0;
            for (int index = 0; index < plan.Frames.Count; ++index)
            {
                ReachyBehaviorTrajectoryFrame frame = plan.Frames[index];
                peak = Math.Max(
                    peak,
                    Math.Abs(
                        frame.TargetPositionsRadians[
                            ReachyBehaviorPlannerActuators.RightAntenna]));
            }
            return peak;
        }

        private static void AssertPlansEqual(
            ReachyBehaviorTrajectoryPlan first,
            ReachyBehaviorTrajectoryPlan second,
            string description)
        {
            Equal(first.Purpose, second.Purpose, description + " purpose");
            Equal(first.Frames.Count, second.Frames.Count, description + " frame count");
            for (int frame = 0; frame < first.Frames.Count; ++frame)
            {
                Equal(
                    first.Frames[frame].OffsetMilliseconds,
                    second.Frames[frame].OffsetMilliseconds,
                    description + " offset");
                for (int actuator = 0;
                    actuator < ReachyBehaviorPlannerActuators.Count;
                    ++actuator)
                {
                    Equal(
                        first.Frames[frame].TargetPositionsRadians[actuator],
                        second.Frames[frame].TargetPositionsRadians[actuator],
                        description + " actuator");
                }
            }
        }

        private static void AssertScheduledPlanWithinPolicy(
            ReachyBehaviorTrajectoryPlan plan,
            IReadOnlyList<double> initialPositions,
            ReachyBehaviorPlannerPolicy policy)
        {
            IReadOnlyList<double> previous = initialPositions;
            var previousVelocities =
                new double[ReachyBehaviorPlannerActuators.Count];
            int previousOffset = plan.SpeechStartOffsetMilliseconds;
            for (int frameIndex = 0;
                frameIndex < plan.Frames.Count;
                ++frameIndex)
            {
                ReachyBehaviorTrajectoryFrame frame = plan.Frames[frameIndex];
                int durationMilliseconds =
                    frame.OffsetMilliseconds - previousOffset;
                Equal(
                    policy.CommandIntervalMilliseconds,
                    durationMilliseconds,
                    "fixed baseline command cadence");
                double seconds = durationMilliseconds / 1000.0;
                for (int actuator = 0;
                    actuator < ReachyBehaviorPlannerActuators.Count;
                    ++actuator)
                {
                    ReachyBehaviorActuatorLimit limit =
                        policy.ActuatorLimits[actuator];
                    double target = frame.TargetPositionsRadians[actuator];
                    Equal(true, limit.Contains(target), "baseline soft envelope");
                    double velocity =
                        (target - previous[actuator]) / seconds;
                    double acceleration =
                        (velocity - previousVelocities[actuator]) / seconds;
                    Equal(
                        true,
                        Math.Abs(velocity) <=
                            limit.MaximumVelocityRadiansPerSecond + 1e-12,
                        "baseline velocity envelope");
                    Equal(
                        true,
                        Math.Abs(acceleration) <=
                            limit.MaximumAccelerationRadiansPerSecondSquared + 1e-12,
                        "baseline acceleration envelope");
                    previousVelocities[actuator] = velocity;
                }
                previous = frame.TargetPositionsRadians;
                previousOffset = frame.OffsetMilliseconds;
            }
        }

        private static void ExpectArgumentOutOfRange(
            Action action,
            string description)
        {
            try
            {
                action();
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
            throw new InvalidOperationException(
                "RMA-153 expected ArgumentOutOfRangeException for " + description + ".");
        }

        private static void ExpectArgument(Action action, string description)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new InvalidOperationException(
                "RMA-153 expected ArgumentException for " + description + ".");
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-153 contract failed for {description}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma152DeterministicBehaviorPlannerContractTests
    {
        private static ReachyDeterministicBehaviorPlanner CreatePlanner()
        {
            return new ReachyDeterministicBehaviorPlanner(
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
        }

        private static ReachyBehaviorMotionSnapshot NeutralMotion()
        {
            return new ReachyBehaviorMotionSnapshot(
                new double[ReachyBehaviorPlannerActuators.Count],
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

        private static WorldModelSnapshot CreateWorldSnapshot(
            string entityId,
            bool visible,
            double confidence,
            VisionCoverageState coverageState,
            double validFraction,
            bool stopTurning,
            double centerX,
            double centerY,
            long snapshotTimestamp = SnapshotTimestamp)
        {
            var bounds = new NormalizedVisionBounds(
                Math.Max(0.0, centerX - 0.05),
                Math.Max(0.0, centerY - 0.05),
                0.10,
                0.10);
            var frame = new ReachyVisionFrameIdentity(
                "reachy-eye",
                sourceSessionId: 1UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: snapshotTimestamp,
                authoritativeSequence: 1UL,
                continuityId: 1U);
            var coverage = new WorldCoverageContext(
                coverageState,
                validFraction,
                stopTurning,
                "rma152-test-coverage");
            var entity = new WorldEntitySnapshot(
                entityId,
                "track-1",
                "face",
                "tracker-1",
                frame,
                visible
                    ? WorldEntityVisibility.CurrentlyVisible
                    : WorldEntityVisibility.RecentlySeen,
                firstSeenTimestampNanoseconds: snapshotTimestamp - 100_000_000L,
                lastSeenTimestampNanoseconds: snapshotTimestamp,
                confidence,
                bounds,
                WorldPositionEstimate.UnknownFromTwoDimensionalTracking(),
                WorldDirectionEstimate.FromBounds(bounds),
                coverage,
                Array.Empty<WorldObservationSnapshot>(),
                Array.Empty<WorldDescriptionSnapshot>(),
                snapshotTimestamp,
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
                snapshotTimestamp,
                new[] { entity },
                diagnostics);
        }

        private static void AssertGazeFailure(
            ReachyDeterministicBehaviorPlanner planner,
            ReachyBehaviorIntent intent,
            WorldModelSnapshot world,
            ReachyBehaviorPlannerStatus expected,
            string description)
        {
            ReachyBehaviorPlanResult result = planner.Plan(
                intent,
                world,
                NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp);
            Equal(expected, result.Status, description);
            Equal(null, result.Plan, description + " plan");
        }

        private static void AssertPlanWithinPolicy(
            ReachyBehaviorTrajectoryPlan plan,
            IReadOnlyList<double> initialPositions,
            ReachyBehaviorPlannerPolicy policy)
        {
            AssertScheduledPlanWithinPolicy(plan, initialPositions, policy);
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-152 contract failed for {description}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }
    }
}

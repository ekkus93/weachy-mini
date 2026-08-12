#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        private ReachyBehaviorPlanResult? ResolveLostTargetSearch(
            string entityId,
            long currentCoverageTimestampNanoseconds,
            WorldCoverageContext currentCoverage,
            ReachyBaselineBehaviorPolicy baselinePolicy,
            WorldModelSnapshot? worldSnapshot,
            long planningTimestampNanoseconds,
            double[] baseTarget,
            out string? resolvedGazeEntityId,
            out List<double[]> poses)
        {
            resolvedGazeEntityId = null;
            poses = new List<double[]>();
            if (worldSnapshot == null)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetNotFound,
                    "gaze-search-world-model-snapshot-unavailable");
            }
            if (currentCoverageTimestampNanoseconds <= 0L ||
                currentCoverageTimestampNanoseconds > planningTimestampNanoseconds ||
                planningTimestampNanoseconds - currentCoverageTimestampNanoseconds >
                    policy.MaximumWorldSnapshotAgeNanoseconds)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeCoverageBlocked,
                    "gaze-search-current-coverage-outside-age-bound");
            }
            if (worldSnapshot.TimestampNanoseconds <= 0L ||
                worldSnapshot.TimestampNanoseconds > planningTimestampNanoseconds ||
                planningTimestampNanoseconds - worldSnapshot.TimestampNanoseconds >
                    policy.MaximumWorldSnapshotAgeNanoseconds)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.WorldSnapshotStale,
                    "gaze-search-world-model-snapshot-outside-age-bound");
            }

            WorldEntitySnapshot? entity = FindEntity(worldSnapshot, entityId);
            if (entity == null)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetNotFound,
                    "gaze-search-target-not-found-in-current-world-model");
            }
            if (entity.IsCurrentlyVisible)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                    "gaze-search-target-still-visible-use-acquisition");
            }
            if (entity.LastSeenTimestampNanoseconds <= 0L ||
                entity.LastSeenTimestampNanoseconds > planningTimestampNanoseconds ||
                planningTimestampNanoseconds - entity.LastSeenTimestampNanoseconds >
                    baselinePolicy.MaximumSearchTargetAgeNanoseconds)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetNotVisible,
                    "gaze-search-target-last-seen-too-old");
            }
            if (entity.Confidence < baselinePolicy.MinimumSearchConfidence)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetLowConfidence,
                    "gaze-search-target-confidence-below-library-threshold");
            }
            if (currentCoverage.ShouldStopVisionDrivenTurning ||
                currentCoverage.ValidFraction < policy.MinimumValidCoverageFraction ||
                (currentCoverage.State != VisionCoverageState.Normal &&
                    currentCoverage.State != VisionCoverageState.Degraded))
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeCoverageBlocked,
                    "gaze-search-image-coverage-does-not-permit-turning");
            }

            double[] center = CopyTarget(baseTarget);
            ApplySearchCenter(entity.Direction, center, baselinePolicy);

            double[] left = CopyTarget(center);
            left[ReachyBehaviorPlannerActuators.BodyYaw] -=
                baselinePolicy.SearchBodyYawAmplitudeRadians;
            ApplyHeadOffset(
                left,
                -baselinePolicy.SearchHeadYawAmplitudeRadians,
                pitchRadians: 0.0,
                rollRadians: 0.0);

            double[] right = CopyTarget(center);
            right[ReachyBehaviorPlannerActuators.BodyYaw] +=
                baselinePolicy.SearchBodyYawAmplitudeRadians;
            ApplyHeadOffset(
                right,
                baselinePolicy.SearchHeadYawAmplitudeRadians,
                pitchRadians: 0.0,
                rollRadians: 0.0);

            poses.Add(center);
            poses.Add(left);
            poses.Add(right);
            poses.Add(CopyTarget(center));
            poses.Add(CopyTarget(baseTarget));
            resolvedGazeEntityId = entity.EntityId;
            return null;
        }

        private static WorldEntitySnapshot? FindEntity(
            WorldModelSnapshot worldSnapshot,
            string entityId)
        {
            for (int index = 0; index < worldSnapshot.Entities.Count; ++index)
            {
                WorldEntitySnapshot candidate = worldSnapshot.Entities[index];
                if (string.Equals(
                        candidate.EntityId,
                        entityId,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void ApplySearchCenter(
            WorldDirectionEstimate direction,
            double[] target,
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            // RMA-101 fixes Reachy optical +X as image-right. In the
            // pinned MuJoCo model, positive yaw_body rotates the robot's
            // forward +X axis toward world +Y, while optical image-right is
            // world -Y at neutral. Negate the image-space horizontal angle
            // so a target to the right commands physical yaw toward it.
            double horizontal = -Math.Atan2(direction.X, direction.Z);
            double horizontalNorm = Math.Sqrt(
                (direction.X * direction.X) +
                (direction.Z * direction.Z));
            double vertical = Math.Atan2(direction.Y, horizontalNorm);
            double bodyYaw = Clamp(
                horizontal * 0.35,
                -baselinePolicy.SearchCenterBodyYawRadians,
                baselinePolicy.SearchCenterBodyYawRadians);
            double headYaw = Clamp(
                horizontal - bodyYaw,
                -baselinePolicy.SearchCenterHeadYawRadians,
                baselinePolicy.SearchCenterHeadYawRadians);
            double headPitch = Clamp(
                vertical,
                -baselinePolicy.SearchCenterHeadPitchRadians,
                baselinePolicy.SearchCenterHeadPitchRadians);

            target[ReachyBehaviorPlannerActuators.BodyYaw] += bodyYaw;
            ApplyHeadOffset(
                target,
                headYaw,
                headPitch,
                rollRadians: 0.0);
        }
    }
}

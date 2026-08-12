#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        private ReachyBehaviorPlanResult? ResolveAndApplyGaze(
            ReachyBehaviorGazeTarget gazeTarget,
            WorldModelSnapshot? worldSnapshot,
            long planningTimestampNanoseconds,
            double[] target,
            out string? resolvedGazeEntityId)
        {
            resolvedGazeEntityId = null;
            if (worldSnapshot == null)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetNotFound,
                    "world-model-snapshot-unavailable");
            }
            if (worldSnapshot.TimestampNanoseconds <= 0L ||
                worldSnapshot.TimestampNanoseconds > planningTimestampNanoseconds ||
                planningTimestampNanoseconds - worldSnapshot.TimestampNanoseconds >
                    policy.MaximumWorldSnapshotAgeNanoseconds)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.WorldSnapshotStale,
                    "world-model-snapshot-outside-planning-age-bound");
            }

            WorldEntitySnapshot? entity = null;
            for (int index = 0; index < worldSnapshot.Entities.Count; ++index)
            {
                WorldEntitySnapshot candidate = worldSnapshot.Entities[index];
                if (string.Equals(
                        candidate.EntityId,
                        gazeTarget.EntityId,
                        StringComparison.Ordinal))
                {
                    entity = candidate;
                    break;
                }
            }
            if (entity == null)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetNotFound,
                    "gaze-target-not-found-in-current-world-model");
            }
            if (!entity.IsCurrentlyVisible)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetNotVisible,
                    "gaze-target-expired-or-not-currently-visible");
            }
            if (entity.Confidence < policy.MinimumGazeConfidence)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeTargetLowConfidence,
                    "gaze-target-confidence-below-planner-threshold");
            }
            if (entity.Coverage.ShouldStopVisionDrivenTurning ||
                entity.Coverage.ValidFraction < policy.MinimumValidCoverageFraction ||
                (entity.Coverage.State != VisionCoverageState.Normal &&
                    entity.Coverage.State != VisionCoverageState.Degraded))
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.GazeCoverageBlocked,
                    "gaze-target-image-coverage-does-not-permit-turning");
            }

            WorldDirectionEstimate direction = entity.Direction;
            double horizontal = Math.Atan2(direction.X, direction.Z);
            double horizontalNorm = Math.Sqrt(
                (direction.X * direction.X) +
                (direction.Z * direction.Z));
            double vertical = Math.Atan2(direction.Y, horizontalNorm);

            double bodyYaw = Clamp(
                horizontal * 0.45,
                -policy.MaximumGazeBodyYawRadians,
                policy.MaximumGazeBodyYawRadians);
            double headYaw = Clamp(
                horizontal - bodyYaw,
                -policy.MaximumGazeHeadYawRadians,
                policy.MaximumGazeHeadYawRadians);
            double headPitch = Clamp(
                vertical,
                -policy.MaximumGazeHeadPitchRadians,
                policy.MaximumGazeHeadPitchRadians);

            target[ReachyBehaviorPlannerActuators.BodyYaw] += bodyYaw;
            ApplyHeadOffset(
                target,
                yawRadians: headYaw,
                pitchRadians: headPitch,
                rollRadians: 0.0);
            resolvedGazeEntityId = entity.EntityId;
            return null;
        }

        private static void ApplyExpression(
            ReachyBehaviorExpression? expression,
            double[] target)
        {
            switch (expression)
            {
                case null:
                case ReachyBehaviorExpression.Neutral:
                    return;
                case ReachyBehaviorExpression.Attentive:
                    ApplyHeadOffset(target, 0.0, 0.03, 0.0);
                    AddAntennaOffset(target, 0.10, -0.10);
                    return;
                case ReachyBehaviorExpression.Curious:
                    ApplyHeadOffset(target, 0.0, 0.0, 0.05);
                    AddAntennaOffset(target, 0.15, -0.15);
                    return;
                case ReachyBehaviorExpression.Pleased:
                    ApplyHeadOffset(target, 0.0, -0.02, 0.0);
                    AddAntennaOffset(target, 0.08, -0.08);
                    return;
                case ReachyBehaviorExpression.Concerned:
                    ApplyHeadOffset(target, 0.0, -0.03, 0.0);
                    AddAntennaOffset(target, -0.08, 0.08);
                    return;
                case ReachyBehaviorExpression.Surprised:
                    ApplyHeadOffset(target, 0.0, 0.06, 0.0);
                    AddAntennaOffset(target, 0.22, -0.22);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(expression));
            }
        }

        private static List<double[]> CreateGesturePoses(
            ReachyBehaviorGesture? gesture,
            double[] baseTarget)
        {
            var poses = new List<double[]>();
            ReachyBehaviorGesture selected = gesture ?? ReachyBehaviorGesture.None;
            switch (selected)
            {
                case ReachyBehaviorGesture.None:
                    poses.Add(CopyTarget(baseTarget));
                    break;
                case ReachyBehaviorGesture.Nod:
                {
                    double[] down = CopyTarget(baseTarget);
                    ApplyHeadOffset(down, 0.0, -0.10, 0.0);
                    poses.Add(down);
                    double[] up = CopyTarget(baseTarget);
                    ApplyHeadOffset(up, 0.0, 0.06, 0.0);
                    poses.Add(up);
                    poses.Add(CopyTarget(baseTarget));
                    break;
                }
                case ReachyBehaviorGesture.SmallHeadTilt:
                {
                    double[] tilted = CopyTarget(baseTarget);
                    ApplyHeadOffset(tilted, 0.0, 0.0, 0.10);
                    poses.Add(tilted);
                    poses.Add(CopyTarget(baseTarget));
                    break;
                }
                case ReachyBehaviorGesture.Recoil:
                {
                    double[] recoil = CopyTarget(baseTarget);
                    ApplyHeadOffset(recoil, 0.0, 0.09, 0.0);
                    recoil[ReachyBehaviorPlannerActuators.BodyYaw] *= 0.75;
                    AddAntennaOffset(recoil, 0.12, -0.12);
                    poses.Add(recoil);
                    poses.Add(CopyTarget(baseTarget));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(gesture));
            }
            return poses;
        }

        private static void ApplyHeadOffset(
            double[] target,
            double yawRadians,
            double pitchRadians,
            double rollRadians)
        {
            target[ReachyBehaviorPlannerActuators.Stewart1] +=
                (0.50 * yawRadians) + (0.70 * pitchRadians);
            target[ReachyBehaviorPlannerActuators.Stewart2] +=
                (-0.50 * yawRadians) +
                (0.70 * pitchRadians) +
                (0.70 * rollRadians);
            target[ReachyBehaviorPlannerActuators.Stewart3] +=
                (0.50 * yawRadians) + (0.70 * rollRadians);
            target[ReachyBehaviorPlannerActuators.Stewart4] +=
                (-0.50 * yawRadians) - (0.70 * pitchRadians);
            target[ReachyBehaviorPlannerActuators.Stewart5] +=
                (0.50 * yawRadians) -
                (0.70 * pitchRadians) -
                (0.70 * rollRadians);
            target[ReachyBehaviorPlannerActuators.Stewart6] +=
                (-0.50 * yawRadians) - (0.70 * rollRadians);
        }

        private static void AddAntennaOffset(
            double[] target,
            double rightRadians,
            double leftRadians)
        {
            target[ReachyBehaviorPlannerActuators.RightAntenna] += rightRadians;
            target[ReachyBehaviorPlannerActuators.LeftAntenna] += leftRadians;
        }

        private static double[] CopyTarget(IReadOnlyList<double> source)
        {
            var copy = new double[ReachyBehaviorPlannerActuators.Count];
            for (int index = 0; index < copy.Length; ++index)
            {
                copy[index] = source[index];
            }
            return copy;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

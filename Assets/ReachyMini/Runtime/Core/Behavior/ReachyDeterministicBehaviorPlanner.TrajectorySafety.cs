#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        private ReachyBehaviorPlanResult? BuildTrajectory(
            ReachyBehaviorTrajectoryPurpose purpose,
            string? speech,
            int startDelayMilliseconds,
            int maximumDurationMilliseconds,
            string? resolvedGazeEntityId,
            IReadOnlyList<double> initialPositions,
            IReadOnlyList<double[]> poses,
            double urgencyScale,
            out ReachyBehaviorTrajectoryPlan? plan)
        {
            plan = null;
            if (startDelayMilliseconds < 0 || maximumDurationMilliseconds <= 0)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                    "behavior-timing-constraint-invalid");
            }
            if (startDelayMilliseconds > maximumDurationMilliseconds)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                    "behavior-start-delay-exceeds-maximum-duration");
            }
            if (urgencyScale <= 0.0 || urgencyScale > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(urgencyScale));
            }

            var frames = new List<ReachyBehaviorTrajectoryFrame>(poses.Count);
            IReadOnlyList<double> previous = initialPositions;
            int offset = startDelayMilliseconds;
            for (int index = 0; index < poses.Count; ++index)
            {
                double[] target = poses[index];
                if (!ValidateTarget(target))
                {
                    return Failure(
                        ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                        "behavior-target-exceeds-soft-actuator-envelope");
                }

                int segmentMilliseconds = MinimumSafeSegmentMilliseconds(
                    previous,
                    target,
                    urgencyScale);
                offset = checked(offset + segmentMilliseconds);
                if (offset > maximumDurationMilliseconds ||
                    offset > policy.MaximumPlanDurationMilliseconds)
                {
                    return Failure(
                        ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                        "behavior-duration-cannot-meet-velocity-acceleration-limits");
                }
                frames.Add(
                    new ReachyBehaviorTrajectoryFrame(offset, target));
                previous = target;
            }

            plan = new ReachyBehaviorTrajectoryPlan(
                purpose,
                speech,
                startDelayMilliseconds,
                resolvedGazeEntityId,
                frames);
            return null;
        }

        private int MinimumSafeSegmentMilliseconds(
            IReadOnlyList<double> from,
            IReadOnlyList<double> to,
            double urgencyScale)
        {
            double minimumSeconds = policy.MinimumSegmentMilliseconds / 1000.0;
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                double distance = Math.Abs(to[index] - from[index]);
                ReachyBehaviorActuatorLimit limit = policy.ActuatorLimits[index];
                double velocity =
                    limit.MaximumVelocityRadiansPerSecond * urgencyScale;
                double acceleration =
                    limit.MaximumAccelerationRadiansPerSecondSquared * urgencyScale;
                double velocitySeconds = distance / velocity;
                double accelerationSeconds = distance <= 0.0
                    ? 0.0
                    : 2.0 * Math.Sqrt(distance / acceleration);
                minimumSeconds = Math.Max(
                    minimumSeconds,
                    Math.Max(velocitySeconds, accelerationSeconds));
            }
            return checked((int)Math.Ceiling(minimumSeconds * 1000.0));
        }

        private bool ValidateMotionSnapshot(
            ReachyBehaviorMotionSnapshot motionSnapshot)
        {
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                ReachyBehaviorActuatorLimit limit = policy.ActuatorLimits[index];
                if (!limit.Contains(motionSnapshot.PositionsRadians[index]))
                {
                    return false;
                }
                double velocity = motionSnapshot.VelocitiesRadiansPerSecond[index];
                if (Math.Abs(velocity) >
                    limit.MaximumVelocityRadiansPerSecond)
                {
                    return false;
                }
            }
            return true;
        }

        private bool ValidateTarget(IReadOnlyList<double> target)
        {
            if (target.Count != ReachyBehaviorPlannerActuators.Count)
            {
                return false;
            }
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                if (!policy.ActuatorLimits[index].Contains(target[index]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

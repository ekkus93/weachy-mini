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

            var frames = new List<ReachyBehaviorTrajectoryFrame>();
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
                if (!HasMotion(previous, target))
                {
                    previous = target;
                    continue;
                }

                int segmentMilliseconds = MinimumSafeSegmentMilliseconds(
                    previous,
                    target,
                    urgencyScale);
                int stepCount = checked(
                    (segmentMilliseconds + policy.CommandIntervalMilliseconds - 1) /
                    policy.CommandIntervalMilliseconds);
                int scheduledSegmentMilliseconds = checked(
                    stepCount * policy.CommandIntervalMilliseconds);
                int segmentEndOffset = checked(offset + scheduledSegmentMilliseconds);
                if (segmentEndOffset > maximumDurationMilliseconds ||
                    segmentEndOffset > policy.MaximumPlanDurationMilliseconds)
                {
                    return Failure(
                        ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                        "behavior-duration-cannot-meet-velocity-acceleration-limits");
                }
                if (frames.Count + stepCount > policy.MaximumTrajectoryFrameCount)
                {
                    return Failure(
                        ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                        "behavior-trajectory-frame-budget-exceeded");
                }

                AppendSmoothStepFrames(
                    frames,
                    previous,
                    target,
                    offset,
                    stepCount);
                offset = segmentEndOffset;
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

        private void AppendSmoothStepFrames(
            List<ReachyBehaviorTrajectoryFrame> frames,
            IReadOnlyList<double> from,
            IReadOnlyList<double> to,
            int startOffsetMilliseconds,
            int stepCount)
        {
            if (stepCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepCount));
            }

            for (int step = 1; step <= stepCount; ++step)
            {
                double progress = step / (double)stepCount;
                double blend = progress * progress * (3.0 - (2.0 * progress));
                var sample = new double[ReachyBehaviorPlannerActuators.Count];
                for (int actuator = 0;
                    actuator < ReachyBehaviorPlannerActuators.Count;
                    ++actuator)
                {
                    sample[actuator] = from[actuator] +
                        ((to[actuator] - from[actuator]) * blend);
                }
                int offset = checked(
                    startOffsetMilliseconds +
                    (step * policy.CommandIntervalMilliseconds));
                frames.Add(new ReachyBehaviorTrajectoryFrame(offset, sample));
            }
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
                if (distance <= 0.0)
                {
                    continue;
                }
                ReachyBehaviorActuatorLimit limit = policy.ActuatorLimits[index];
                double velocity =
                    limit.MaximumVelocityRadiansPerSecond * urgencyScale;
                double acceleration =
                    limit.MaximumAccelerationRadiansPerSecondSquared * urgencyScale;

                // Cubic smoothstep h(s)=3s^2-2s^3 has peak dh/ds=1.5 and
                // peak |d2h/ds2|=6. These bounds make the scheduled setpoint
                // path respect the configured velocity/acceleration envelope.
                double velocitySeconds = 1.5 * distance / velocity;
                double accelerationSeconds = Math.Sqrt(6.0 * distance / acceleration);
                minimumSeconds = Math.Max(
                    minimumSeconds,
                    Math.Max(velocitySeconds, accelerationSeconds));
            }
            return checked((int)Math.Ceiling(minimumSeconds * 1000.0));
        }

        private static bool HasMotion(
            IReadOnlyList<double> from,
            IReadOnlyList<double> to)
        {
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                if (from[index] != to[index])
                {
                    return true;
                }
            }
            return false;
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

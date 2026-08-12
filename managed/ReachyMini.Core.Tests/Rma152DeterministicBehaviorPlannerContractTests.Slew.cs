#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Behavior;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma152DeterministicBehaviorPlannerContractTests
    {
        private static void TrajectoryFramesSlewInsteadOfDelayedTargetStep()
        {
            ReachyBehaviorPlannerPolicy policy =
                ReachyBehaviorPlannerPolicy.CreateMobileDefault();
            var planner = new ReachyDeterministicBehaviorPlanner(policy);
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Surprised,
                gesture: ReachyBehaviorGesture.None,
                urgency: ReachyBehaviorUrgency.High,
                timing: null);

            ReachyBehaviorTrajectoryPlan plan = planner.Plan(
                intent,
                worldSnapshot: null,
                NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp).Plan ??
                throw new InvalidOperationException(
                    "Setpoint-slew fixture could not create a trajectory plan.");

            Equal(
                true,
                plan.Frames.Count > 1,
                "setpoint slew uses intermediate frames");
            Equal(
                policy.CommandIntervalMilliseconds,
                plan.Frames[0].OffsetMilliseconds,
                "first setpoint arrives at bounded command cadence");
            double first = plan.Frames[0].TargetPositionsRadians[
                ReachyBehaviorPlannerActuators.RightAntenna];
            double final = plan.Frames[plan.Frames.Count - 1]
                .TargetPositionsRadians[ReachyBehaviorPlannerActuators.RightAntenna];
            Equal(true, Math.Abs(first) < Math.Abs(final), "first frame is intermediate");
            Equal(true, Math.Abs(final) > 0.0, "final expression target is nonzero");
            AssertScheduledPlanWithinPolicy(
                plan,
                NeutralMotion().PositionsRadians,
                policy);
        }

        private static void RecoilPreservesUnrelatedBodyYaw()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            var positions = new[]
            {
                0.20,
                0.10,
                -0.10,
                0.08,
                -0.08,
                0.07,
                -0.07,
                0.15,
                -0.15,
            };
            var motion = new ReachyBehaviorMotionSnapshot(
                positions,
                new double[ReachyBehaviorPlannerActuators.Count]);
            ReachyBehaviorIntent recoilOnly = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: null,
                gesture: ReachyBehaviorGesture.Recoil,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);

            ReachyBehaviorPlanResult result = planner.Plan(
                recoilOnly,
                worldSnapshot: null,
                motion,
                SafeMotion(),
                PlanningTimestamp);
            Equal(true, result.Succeeded, "relative recoil plan");
            ReachyBehaviorTrajectoryPlan plan = result.Plan ??
                throw new InvalidOperationException("Relative recoil plan was null.");
            for (int index = 0; index < plan.Frames.Count; ++index)
            {
                Equal(
                    positions[ReachyBehaviorPlannerActuators.BodyYaw],
                    plan.Frames[index].TargetPositionsRadians[
                        ReachyBehaviorPlannerActuators.BodyYaw],
                    "recoil preserves unrelated body yaw");
            }
        }

        private static void SafeRestCoversFullSoftEnvelope()
        {
            ReachyBehaviorPlannerPolicy policy =
                ReachyBehaviorPlannerPolicy.CreateMobileDefault();
            var planner = new ReachyDeterministicBehaviorPlanner(policy);
            var positions = new double[ReachyBehaviorPlannerActuators.Count];
            for (int index = 0; index < positions.Length; ++index)
            {
                ReachyBehaviorActuatorLimit limit = policy.ActuatorLimits[index];
                positions[index] = Math.Abs(limit.MinimumPositionRadians) >=
                    Math.Abs(limit.MaximumPositionRadians)
                        ? limit.MinimumPositionRadians
                        : limit.MaximumPositionRadians;
            }
            var motion = new ReachyBehaviorMotionSnapshot(
                positions,
                new double[ReachyBehaviorPlannerActuators.Count]);

            ReachyBehaviorPlanResult result = planner.PlanSafeRest(
                motion,
                SafeMotion());
            Equal(true, result.Succeeded, "full-envelope safe-rest plan");
            ReachyBehaviorTrajectoryPlan plan = result.Plan ??
                throw new InvalidOperationException(
                    "Full-envelope safe-rest plan was null.");
            Equal(
                true,
                plan.Frames.Count <= policy.MaximumTrajectoryFrameCount,
                "full-envelope safe-rest frame budget");
            ReachyBehaviorTrajectoryFrame finalFrame =
                plan.Frames[plan.Frames.Count - 1];
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                Equal(
                    0.0,
                    finalFrame.TargetPositionsRadians[index],
                    "full-envelope safe-rest neutral target");
            }
            AssertScheduledPlanWithinPolicy(plan, positions, policy);
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
            Equal(
                true,
                plan.Frames.Count <= policy.MaximumTrajectoryFrameCount,
                "trajectory frame budget");

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
                    "fixed trajectory command cadence");
                double seconds = durationMilliseconds / 1000.0;
                for (int actuator = 0;
                    actuator < ReachyBehaviorPlannerActuators.Count;
                    ++actuator)
                {
                    ReachyBehaviorActuatorLimit limit =
                        policy.ActuatorLimits[actuator];
                    double target = frame.TargetPositionsRadians[actuator];
                    Equal(true, limit.Contains(target), "actuator soft envelope");
                    double velocity =
                        (target - previous[actuator]) / seconds;
                    double acceleration =
                        (velocity - previousVelocities[actuator]) / seconds;
                    Equal(
                        true,
                        Math.Abs(velocity) <=
                            limit.MaximumVelocityRadiansPerSecond + 1e-12,
                        "scheduled target velocity envelope");
                    Equal(
                        true,
                        Math.Abs(acceleration) <=
                            limit.MaximumAccelerationRadiansPerSecondSquared + 1e-12,
                        "scheduled target acceleration envelope");
                    previousVelocities[actuator] = velocity;
                }
                previous = frame.TargetPositionsRadians;
                previousOffset = frame.OffsetMilliseconds;
            }

            if (plan.Frames.Count > 0)
            {
                double seconds = policy.CommandIntervalMilliseconds / 1000.0;
                for (int actuator = 0;
                    actuator < ReachyBehaviorPlannerActuators.Count;
                    ++actuator)
                {
                    double stopAcceleration =
                        Math.Abs(previousVelocities[actuator]) / seconds;
                    Equal(
                        true,
                        stopAcceleration <=
                            policy.ActuatorLimits[actuator]
                                .MaximumAccelerationRadiansPerSecondSquared + 1e-12,
                        "scheduled target endpoint acceleration envelope");
                }
            }
        }
    }
}

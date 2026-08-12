#nullable enable

using System;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma152DeterministicBehaviorPlannerContractTests
    {
        private static void CurrentHighConfidenceGazeTargetIsResolved()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            WorldModelSnapshot world = CreateWorldSnapshot(
                entityId: "entity-12",
                visible: true,
                confidence: 0.95,
                coverageState: VisionCoverageState.Normal,
                validFraction: 0.95,
                stopTurning: false,
                centerX: 0.80,
                centerY: 0.50);
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: "Looking.",
                gazeTarget: new ReachyBehaviorGazeTarget("entity-12"),
                expression: ReachyBehaviorExpression.Attentive,
                gesture: ReachyBehaviorGesture.None,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);

            ReachyBehaviorPlanResult result = planner.Plan(
                intent,
                world,
                NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp);

            Equal(true, result.Succeeded, "gaze plan success");
            ReachyBehaviorTrajectoryPlan plan = result.Plan ??
                throw new InvalidOperationException("RMA-152 gaze plan was null.");
            Equal("entity-12", plan.ResolvedGazeEntityId, "resolved gaze entity");
            Equal("Looking.", plan.Speech, "speech pass-through");
            Equal(true, plan.Frames.Count > 0, "gaze plan frame count");
            Equal(
                true,
                plan.Frames[0].TargetPositionsRadians[
                    ReachyBehaviorPlannerActuators.BodyYaw] < 0.0,
                "right-side entity produces negative physical body yaw toward optical image-right");
            AssertPlanWithinPolicy(
                plan,
                NeutralMotion().PositionsRadians,
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
        }

        private static void UnsafeGazeTargetsFailClosed()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: new ReachyBehaviorGazeTarget("entity-1"),
                expression: null,
                gesture: ReachyBehaviorGesture.None,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);

            AssertGazeFailure(
                planner,
                intent,
                CreateWorldSnapshot(
                    "entity-other",
                    true,
                    0.9,
                    VisionCoverageState.Normal,
                    1.0,
                    false,
                    0.5,
                    0.5),
                ReachyBehaviorPlannerStatus.GazeTargetNotFound,
                "missing target");
            AssertGazeFailure(
                planner,
                intent,
                CreateWorldSnapshot(
                    "entity-1",
                    false,
                    0.9,
                    VisionCoverageState.Normal,
                    1.0,
                    false,
                    0.5,
                    0.5),
                ReachyBehaviorPlannerStatus.GazeTargetNotVisible,
                "expired target");
            AssertGazeFailure(
                planner,
                intent,
                CreateWorldSnapshot(
                    "entity-1",
                    true,
                    0.4,
                    VisionCoverageState.Normal,
                    1.0,
                    false,
                    0.5,
                    0.5),
                ReachyBehaviorPlannerStatus.GazeTargetLowConfidence,
                "low-confidence target");
            AssertGazeFailure(
                planner,
                intent,
                CreateWorldSnapshot(
                    "entity-1",
                    true,
                    0.9,
                    VisionCoverageState.Unusable,
                    0.2,
                    true,
                    0.5,
                    0.5),
                ReachyBehaviorPlannerStatus.GazeCoverageBlocked,
                "coverage-blocked target");

            WorldModelSnapshot stale = CreateWorldSnapshot(
                "entity-1",
                true,
                0.9,
                VisionCoverageState.Normal,
                1.0,
                false,
                0.5,
                0.5,
                snapshotTimestamp: 8_000_000_000L);
            AssertGazeFailure(
                planner,
                intent,
                stale,
                ReachyBehaviorPlannerStatus.WorldSnapshotStale,
                "stale world snapshot");
        }

        private static void GestureTrajectoryIsDeterministicAndBounded()
        {
            ReachyBehaviorPlannerPolicy policy =
                ReachyBehaviorPlannerPolicy.CreateMobileDefault();
            var planner = new ReachyDeterministicBehaviorPlanner(policy);
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: "Okay.",
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Curious,
                gesture: ReachyBehaviorGesture.Nod,
                urgency: ReachyBehaviorUrgency.High,
                timing: new ReachyBehaviorTimingConstraints(25, 4_500));
            ReachyBehaviorMotionSnapshot motion = NeutralMotion();

            ReachyBehaviorPlanResult first = planner.Plan(
                intent,
                worldSnapshot: null,
                motion,
                SafeMotion(),
                PlanningTimestamp);
            ReachyBehaviorPlanResult second = planner.Plan(
                intent,
                worldSnapshot: null,
                motion,
                SafeMotion(),
                PlanningTimestamp);

            Equal(true, first.Succeeded, "first deterministic gesture plan");
            Equal(true, second.Succeeded, "second deterministic gesture plan");
            ReachyBehaviorTrajectoryPlan a = first.Plan ??
                throw new InvalidOperationException("First RMA-152 plan was null.");
            ReachyBehaviorTrajectoryPlan b = second.Plan ??
                throw new InvalidOperationException("Second RMA-152 plan was null.");
            Equal(a.Frames.Count, b.Frames.Count, "deterministic frame count");
            for (int frameIndex = 0; frameIndex < a.Frames.Count; ++frameIndex)
            {
                Equal(
                    a.Frames[frameIndex].OffsetMilliseconds,
                    b.Frames[frameIndex].OffsetMilliseconds,
                    "deterministic frame timing");
                for (int actuator = 0;
                    actuator < ReachyBehaviorPlannerActuators.Count;
                    ++actuator)
                {
                    Equal(
                        a.Frames[frameIndex].TargetPositionsRadians[actuator],
                        b.Frames[frameIndex].TargetPositionsRadians[actuator],
                        "deterministic actuator target");
                }
            }
            AssertPlanWithinPolicy(a, motion.PositionsRadians, policy);
            Equal(
                true,
                Math.Abs(
                    a.Frames[0].TargetPositionsRadians[
                        ReachyBehaviorPlannerActuators.RightAntenna]) > 0.0,
                "expression coordinates antenna motion");
        }


        private static void MotionPlanningIsRelativeToAuthoritativeState()
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
            ReachyBehaviorIntent expressionOnly = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Attentive,
                gesture: ReachyBehaviorGesture.None,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);

            ReachyBehaviorPlanResult expressionResult = planner.Plan(
                expressionOnly,
                worldSnapshot: null,
                motion,
                SafeMotion(),
                PlanningTimestamp);
            Equal(true, expressionResult.Succeeded, "relative expression plan");
            ReachyBehaviorTrajectoryFrame expressionFrame =
                expressionResult.Plan?.Frames[0] ??
                throw new InvalidOperationException(
                    "Relative expression plan had no frame.");
            Equal(
                positions[ReachyBehaviorPlannerActuators.BodyYaw],
                expressionFrame.TargetPositionsRadians[
                    ReachyBehaviorPlannerActuators.BodyYaw],
                "expression preserves unrelated body yaw");

            WorldModelSnapshot world = CreateWorldSnapshot(
                entityId: "entity-right",
                visible: true,
                confidence: 0.95,
                coverageState: VisionCoverageState.Normal,
                validFraction: 0.95,
                stopTurning: false,
                centerX: 0.80,
                centerY: 0.50);
            ReachyBehaviorIntent gazeOnly = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: new ReachyBehaviorGazeTarget("entity-right"),
                expression: null,
                gesture: ReachyBehaviorGesture.None,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);
            ReachyBehaviorPlanResult gazeResult = planner.Plan(
                gazeOnly,
                world,
                motion,
                SafeMotion(),
                PlanningTimestamp);
            Equal(true, gazeResult.Succeeded, "relative gaze plan");
            double gazeYaw = gazeResult.Plan?.Frames[0].TargetPositionsRadians[
                ReachyBehaviorPlannerActuators.BodyYaw] ?? double.NaN;
            Equal(
                true,
                gazeYaw < positions[ReachyBehaviorPlannerActuators.BodyYaw],
                "right-side gaze applies negative physical body-yaw delta to authoritative pose");
        }

        private static void SafetyInterlocksBlockMotionWithoutBlockingSpeechOnlyIntent()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            ReachyBehaviorIntent moving = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Curious,
                gesture: ReachyBehaviorGesture.SmallHeadTilt,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);
            ReachyBehaviorSafetySnapshot[] blocked =
            {
                new ReachyBehaviorSafetySnapshot(false, true, false, false, false, false),
                new ReachyBehaviorSafetySnapshot(true, false, false, false, false, false),
                new ReachyBehaviorSafetySnapshot(true, true, true, false, false, false),
                new ReachyBehaviorSafetySnapshot(true, true, false, true, false, false),
                new ReachyBehaviorSafetySnapshot(true, true, false, false, true, false),
                new ReachyBehaviorSafetySnapshot(true, true, false, false, false, true),
            };
            for (int index = 0; index < blocked.Length; ++index)
            {
                ReachyBehaviorPlanResult result = planner.Plan(
                    moving,
                    worldSnapshot: null,
                    NeutralMotion(),
                    blocked[index],
                    PlanningTimestamp);
                Equal(
                    ReachyBehaviorPlannerStatus.SafetyInterlockActive,
                    result.Status,
                    "motion safety interlock " + index);
                Equal(null, result.Plan, "blocked motion plan " + index);
            }

            ReachyBehaviorIntent speechOnly = new ReachyBehaviorIntent(
                speech: "I cannot move safely right now.",
                gazeTarget: null,
                expression: null,
                gesture: null,
                urgency: null,
                timing: null);
            ReachyBehaviorPlanResult speech = planner.Plan(
                speechOnly,
                worldSnapshot: null,
                NeutralMotion(),
                blocked[0],
                PlanningTimestamp);
            Equal(true, speech.Succeeded, "speech-only intent under motion interlock");
            Equal(0, speech.Plan?.Frames.Count ?? -1, "speech-only motion frame count");
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        public ReachyBehaviorPlanResult Plan(
            ReachyBehaviorIntent intent,
            WorldModelSnapshot? worldSnapshot,
            ReachyBehaviorMotionSnapshot motionSnapshot,
            ReachyBehaviorSafetySnapshot safetySnapshot,
            long planningTimestampNanoseconds,
            CancellationToken cancellationToken = default)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }
            if (motionSnapshot == null)
            {
                throw new ArgumentNullException(nameof(motionSnapshot));
            }
            if (safetySnapshot == null)
            {
                throw new ArgumentNullException(nameof(safetySnapshot));
            }
            if (planningTimestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(planningTimestampNanoseconds));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.Cancelled,
                    "cancelled-safe-rest-replan-required");
            }

            bool motionRequested = RequestsMotion(intent);
            if (!motionRequested)
            {
                int speechOffset = intent.Timing?.StartDelayMilliseconds ?? 0;
                if (intent.Timing?.MaximumDurationMilliseconds is int maximum &&
                    speechOffset > maximum)
                {
                    return Failure(
                        ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                        "speech-start-delay-exceeds-maximum-duration");
                }
                return Success(
                    new ReachyBehaviorTrajectoryPlan(
                        ReachyBehaviorTrajectoryPurpose.Intent,
                        intent.Speech,
                        speechOffset,
                        resolvedGazeEntityId: null,
                        Array.Empty<ReachyBehaviorTrajectoryFrame>()));
            }

            if (!safetySnapshot.AllowsMotion)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.SafetyInterlockActive,
                    SafetyDiagnostic(safetySnapshot));
            }
            if (!ValidateMotionSnapshot(motionSnapshot))
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.MotionStateInvalid,
                    "authoritative-motion-state-outside-planner-envelope");
            }

            double[] baseTarget = CopyTarget(
                motionSnapshot.PositionsRadians);
            string? resolvedGazeEntityId = null;
            if (intent.GazeTarget != null)
            {
                ReachyBehaviorPlanResult? gazeFailure = ResolveAndApplyGaze(
                    intent.GazeTarget,
                    worldSnapshot,
                    planningTimestampNanoseconds,
                    baseTarget,
                    out resolvedGazeEntityId);
                if (gazeFailure != null)
                {
                    return gazeFailure;
                }
            }

            ApplyExpression(intent.Expression, baseTarget);
            if (!ValidateTarget(baseTarget))
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                    "resolved-base-pose-exceeds-soft-actuator-envelope");
            }

            List<double[]> poses = CreateGesturePoses(
                intent.Gesture,
                baseTarget);
            int startDelay = intent.Timing?.StartDelayMilliseconds ?? 0;
            int maximumDuration = Math.Min(
                intent.Timing?.MaximumDurationMilliseconds ??
                    policy.MaximumPlanDurationMilliseconds,
                policy.MaximumPlanDurationMilliseconds);
            double urgencyScale = UrgencyScale(intent.Urgency);

            ReachyBehaviorPlanResult? trajectoryFailure = BuildTrajectory(
                ReachyBehaviorTrajectoryPurpose.Intent,
                intent.Speech,
                startDelay,
                maximumDuration,
                resolvedGazeEntityId,
                motionSnapshot.PositionsRadians,
                poses,
                urgencyScale,
                out ReachyBehaviorTrajectoryPlan? plan);
            if (trajectoryFailure != null)
            {
                return trajectoryFailure;
            }

            return Success(
                plan ?? throw new InvalidOperationException(
                    "Successful behavior trajectory construction returned no plan."));
        }

        public ReachyBehaviorPlanResult PlanSafeRest(
            ReachyBehaviorMotionSnapshot motionSnapshot,
            ReachyBehaviorSafetySnapshot safetySnapshot,
            CancellationToken cancellationToken = default)
        {
            if (motionSnapshot == null)
            {
                throw new ArgumentNullException(nameof(motionSnapshot));
            }
            if (safetySnapshot == null)
            {
                throw new ArgumentNullException(nameof(safetySnapshot));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.Cancelled,
                    "safe-rest-planning-cancelled");
            }
            if (!safetySnapshot.AllowsMotion)
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.SafetyInterlockActive,
                    SafetyDiagnostic(safetySnapshot));
            }
            if (!ValidateMotionSnapshot(motionSnapshot))
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.MotionStateInvalid,
                    "safe-rest-source-state-outside-planner-envelope");
            }

            var neutral = new double[ReachyBehaviorPlannerActuators.Count];
            ReachyBehaviorPlanResult? trajectoryFailure = BuildTrajectory(
                ReachyBehaviorTrajectoryPurpose.SafeRest,
                speech: null,
                startDelayMilliseconds: 0,
                maximumDurationMilliseconds: policy.MaximumPlanDurationMilliseconds,
                resolvedGazeEntityId: null,
                motionSnapshot.PositionsRadians,
                new List<double[]> { neutral },
                urgencyScale: 1.0,
                out ReachyBehaviorTrajectoryPlan? plan);
            if (trajectoryFailure != null)
            {
                return trajectoryFailure;
            }

            return Success(
                plan ?? throw new InvalidOperationException(
                    "Successful safe-rest trajectory construction returned no plan."));
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        internal void ValidateBaselinePolicy(
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            if (baselinePolicy == null)
            {
                throw new ArgumentNullException(nameof(baselinePolicy));
            }
            if (baselinePolicy.SearchCenterBodyYawRadians +
                    baselinePolicy.SearchBodyYawAmplitudeRadians >
                policy.MaximumGazeBodyYawRadians ||
                baselinePolicy.SearchCenterHeadYawRadians +
                    baselinePolicy.SearchHeadYawAmplitudeRadians >
                policy.MaximumGazeHeadYawRadians ||
                baselinePolicy.SearchCenterHeadPitchRadians >
                    policy.MaximumGazeHeadPitchRadians)
            {
                throw new ArgumentException(
                    "Baseline gaze-search bounds exceed the RMA-152 planner gaze envelope.",
                    nameof(baselinePolicy));
            }
        }

        internal ReachyBehaviorPlanResult PlanBaseline(
            ReachyBaselineBehaviorRequest request,
            ReachyBaselineBehaviorPolicy baselinePolicy,
            WorldModelSnapshot? worldSnapshot,
            ReachyBehaviorMotionSnapshot motionSnapshot,
            ReachyBehaviorSafetySnapshot safetySnapshot,
            long planningTimestampNanoseconds,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (baselinePolicy == null)
            {
                throw new ArgumentNullException(nameof(baselinePolicy));
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
                    "baseline-behavior-planning-cancelled");
            }

            if (request.Kind == ReachyBaselineBehaviorKind.SleepRest)
            {
                return PlanSafeRest(
                    motionSnapshot,
                    safetySnapshot,
                    cancellationToken);
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
            if (request.Kind == ReachyBaselineBehaviorKind.Wake &&
                !IsNeutralWakeSource(motionSnapshot, baselinePolicy))
            {
                return Failure(
                    ReachyBehaviorPlannerStatus.MotionStateInvalid,
                    "wake-requires-fresh-neutral-awake-authoritative-state");
            }

            double[] baseTarget = CopyTarget(motionSnapshot.PositionsRadians);
            string? resolvedGazeEntityId = null;
            List<double[]> poses;
            double urgencyScale;

            switch (request.Kind)
            {
                case ReachyBaselineBehaviorKind.NeutralIdle:
                    poses = CreateIdlePoses(baseTarget, baselinePolicy);
                    urgencyScale = 0.55;
                    break;
                case ReachyBaselineBehaviorKind.Listening:
                    ApplyExpression(ReachyBehaviorExpression.Attentive, baseTarget);
                    poses = OnePose(baseTarget);
                    urgencyScale = 0.55;
                    break;
                case ReachyBaselineBehaviorKind.Speaking:
                    poses = CreateSpeakingPoses(
                        baseTarget,
                        request,
                        baselinePolicy);
                    urgencyScale = 0.75;
                    break;
                case ReachyBaselineBehaviorKind.Acknowledgment:
                    poses = CreateGesturePoses(
                        ReachyBehaviorGesture.Nod,
                        baseTarget);
                    urgencyScale = 0.75;
                    break;
                case ReachyBaselineBehaviorKind.Curiosity:
                    ApplyExpression(ReachyBehaviorExpression.Curious, baseTarget);
                    poses = CreateGesturePoses(
                        ReachyBehaviorGesture.SmallHeadTilt,
                        baseTarget);
                    urgencyScale = 0.55;
                    break;
                case ReachyBaselineBehaviorKind.Surprise:
                    ApplyExpression(ReachyBehaviorExpression.Surprised, baseTarget);
                    poses = CreateGesturePoses(
                        ReachyBehaviorGesture.Recoil,
                        baseTarget);
                    urgencyScale = 1.0;
                    break;
                case ReachyBaselineBehaviorKind.GazeAcquisition:
                {
                    ReachyBehaviorPlanResult? gazeFailure =
                        ResolveAndApplyGaze(
                            new ReachyBehaviorGazeTarget(
                                RequireBaselineGazeEntityId(request)),
                            worldSnapshot,
                            planningTimestampNanoseconds,
                            baseTarget,
                            out resolvedGazeEntityId);
                    if (gazeFailure != null)
                    {
                        return gazeFailure;
                    }
                    ApplyExpression(
                        ReachyBehaviorExpression.Attentive,
                        baseTarget);
                    poses = OnePose(baseTarget);
                    urgencyScale = 0.75;
                    break;
                }
                case ReachyBaselineBehaviorKind.GazeSearch:
                {
                    ReachyBehaviorPlanResult? searchFailure =
                        ResolveLostTargetSearch(
                            RequireBaselineGazeEntityId(request),
                            request.SearchCoverageTimestampNanoseconds ??
                                throw new InvalidOperationException(
                                    "Gaze-search request omitted current coverage timestamp."),
                            request.SearchCoverage ??
                                throw new InvalidOperationException(
                                    "Gaze-search request omitted current coverage."),
                            baselinePolicy,
                            worldSnapshot,
                            planningTimestampNanoseconds,
                            baseTarget,
                            out resolvedGazeEntityId,
                            out poses);
                    if (searchFailure != null)
                    {
                        return searchFailure;
                    }
                    urgencyScale = 0.55;
                    break;
                }
                case ReachyBaselineBehaviorKind.UnavailableError:
                    ApplyExpression(ReachyBehaviorExpression.Concerned, baseTarget);
                    poses = OnePose(baseTarget);
                    urgencyScale = 0.55;
                    break;
                case ReachyBaselineBehaviorKind.Wake:
                    poses = CreateWakePoses(baseTarget, baselinePolicy);
                    urgencyScale = 0.55;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        request.Kind,
                        "Unsupported baseline behavior kind.");
            }

            ReachyBehaviorPlanResult? trajectoryFailure = BuildTrajectory(
                ReachyBehaviorTrajectoryPurpose.Intent,
                speech: null,
                startDelayMilliseconds: 0,
                maximumDurationMilliseconds: policy.MaximumPlanDurationMilliseconds,
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
                    "Successful baseline behavior construction returned no plan."));
        }

        private static string RequireBaselineGazeEntityId(
            ReachyBaselineBehaviorRequest request)
        {
            return request.GazeEntityId ??
                throw new InvalidOperationException(
                    "A gaze baseline behavior was missing its validated entity ID.");
        }

        private static List<double[]> OnePose(double[] target)
        {
            return new List<double[]> { CopyTarget(target) };
        }
    }
}

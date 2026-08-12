#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public sealed class ReachyVisualServoGazeLoop
    {
        private readonly ReachyDeterministicBehaviorPlanner planner;
        private readonly ReachyBehaviorTrajectoryExecutor executor;
        private readonly IReachyVisualServoFeedbackSource feedbackSource;
        private readonly IReachyVisualServoPollDelay pollDelay;
        private readonly ReachyVisualServoPolicy policy;

        public ReachyVisualServoGazeLoop(
            ReachyDeterministicBehaviorPlanner planner,
            ReachyBehaviorTrajectoryExecutor executor,
            IReachyVisualServoFeedbackSource feedbackSource,
            IReachyVisualServoPollDelay pollDelay,
            ReachyVisualServoPolicy policy)
        {
            this.planner = planner ??
                throw new ArgumentNullException(nameof(planner));
            this.executor = executor ??
                throw new ArgumentNullException(nameof(executor));
            this.feedbackSource = feedbackSource ??
                throw new ArgumentNullException(nameof(feedbackSource));
            this.pollDelay = pollDelay ??
                throw new ArgumentNullException(nameof(pollDelay));
            this.policy = policy ??
                throw new ArgumentNullException(nameof(policy));
        }

        public static string ContractId => ReachyVisualServoPolicy.CurrentContractId;

        public async Task<ReachyVisualServoResult> CenterAsync(
            string entityId,
            CancellationToken cancellationToken = default)
        {
            ReachyBehaviorIntent gazeIntent = CreateGazeCorrectionIntent(entityId);

            using (var timeout = new CancellationTokenSource())
            using (CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token))
            {
                timeout.CancelAfter(policy.MaximumLoopDurationMilliseconds);
                CancellationToken loopToken = linked.Token;
                int adjustmentCount = 0;
                int submittedFrameCount = 0;

                ReachyVisualServoFeedbackSample sample;
                try
                {
                    sample = await feedbackSource.CaptureAsync(loopToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (loopToken.IsCancellationRequested)
                {
                    return CancellationResult(
                        cancellationToken,
                        adjustmentCount,
                        submittedFrameCount,
                        0.0,
                        0.0);
                }
                catch (ReachyVisualServoFeedbackUnavailableException exception)
                {
                    return Result(
                        ReachyVisualServoStatus.FeedbackUnavailable,
                        exception.DiagnosticCode,
                        adjustmentCount,
                        submittedFrameCount,
                        0.0,
                        0.0);
                }

                while (true)
                {
                    WorldEntitySnapshot? entity = FindEntity(
                        sample.WorldSnapshot,
                        entityId);
                    ReachyVisualServoResult? stop = EvaluateStopConditions(
                        sample,
                        entity,
                        adjustmentCount,
                        submittedFrameCount);
                    if (stop != null)
                    {
                        return stop;
                    }

                    WorldEntitySnapshot currentEntity = entity ??
                        throw new InvalidOperationException(
                            "A visual-servo target disappeared after stop-condition evaluation.");
                    GetTrackingError(
                        currentEntity,
                        out double horizontalError,
                        out double verticalError);
                    if (WithinTolerance(horizontalError, verticalError))
                    {
                        return Result(
                            ReachyVisualServoStatus.Centered,
                            "visual-servo-centered-from-transformed-tracking",
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }
                    if (adjustmentCount >= policy.MaximumIterations)
                    {
                        return Result(
                            ReachyVisualServoStatus.TimedOut,
                            "visual-servo-iteration-limit-reached",
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }

                    ReachyBehaviorPlanResult planResult = planner.Plan(
                        gazeIntent,
                        sample.WorldSnapshot,
                        sample.MotionSnapshot,
                        sample.SafetySnapshot,
                        sample.TimestampNanoseconds,
                        loopToken);
                    if (!planResult.Succeeded || planResult.Plan == null)
                    {
                        if (planResult.Status ==
                            ReachyBehaviorPlannerStatus.Cancelled)
                        {
                            return CancellationResult(
                                cancellationToken,
                                adjustmentCount,
                                submittedFrameCount,
                                horizontalError,
                                verticalError);
                        }
                        return PlannerFailure(
                            planResult,
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }

                    ReachyBehaviorTrajectoryPlan plan = planResult.Plan;
                    if (plan.Frames.Count == 0)
                    {
                        return Result(
                            ReachyVisualServoStatus.PlannerRejected,
                            "visual-servo-planner-produced-no-motion-for-off-center-target",
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }

                    ReachyVisionFrameIdentity beforeFrame =
                        currentEntity.LatestFrameIdentity;
                    ReachyBehaviorMotionSnapshot beforeMotion =
                        sample.MotionSnapshot;
                    ulong beforeAuthoritativeSequence =
                        sample.AuthoritativeStateSequence;

                    ReachyBehaviorTrajectoryExecutionResult execution;
                    try
                    {
                        execution = await executor.ExecuteAsync(plan, loopToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (loopToken.IsCancellationRequested)
                    {
                        return CancellationResult(
                            cancellationToken,
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }

                    submittedFrameCount = checked(
                        submittedFrameCount + execution.SubmittedFrameCount);
                    if (!execution.Completed)
                    {
                        if (execution.Status ==
                            ReachyBehaviorTrajectoryExecutionStatus.Cancelled)
                        {
                            return CancellationResult(
                                cancellationToken,
                                adjustmentCount,
                                submittedFrameCount,
                                horizontalError,
                                verticalError);
                        }
                        return Result(
                            ReachyVisualServoStatus.ExecutionRejected,
                            "visual-servo-" + execution.DiagnosticCode,
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }
                    adjustmentCount = checked(adjustmentCount + 1);

                    FeedbackWaitResult waitResult;
                    try
                    {
                        waitResult = await WaitForPostMotionFrameAsync(
                                entityId,
                                beforeFrame,
                                beforeMotion,
                                beforeAuthoritativeSequence,
                                adjustmentCount,
                                submittedFrameCount,
                                loopToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (loopToken.IsCancellationRequested)
                    {
                        return CancellationResult(
                            cancellationToken,
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError);
                    }

                    if (waitResult.StopResult != null)
                    {
                        return waitResult.StopResult;
                    }
                    sample = waitResult.Sample ??
                        throw new InvalidOperationException(
                            "Visual-servo feedback wait completed without a sample.");
                }
            }
        }

        private async Task<FeedbackWaitResult> WaitForPostMotionFrameAsync(
            string entityId,
            ReachyVisionFrameIdentity beforeFrame,
            ReachyBehaviorMotionSnapshot beforeMotion,
            ulong beforeAuthoritativeSequence,
            int adjustmentCount,
            int submittedFrameCount,
            CancellationToken cancellationToken)
        {
            ulong observedMotionSequence = 0UL;
            ulong lastAuthoritativeSequence = beforeAuthoritativeSequence;
            ReachyVisionFrameIdentity lastFrame = beforeFrame;

            while (true)
            {
                await pollDelay.DelayAsync(
                        policy.FeedbackPollDelayMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
                ReachyVisualServoFeedbackSample next;
                try
                {
                    next = await feedbackSource.CaptureAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ReachyVisualServoFeedbackUnavailableException exception)
                {
                    return FeedbackWaitResult.Stopped(
                        Result(
                            ReachyVisualServoStatus.FeedbackUnavailable,
                            exception.DiagnosticCode,
                            adjustmentCount,
                            submittedFrameCount,
                            0.0,
                            0.0));
                }

                WorldEntitySnapshot? entity = FindEntity(
                    next.WorldSnapshot,
                    entityId);
                ReachyVisualServoResult? stop = EvaluateStopConditions(
                    next,
                    entity,
                    adjustmentCount,
                    submittedFrameCount);
                if (stop != null)
                {
                    return FeedbackWaitResult.Stopped(stop);
                }

                WorldEntitySnapshot currentEntity = entity ??
                    throw new InvalidOperationException(
                        "A visual-servo target disappeared after feedback stop evaluation.");
                GetTrackingError(
                    currentEntity,
                    out double horizontalError,
                    out double verticalError);

                if (!SameTrackingContinuity(
                        beforeFrame,
                        currentEntity.LatestFrameIdentity))
                {
                    return FeedbackWaitResult.Stopped(
                        Result(
                            ReachyVisualServoStatus.FrameDiscontinuity,
                            "visual-servo-transformed-frame-continuity-changed",
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError));
                }
                ReachyVisionFrameIdentity nextFrame =
                    currentEntity.LatestFrameIdentity;
                if (next.AuthoritativeStateSequence < lastAuthoritativeSequence ||
                    HasFrameRegressed(lastFrame, nextFrame))
                {
                    return FeedbackWaitResult.Stopped(
                        Result(
                            ReachyVisualServoStatus.FrameDiscontinuity,
                            "visual-servo-authoritative-feedback-regressed",
                            adjustmentCount,
                            submittedFrameCount,
                            horizontalError,
                            verticalError));
                }

                if (observedMotionSequence == 0UL &&
                    next.AuthoritativeStateSequence > beforeAuthoritativeSequence &&
                    HasObservedPhysicalMotion(beforeMotion, next.MotionSnapshot))
                {
                    observedMotionSequence = next.AuthoritativeStateSequence;
                }

                if (observedMotionSequence != 0UL &&
                    IsNewerTransformedFrame(beforeFrame, nextFrame) &&
                    nextFrame.AuthoritativeSequence >= observedMotionSequence)
                {
                    return FeedbackWaitResult.ContinueWith(next);
                }

                lastAuthoritativeSequence = next.AuthoritativeStateSequence;
                lastFrame = nextFrame;
            }
        }

        private ReachyVisualServoResult? EvaluateStopConditions(
            ReachyVisualServoFeedbackSample sample,
            WorldEntitySnapshot? entity,
            int adjustmentCount,
            int submittedFrameCount)
        {
            double horizontalError = 0.0;
            double verticalError = 0.0;
            if (entity != null)
            {
                GetTrackingError(
                    entity,
                    out horizontalError,
                    out verticalError);
            }

            if (sample.SafetySnapshot.LoadLimitActive)
            {
                return Result(
                    ReachyVisualServoStatus.LoadLimit,
                    "visual-servo-stopped-on-authoritative-load-limit",
                    adjustmentCount,
                    submittedFrameCount,
                    horizontalError,
                    verticalError);
            }
            if (entity == null || !entity.IsCurrentlyVisible)
            {
                return Result(
                    ReachyVisualServoStatus.TargetLost,
                    "visual-servo-target-lost",
                    adjustmentCount,
                    submittedFrameCount,
                    horizontalError,
                    verticalError);
            }
            if (!CoverageAllowsTurning(entity.Coverage))
            {
                return Result(
                    ReachyVisualServoStatus.CoverageBlocked,
                    "visual-servo-transformed-image-coverage-blocked",
                    adjustmentCount,
                    submittedFrameCount,
                    horizontalError,
                    verticalError);
            }
            if (!sample.SafetySnapshot.AllowsMotion)
            {
                return Result(
                    ReachyVisualServoStatus.SafetyInterlock,
                    "visual-servo-authoritative-safety-interlock-active",
                    adjustmentCount,
                    submittedFrameCount,
                    horizontalError,
                    verticalError);
            }
            return null;
        }

        private ReachyVisualServoResult PlannerFailure(
            ReachyBehaviorPlanResult plannerResult,
            int adjustmentCount,
            int submittedFrameCount,
            double horizontalError,
            double verticalError)
        {
            ReachyVisualServoStatus status = plannerResult.Status switch
            {
                ReachyBehaviorPlannerStatus.GazeTargetNotFound =>
                    ReachyVisualServoStatus.TargetLost,
                ReachyBehaviorPlannerStatus.GazeTargetNotVisible =>
                    ReachyVisualServoStatus.TargetLost,
                ReachyBehaviorPlannerStatus.GazeCoverageBlocked =>
                    ReachyVisualServoStatus.CoverageBlocked,
                ReachyBehaviorPlannerStatus.SafetyInterlockActive =>
                    ReachyVisualServoStatus.SafetyInterlock,
                ReachyBehaviorPlannerStatus.Cancelled =>
                    ReachyVisualServoStatus.Cancelled,
                _ => ReachyVisualServoStatus.PlannerRejected,
            };
            return Result(
                status,
                "visual-servo-planner-" + plannerResult.DiagnosticCode,
                adjustmentCount,
                submittedFrameCount,
                horizontalError,
                verticalError);
        }

        private ReachyVisualServoResult CancellationResult(
            CancellationToken callerToken,
            int adjustmentCount,
            int submittedFrameCount,
            double horizontalError,
            double verticalError)
        {
            return callerToken.IsCancellationRequested
                ? Result(
                    ReachyVisualServoStatus.Cancelled,
                    "visual-servo-cancelled",
                    adjustmentCount,
                    submittedFrameCount,
                    horizontalError,
                    verticalError)
                : Result(
                    ReachyVisualServoStatus.TimedOut,
                    "visual-servo-timeout-awaiting-authoritative-feedback",
                    adjustmentCount,
                    submittedFrameCount,
                    horizontalError,
                    verticalError);
        }

        private bool CoverageAllowsTurning(WorldCoverageContext coverage)
        {
            return !coverage.ShouldStopVisionDrivenTurning &&
                coverage.ValidFraction >= policy.MinimumValidCoverageFraction &&
                (coverage.State == VisionCoverageState.Normal ||
                    coverage.State == VisionCoverageState.Degraded);
        }

        private bool WithinTolerance(
            double horizontalError,
            double verticalError)
        {
            return Math.Abs(horizontalError) <=
                    policy.HorizontalToleranceNormalized &&
                Math.Abs(verticalError) <=
                    policy.VerticalToleranceNormalized;
        }

        private bool HasObservedPhysicalMotion(
            ReachyBehaviorMotionSnapshot before,
            ReachyBehaviorMotionSnapshot after)
        {
            for (int index = ReachyBehaviorPlannerActuators.BodyYaw;
                index <= ReachyBehaviorPlannerActuators.Stewart6;
                ++index)
            {
                if (Math.Abs(
                        after.PositionsRadians[index] -
                        before.PositionsRadians[index]) >=
                    policy.MinimumObservedMotionRadians)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameTrackingContinuity(
            ReachyVisionFrameIdentity before,
            ReachyVisionFrameIdentity after)
        {
            return string.Equals(
                    before.CameraId,
                    after.CameraId,
                    StringComparison.Ordinal) &&
                before.SourceSessionId == after.SourceSessionId &&
                before.ContinuityId == after.ContinuityId;
        }

        private static bool HasFrameRegressed(
            ReachyVisionFrameIdentity before,
            ReachyVisionFrameIdentity after)
        {
            return after.SourceSequence < before.SourceSequence ||
                after.SourceTimestampNanoseconds <
                    before.SourceTimestampNanoseconds ||
                after.AuthoritativeSequence < before.AuthoritativeSequence;
        }

        private static bool IsNewerTransformedFrame(
            ReachyVisionFrameIdentity before,
            ReachyVisionFrameIdentity after)
        {
            return after.SourceSequence > before.SourceSequence &&
                after.SourceTimestampNanoseconds >
                    before.SourceTimestampNanoseconds &&
                after.AuthoritativeSequence > before.AuthoritativeSequence;
        }

        private static ReachyBehaviorIntent CreateGazeCorrectionIntent(
            string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) ||
                entityId.Length >
                    ReachyBehaviorIntentPolicy.MaximumEntityIdCharacters)
            {
                throw new ArgumentException(
                    "Visual-servo entity IDs must be non-empty and within " +
                        "the behavior-intent bound.",
                    nameof(entityId));
            }

            return new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: new ReachyBehaviorGazeTarget(entityId),
                expression: null,
                gesture: null,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);
        }

        private static WorldEntitySnapshot? FindEntity(
            WorldModelSnapshot snapshot,
            string entityId)
        {
            for (int index = 0; index < snapshot.Entities.Count; ++index)
            {
                WorldEntitySnapshot candidate = snapshot.Entities[index];
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

        private static void GetTrackingError(
            WorldEntitySnapshot entity,
            out double horizontalError,
            out double verticalError)
        {
            horizontalError = entity.Bounds.CenterX - 0.5;
            verticalError = entity.Bounds.CenterY - 0.5;
        }

        private static ReachyVisualServoResult Result(
            ReachyVisualServoStatus status,
            string diagnosticCode,
            int adjustmentCount,
            int submittedFrameCount,
            double horizontalError,
            double verticalError)
        {
            return new ReachyVisualServoResult(
                status,
                diagnosticCode,
                adjustmentCount,
                submittedFrameCount,
                horizontalError,
                verticalError);
        }

        private sealed class FeedbackWaitResult
        {
            private FeedbackWaitResult(
                ReachyVisualServoFeedbackSample? sample,
                ReachyVisualServoResult? stopResult)
            {
                Sample = sample;
                StopResult = stopResult;
            }

            internal ReachyVisualServoFeedbackSample? Sample { get; }

            internal ReachyVisualServoResult? StopResult { get; }

            internal static FeedbackWaitResult ContinueWith(
                ReachyVisualServoFeedbackSample sample)
            {
                return new FeedbackWaitResult(
                    sample ?? throw new ArgumentNullException(nameof(sample)),
                    stopResult: null);
            }

            internal static FeedbackWaitResult Stopped(
                ReachyVisualServoResult result)
            {
                return new FeedbackWaitResult(
                    sample: null,
                    result ?? throw new ArgumentNullException(nameof(result)));
            }
        }
    }
}

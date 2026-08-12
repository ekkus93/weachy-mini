#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Behavior
{
    public enum ReachyBehaviorTrajectoryExecutionStatus
    {
        Completed = 0,
        Cancelled = 1,
        SubmissionRejected = 2,
    }

    public sealed class ReachyBehaviorTrajectoryExecutionResult
    {
        internal ReachyBehaviorTrajectoryExecutionResult(
            ReachyBehaviorTrajectoryExecutionStatus status,
            string diagnosticCode,
            int submittedFrameCount,
            ReachyBehaviorTargetSubmissionStatus? submissionStatus)
        {
            if (submittedFrameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(submittedFrameCount));
            }
            Status = status;
            DiagnosticCode = diagnosticCode ?? string.Empty;
            SubmittedFrameCount = submittedFrameCount;
            SubmissionStatus = submissionStatus;
        }

        public ReachyBehaviorTrajectoryExecutionStatus Status { get; }

        public string DiagnosticCode { get; }

        public int SubmittedFrameCount { get; }

        public ReachyBehaviorTargetSubmissionStatus? SubmissionStatus { get; }

        public bool Completed =>
            Status == ReachyBehaviorTrajectoryExecutionStatus.Completed;
    }

    public interface IReachyBehaviorTrajectoryDelay
    {
        Task DelayAsync(
            int milliseconds,
            CancellationToken cancellationToken);
    }

    public sealed class ReachyBehaviorSystemTrajectoryDelay :
        IReachyBehaviorTrajectoryDelay
    {
        public Task DelayAsync(
            int milliseconds,
            CancellationToken cancellationToken)
        {
            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds));
            }
            return Task.Delay(milliseconds, cancellationToken);
        }
    }

    public sealed class ReachyBehaviorTrajectoryExecutor
    {
        private readonly IReachyBehaviorControllerTargetSink targetSink;
        private readonly IReachyBehaviorTrajectoryDelay delay;

        public ReachyBehaviorTrajectoryExecutor(
            IReachyBehaviorControllerTargetSink targetSink,
            IReachyBehaviorTrajectoryDelay delay)
        {
            this.targetSink = targetSink ??
                throw new ArgumentNullException(nameof(targetSink));
            this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public async Task<ReachyBehaviorTrajectoryExecutionResult> ExecuteAsync(
            ReachyBehaviorTrajectoryPlan plan,
            CancellationToken cancellationToken = default)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(submittedFrameCount: 0);
            }

            int submitted = 0;
            int previousOffsetMilliseconds = 0;
            for (int index = 0; index < plan.Frames.Count; ++index)
            {
                ReachyBehaviorTrajectoryFrame frame = plan.Frames[index];
                int delayMilliseconds = checked(
                    frame.OffsetMilliseconds - previousOffsetMilliseconds);
                try
                {
                    await delay.DelayAsync(
                            delayMilliseconds,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return Cancelled(submitted);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return Cancelled(submitted);
                }

                ReachyBehaviorTargetSubmissionStatus submission =
                    targetSink.Submit(frame);
                if (submission != ReachyBehaviorTargetSubmissionStatus.Accepted)
                {
                    return new ReachyBehaviorTrajectoryExecutionResult(
                        ReachyBehaviorTrajectoryExecutionStatus.SubmissionRejected,
                        SubmissionDiagnostic(submission),
                        submitted,
                        submission);
                }

                ++submitted;
                previousOffsetMilliseconds = frame.OffsetMilliseconds;
            }

            return new ReachyBehaviorTrajectoryExecutionResult(
                ReachyBehaviorTrajectoryExecutionStatus.Completed,
                "trajectory-execution-completed",
                submitted,
                submissionStatus: null);
        }

        private static ReachyBehaviorTrajectoryExecutionResult Cancelled(
            int submittedFrameCount)
        {
            return new ReachyBehaviorTrajectoryExecutionResult(
                ReachyBehaviorTrajectoryExecutionStatus.Cancelled,
                "trajectory-cancelled-safe-rest-replan-required",
                submittedFrameCount,
                submissionStatus: null);
        }

        private static string SubmissionDiagnostic(
            ReachyBehaviorTargetSubmissionStatus status)
        {
            return status switch
            {
                ReachyBehaviorTargetSubmissionStatus.QueueFull =>
                    "trajectory-controller-queue-full",
                ReachyBehaviorTargetSubmissionStatus.ControllerUnavailable =>
                    "trajectory-controller-unavailable",
                ReachyBehaviorTargetSubmissionStatus.ContractRejected =>
                    "trajectory-controller-contract-rejected",
                _ => "trajectory-controller-submission-rejected",
            };
        }
    }
}

#nullable enable

using System;

namespace ReachyMini.Behavior
{
    public enum ReachyVisualServoStatus
    {
        Centered = 0,
        TargetLost = 1,
        CoverageBlocked = 2,
        LoadLimit = 3,
        SafetyInterlock = 4,
        TimedOut = 5,
        Cancelled = 6,
        PlannerRejected = 7,
        ExecutionRejected = 8,
        FrameDiscontinuity = 9,
        FeedbackUnavailable = 10,
    }

    public sealed class ReachyVisualServoResult
    {
        internal ReachyVisualServoResult(
            ReachyVisualServoStatus status,
            string diagnosticCode,
            int adjustmentCount,
            int submittedFrameCount,
            double horizontalErrorNormalized,
            double verticalErrorNormalized)
        {
            if (!Enum.IsDefined(typeof(ReachyVisualServoStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
            if (string.IsNullOrWhiteSpace(diagnosticCode))
            {
                throw new ArgumentException(
                    "Visual-servo diagnostics cannot be empty.",
                    nameof(diagnosticCode));
            }
            if (adjustmentCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(adjustmentCount));
            }
            if (submittedFrameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(submittedFrameCount));
            }
            RequireFinite(
                horizontalErrorNormalized,
                nameof(horizontalErrorNormalized));
            RequireFinite(
                verticalErrorNormalized,
                nameof(verticalErrorNormalized));

            Status = status;
            DiagnosticCode = diagnosticCode;
            AdjustmentCount = adjustmentCount;
            SubmittedFrameCount = submittedFrameCount;
            HorizontalErrorNormalized = horizontalErrorNormalized;
            VerticalErrorNormalized = verticalErrorNormalized;
        }

        public ReachyVisualServoStatus Status { get; }

        public string DiagnosticCode { get; }

        public int AdjustmentCount { get; }

        public int SubmittedFrameCount { get; }

        public double HorizontalErrorNormalized { get; }

        public double VerticalErrorNormalized { get; }

        public bool Centered => Status == ReachyVisualServoStatus.Centered;

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}

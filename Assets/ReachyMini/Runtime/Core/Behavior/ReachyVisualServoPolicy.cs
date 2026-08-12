#nullable enable

using System;

namespace ReachyMini.Behavior
{
    public sealed class ReachyVisualServoPolicy
    {
        public const string CurrentContractId =
            "rma154_visual_servo_gaze_v1";

        public ReachyVisualServoPolicy(
            double horizontalToleranceNormalized,
            double verticalToleranceNormalized,
            double minimumValidCoverageFraction,
            double minimumObservedMotionRadians,
            int feedbackPollDelayMilliseconds,
            int maximumIterations,
            int maximumLoopDurationMilliseconds)
        {
            RequirePositiveUnit(
                horizontalToleranceNormalized,
                nameof(horizontalToleranceNormalized));
            RequirePositiveUnit(
                verticalToleranceNormalized,
                nameof(verticalToleranceNormalized));
            RequireUnit(
                minimumValidCoverageFraction,
                nameof(minimumValidCoverageFraction));
            RequirePositive(
                minimumObservedMotionRadians,
                nameof(minimumObservedMotionRadians));
            if (feedbackPollDelayMilliseconds <= 0 ||
                feedbackPollDelayMilliseconds > 1_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(feedbackPollDelayMilliseconds));
            }
            if (maximumIterations <= 0 || maximumIterations > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumIterations));
            }
            if (maximumLoopDurationMilliseconds <= 0 ||
                maximumLoopDurationMilliseconds > 60_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumLoopDurationMilliseconds));
            }

            HorizontalToleranceNormalized = horizontalToleranceNormalized;
            VerticalToleranceNormalized = verticalToleranceNormalized;
            MinimumValidCoverageFraction = minimumValidCoverageFraction;
            MinimumObservedMotionRadians = minimumObservedMotionRadians;
            FeedbackPollDelayMilliseconds = feedbackPollDelayMilliseconds;
            MaximumIterations = maximumIterations;
            MaximumLoopDurationMilliseconds = maximumLoopDurationMilliseconds;
        }

        public string ContractId => CurrentContractId;

        public double HorizontalToleranceNormalized { get; }

        public double VerticalToleranceNormalized { get; }

        public double MinimumValidCoverageFraction { get; }

        public double MinimumObservedMotionRadians { get; }

        public int FeedbackPollDelayMilliseconds { get; }

        public int MaximumIterations { get; }

        public int MaximumLoopDurationMilliseconds { get; }

        public static ReachyVisualServoPolicy CreateMobileDefault()
        {
            return new ReachyVisualServoPolicy(
                horizontalToleranceNormalized: 0.06,
                verticalToleranceNormalized: 0.06,
                minimumValidCoverageFraction: 0.50,
                minimumObservedMotionRadians: 1.0e-5,
                feedbackPollDelayMilliseconds: 20,
                maximumIterations: 8,
                maximumLoopDurationMilliseconds: 15_000);
        }

        private static void RequireUnit(double value, string name)
        {
            if (!IsFinite(value) || value < 0.0 || value > 1.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequirePositiveUnit(double value, string name)
        {
            RequireUnit(value, name);
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequirePositive(double value, string name)
        {
            if (!IsFinite(value) || value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

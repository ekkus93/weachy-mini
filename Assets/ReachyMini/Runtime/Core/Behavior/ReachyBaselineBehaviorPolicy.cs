#nullable enable

using System;

namespace ReachyMini.Behavior
{
    public sealed class ReachyBaselineBehaviorPolicy
    {
        public const string CurrentContractId =
            "rma153_baseline_behavior_library_v1";

        public ReachyBaselineBehaviorPolicy(
            double idlePitchAmplitudeRadians,
            double idleRollAmplitudeRadians,
            double idleAntennaAmplitudeRadians,
            double speakingTimingIntensity,
            double speakingMaximumPitchRadians,
            double speakingMaximumRollRadians,
            double speakingMaximumAntennaRadians,
            double minimumSearchConfidence,
            long maximumSearchTargetAgeNanoseconds,
            double searchCenterBodyYawRadians,
            double searchCenterHeadYawRadians,
            double searchCenterHeadPitchRadians,
            double searchBodyYawAmplitudeRadians,
            double searchHeadYawAmplitudeRadians,
            double wakeNeutralPositionToleranceRadians,
            double wakeNeutralVelocityToleranceRadiansPerSecond,
            double wakeHeadPitchRadians,
            double wakeAntennaRadians)
        {
            RequireNonNegative(
                idlePitchAmplitudeRadians,
                nameof(idlePitchAmplitudeRadians));
            RequireNonNegative(
                idleRollAmplitudeRadians,
                nameof(idleRollAmplitudeRadians));
            RequireNonNegative(
                idleAntennaAmplitudeRadians,
                nameof(idleAntennaAmplitudeRadians));
            RequireUnit(speakingTimingIntensity, nameof(speakingTimingIntensity));
            RequireNonNegative(
                speakingMaximumPitchRadians,
                nameof(speakingMaximumPitchRadians));
            RequireNonNegative(
                speakingMaximumRollRadians,
                nameof(speakingMaximumRollRadians));
            RequireNonNegative(
                speakingMaximumAntennaRadians,
                nameof(speakingMaximumAntennaRadians));
            RequireUnit(minimumSearchConfidence, nameof(minimumSearchConfidence));
            if (maximumSearchTargetAgeNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSearchTargetAgeNanoseconds));
            }
            RequireNonNegative(
                searchCenterBodyYawRadians,
                nameof(searchCenterBodyYawRadians));
            RequireNonNegative(
                searchCenterHeadYawRadians,
                nameof(searchCenterHeadYawRadians));
            RequireNonNegative(
                searchCenterHeadPitchRadians,
                nameof(searchCenterHeadPitchRadians));
            RequireNonNegative(
                searchBodyYawAmplitudeRadians,
                nameof(searchBodyYawAmplitudeRadians));
            RequireNonNegative(
                searchHeadYawAmplitudeRadians,
                nameof(searchHeadYawAmplitudeRadians));
            RequirePositive(
                wakeNeutralPositionToleranceRadians,
                nameof(wakeNeutralPositionToleranceRadians));
            RequirePositive(
                wakeNeutralVelocityToleranceRadiansPerSecond,
                nameof(wakeNeutralVelocityToleranceRadiansPerSecond));
            RequireNonNegative(
                wakeHeadPitchRadians,
                nameof(wakeHeadPitchRadians));
            RequireNonNegative(
                wakeAntennaRadians,
                nameof(wakeAntennaRadians));

            IdlePitchAmplitudeRadians = idlePitchAmplitudeRadians;
            IdleRollAmplitudeRadians = idleRollAmplitudeRadians;
            IdleAntennaAmplitudeRadians = idleAntennaAmplitudeRadians;
            SpeakingTimingIntensity = speakingTimingIntensity;
            SpeakingMaximumPitchRadians = speakingMaximumPitchRadians;
            SpeakingMaximumRollRadians = speakingMaximumRollRadians;
            SpeakingMaximumAntennaRadians = speakingMaximumAntennaRadians;
            MinimumSearchConfidence = minimumSearchConfidence;
            MaximumSearchTargetAgeNanoseconds = maximumSearchTargetAgeNanoseconds;
            SearchCenterBodyYawRadians = searchCenterBodyYawRadians;
            SearchCenterHeadYawRadians = searchCenterHeadYawRadians;
            SearchCenterHeadPitchRadians = searchCenterHeadPitchRadians;
            SearchBodyYawAmplitudeRadians = searchBodyYawAmplitudeRadians;
            SearchHeadYawAmplitudeRadians = searchHeadYawAmplitudeRadians;
            WakeNeutralPositionToleranceRadians =
                wakeNeutralPositionToleranceRadians;
            WakeNeutralVelocityToleranceRadiansPerSecond =
                wakeNeutralVelocityToleranceRadiansPerSecond;
            WakeHeadPitchRadians = wakeHeadPitchRadians;
            WakeAntennaRadians = wakeAntennaRadians;
        }

        public double IdlePitchAmplitudeRadians { get; }

        public double IdleRollAmplitudeRadians { get; }

        public double IdleAntennaAmplitudeRadians { get; }

        public double SpeakingTimingIntensity { get; }

        public double SpeakingMaximumPitchRadians { get; }

        public double SpeakingMaximumRollRadians { get; }

        public double SpeakingMaximumAntennaRadians { get; }

        public double MinimumSearchConfidence { get; }

        public long MaximumSearchTargetAgeNanoseconds { get; }

        public double SearchCenterBodyYawRadians { get; }

        public double SearchCenterHeadYawRadians { get; }

        public double SearchCenterHeadPitchRadians { get; }

        public double SearchBodyYawAmplitudeRadians { get; }

        public double SearchHeadYawAmplitudeRadians { get; }

        public double WakeNeutralPositionToleranceRadians { get; }

        public double WakeNeutralVelocityToleranceRadiansPerSecond { get; }

        public double WakeHeadPitchRadians { get; }

        public double WakeAntennaRadians { get; }

        public static ReachyBaselineBehaviorPolicy CreateMobileDefault()
        {
            return new ReachyBaselineBehaviorPolicy(
                idlePitchAmplitudeRadians: 0.010,
                idleRollAmplitudeRadians: 0.006,
                idleAntennaAmplitudeRadians: 0.020,
                speakingTimingIntensity: 0.55,
                speakingMaximumPitchRadians: 0.025,
                speakingMaximumRollRadians: 0.018,
                speakingMaximumAntennaRadians: 0.055,
                minimumSearchConfidence: 0.35,
                maximumSearchTargetAgeNanoseconds: 2_000_000_000L,
                searchCenterBodyYawRadians: 0.18,
                searchCenterHeadYawRadians: 0.10,
                searchCenterHeadPitchRadians: 0.08,
                searchBodyYawAmplitudeRadians: 0.06,
                searchHeadYawAmplitudeRadians: 0.045,
                wakeNeutralPositionToleranceRadians: 0.025,
                wakeNeutralVelocityToleranceRadiansPerSecond: 0.05,
                wakeHeadPitchRadians: 0.025,
                wakeAntennaRadians: 0.070);
        }

        private static void RequireUnit(double value, string name)
        {
            if (!IsFinite(value) || value < 0.0 || value > 1.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireNonNegative(double value, string name)
        {
            if (!IsFinite(value) || value < 0.0)
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

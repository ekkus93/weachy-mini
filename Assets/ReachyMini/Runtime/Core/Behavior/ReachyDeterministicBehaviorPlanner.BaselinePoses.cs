#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Behavior
{
    public sealed partial class ReachyDeterministicBehaviorPlanner
    {
        private static List<double[]> CreateIdlePoses(
            double[] baseTarget,
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            double[] inhale = CopyTarget(baseTarget);
            ApplyHeadOffset(
                inhale,
                yawRadians: 0.0,
                pitchRadians: baselinePolicy.IdlePitchAmplitudeRadians,
                rollRadians: baselinePolicy.IdleRollAmplitudeRadians);
            AddAntennaOffset(
                inhale,
                baselinePolicy.IdleAntennaAmplitudeRadians,
                -baselinePolicy.IdleAntennaAmplitudeRadians);

            double[] exhale = CopyTarget(baseTarget);
            ApplyHeadOffset(
                exhale,
                yawRadians: 0.0,
                pitchRadians: -0.80 * baselinePolicy.IdlePitchAmplitudeRadians,
                rollRadians: -baselinePolicy.IdleRollAmplitudeRadians);
            AddAntennaOffset(
                exhale,
                -0.90 * baselinePolicy.IdleAntennaAmplitudeRadians,
                0.90 * baselinePolicy.IdleAntennaAmplitudeRadians);

            return new List<double[]>
            {
                inhale,
                CopyTarget(baseTarget),
                exhale,
                CopyTarget(baseTarget),
            };
        }

        private static List<double[]> CreateSpeakingPoses(
            double[] baseTarget,
            ReachyBaselineBehaviorRequest request,
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            double intensity = SpeakingIntensity(request, baselinePolicy);
            if (intensity <= 0.0)
            {
                return OnePose(baseTarget);
            }

            double pitch =
                baselinePolicy.SpeakingMaximumPitchRadians * intensity;
            double roll =
                baselinePolicy.SpeakingMaximumRollRadians * intensity;
            double antenna =
                baselinePolicy.SpeakingMaximumAntennaRadians * intensity;

            double[] first = CopyTarget(baseTarget);
            ApplyHeadOffset(first, 0.0, pitch, roll);
            AddAntennaOffset(first, antenna, -antenna);

            double[] second = CopyTarget(baseTarget);
            ApplyHeadOffset(second, 0.0, -0.55 * pitch, -roll);
            AddAntennaOffset(second, -0.50 * antenna, 0.50 * antenna);

            double[] third = CopyTarget(baseTarget);
            ApplyHeadOffset(third, 0.0, 0.20 * pitch, 0.45 * roll);
            AddAntennaOffset(third, 0.35 * antenna, -0.35 * antenna);

            return new List<double[]>
            {
                first,
                second,
                third,
                CopyTarget(baseTarget),
            };
        }

        private static double SpeakingIntensity(
            ReachyBaselineBehaviorRequest request,
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            switch (request.SpeakingDrive)
            {
                case ReachyBaselineSpeakingDrive.Timing:
                    return baselinePolicy.SpeakingTimingIntensity;
                case ReachyBaselineSpeakingDrive.AudioEnergy:
                    return request.NormalizedSpeechEnergy ??
                        throw new InvalidOperationException(
                            "Audio-energy speaking request omitted its energy value.");
                default:
                    throw new InvalidOperationException(
                        "Speaking baseline request omitted its explicit drive mode.");
            }
        }

        private static List<double[]> CreateWakePoses(
            double[] baseTarget,
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            double[] first = CopyTarget(baseTarget);
            ApplyHeadOffset(
                first,
                yawRadians: 0.0,
                pitchRadians: 0.50 * baselinePolicy.WakeHeadPitchRadians,
                rollRadians: 0.0);
            AddAntennaOffset(
                first,
                0.50 * baselinePolicy.WakeAntennaRadians,
                -0.50 * baselinePolicy.WakeAntennaRadians);

            double[] attentive = CopyTarget(baseTarget);
            ApplyHeadOffset(
                attentive,
                yawRadians: 0.0,
                pitchRadians: baselinePolicy.WakeHeadPitchRadians,
                rollRadians: 0.0);
            AddAntennaOffset(
                attentive,
                baselinePolicy.WakeAntennaRadians,
                -baselinePolicy.WakeAntennaRadians);

            return new List<double[]> { first, attentive };
        }

        private static bool IsNeutralWakeSource(
            ReachyBehaviorMotionSnapshot motionSnapshot,
            ReachyBaselineBehaviorPolicy baselinePolicy)
        {
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                if (Math.Abs(motionSnapshot.PositionsRadians[index]) >
                        baselinePolicy.WakeNeutralPositionToleranceRadians ||
                    Math.Abs(motionSnapshot.VelocitiesRadiansPerSecond[index]) >
                        baselinePolicy.WakeNeutralVelocityToleranceRadiansPerSecond)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

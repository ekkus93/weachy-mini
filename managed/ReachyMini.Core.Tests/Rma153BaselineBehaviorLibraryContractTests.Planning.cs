#nullable enable

using System;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma153BaselineBehaviorLibraryContractTests
    {
        private static void CatalogRequestsAreExplicitAndBounded()
        {
            ReachyBaselineBehaviorRequest[] catalog =
            {
                ReachyBaselineBehaviorRequest.NeutralIdle(),
                ReachyBaselineBehaviorRequest.Listening(),
                ReachyBaselineBehaviorRequest.SpeakingFromTiming(),
                ReachyBaselineBehaviorRequest.SpeakingFromAudioEnergy(0.5),
                ReachyBaselineBehaviorRequest.Acknowledgment(),
                ReachyBaselineBehaviorRequest.Curiosity(),
                ReachyBaselineBehaviorRequest.Surprise(),
                ReachyBaselineBehaviorRequest.GazeAcquisition("entity-10"),
                ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-10",
                    SnapshotTimestamp,
                    Coverage(VisionCoverageState.Normal, 0.9, stopTurning: false)),
                ReachyBaselineBehaviorRequest.UnavailableError(),
                ReachyBaselineBehaviorRequest.SleepRest(),
                ReachyBaselineBehaviorRequest.Wake(),
            };
            Equal(12, catalog.Length, "baseline request factory count");
            Equal<ReachyBaselineSpeakingDrive?>(
                ReachyBaselineSpeakingDrive.Timing,
                catalog[2].SpeakingDrive,
                "timing speaking drive");
            Equal<ReachyBaselineSpeakingDrive?>(
                ReachyBaselineSpeakingDrive.AudioEnergy,
                catalog[3].SpeakingDrive,
                "audio-energy speaking drive");
            Equal<double?>(0.5, catalog[3].NormalizedSpeechEnergy, "speech energy");

            ExpectArgumentOutOfRange(
                () => ReachyBaselineBehaviorRequest.SpeakingFromAudioEnergy(-0.01),
                "negative speech energy");
            ExpectArgumentOutOfRange(
                () => ReachyBaselineBehaviorRequest.SpeakingFromAudioEnergy(1.01),
                "speech energy above one");
            ExpectArgument(
                () => ReachyBaselineBehaviorRequest.GazeAcquisition("person-1"),
                "noncanonical gaze entity");
            ExpectArgumentOutOfRange(
                () => ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-10",
                    coverageTimestampNanoseconds: 0L,
                    Coverage(VisionCoverageState.Normal, 0.9, stopTurning: false)),
                "nonpositive gaze-search coverage timestamp");
        }

        private static void IdleListeningAndSpeakingAreDeterministicAndBounded()
        {
            ReachyBaselineBehaviorLibrary library = CreateLibrary();
            ReachyBehaviorPlannerPolicy plannerPolicy =
                ReachyBehaviorPlannerPolicy.CreateMobileDefault();

            ReachyBaselineBehaviorPlanResult idleA = Plan(
                library,
                ReachyBaselineBehaviorRequest.NeutralIdle());
            ReachyBaselineBehaviorPlanResult idleB = Plan(
                library,
                ReachyBaselineBehaviorRequest.NeutralIdle());
            RequireSucceeded(idleA, "idle A");
            RequireSucceeded(idleB, "idle B");
            AssertPlansEqual(
                RequirePlan(idleA, "idle A"),
                RequirePlan(idleB, "idle B"),
                "deterministic idle");
            AssertScheduledPlanWithinPolicy(
                RequirePlan(idleA, "idle A"),
                NeutralMotion().PositionsRadians,
                plannerPolicy);
            AssertReturnsToSource(
                RequirePlan(idleA, "idle A"),
                NeutralMotion().PositionsRadians,
                "idle returns to source");

            ReachyBaselineBehaviorPlanResult listening = Plan(
                library,
                ReachyBaselineBehaviorRequest.Listening());
            RequireSucceeded(listening, "listening");
            Equal(
                true,
                RequirePlan(listening, "listening").Frames.Count > 0,
                "listening posture has motion");
            AssertScheduledPlanWithinPolicy(
                RequirePlan(listening, "listening"),
                NeutralMotion().PositionsRadians,
                plannerPolicy);

            ReachyBaselineBehaviorPlanResult speaking = Plan(
                library,
                ReachyBaselineBehaviorRequest.SpeakingFromTiming());
            RequireSucceeded(speaking, "timing speaking");
            AssertScheduledPlanWithinPolicy(
                RequirePlan(speaking, "timing speaking"),
                NeutralMotion().PositionsRadians,
                plannerPolicy);
            AssertReturnsToSource(
                RequirePlan(speaking, "timing speaking"),
                NeutralMotion().PositionsRadians,
                "timing speaking returns to source");
        }

        private static void SpeakingEnergyControlsMotionWithoutAccumulation()
        {
            ReachyBaselineBehaviorLibrary library = CreateLibrary();
            ReachyBaselineBehaviorPlanResult silent = Plan(
                library,
                ReachyBaselineBehaviorRequest.SpeakingFromAudioEnergy(0.0));
            RequireSucceeded(silent, "silent speech energy");
            Equal(
                0,
                RequirePlan(silent, "silent speech energy").Frames.Count,
                "zero speech energy creates no expressive motion");

            ReachyBaselineBehaviorPlanResult low = Plan(
                library,
                ReachyBaselineBehaviorRequest.SpeakingFromAudioEnergy(0.25));
            ReachyBaselineBehaviorPlanResult high = Plan(
                library,
                ReachyBaselineBehaviorRequest.SpeakingFromAudioEnergy(1.0));
            RequireSucceeded(low, "low speech energy");
            RequireSucceeded(high, "high speech energy");
            double lowPeak = PeakAntennaMagnitude(RequirePlan(low, "low speech energy"));
            double highPeak = PeakAntennaMagnitude(RequirePlan(high, "high speech energy"));
            Equal(true, highPeak > lowPeak, "speech energy scales motion amplitude");
            AssertReturnsToSource(
                RequirePlan(high, "high speech energy"),
                NeutralMotion().PositionsRadians,
                "audio speaking cycle returns to source");
        }

        private static void NodCuriosityAndSurpriseUsePlannerBoundedGestures()
        {
            ReachyBaselineBehaviorRequest[] requests =
            {
                ReachyBaselineBehaviorRequest.Acknowledgment(),
                ReachyBaselineBehaviorRequest.Curiosity(),
                ReachyBaselineBehaviorRequest.Surprise(),
            };
            ReachyBehaviorPlannerPolicy policy =
                ReachyBehaviorPlannerPolicy.CreateMobileDefault();
            ReachyBaselineBehaviorLibrary library = CreateLibrary();
            for (int index = 0; index < requests.Length; ++index)
            {
                ReachyBaselineBehaviorPlanResult result = Plan(library, requests[index]);
                RequireSucceeded(result, "bounded baseline gesture " + index);
                AssertScheduledPlanWithinPolicy(
                    RequirePlan(result, "bounded baseline gesture " + index),
                    NeutralMotion().PositionsRadians,
                    policy);
            }
        }

        private static void UnavailableErrorUsesBoundedConcernedExpression()
        {
            ReachyBaselineBehaviorPlanResult result = Plan(
                CreateLibrary(),
                ReachyBaselineBehaviorRequest.UnavailableError());
            RequireSucceeded(result, "unavailable/error expression");
            ReachyBehaviorTrajectoryPlan plan = RequirePlan(
                result,
                "unavailable/error expression");
            Equal(true, plan.Frames.Count > 0, "error expression has visible motion");
            AssertScheduledPlanWithinPolicy(
                plan,
                NeutralMotion().PositionsRadians,
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
        }
    }
}

#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.Behavior
{
    public enum ReachyBaselineBehaviorKind
    {
        NeutralIdle = 0,
        Listening = 1,
        Speaking = 2,
        Acknowledgment = 3,
        Curiosity = 4,
        Surprise = 5,
        GazeAcquisition = 6,
        GazeSearch = 7,
        UnavailableError = 8,
        SleepRest = 9,
        Wake = 10,
    }

    public enum ReachyBaselineSpeakingDrive
    {
        Timing = 0,
        AudioEnergy = 1,
    }

    public enum ReachyBaselineLifecycleAction
    {
        None = 0,
        EnterSleepRest = 1,
        WakeNeutral = 2,
    }

    public sealed class ReachyBaselineBehaviorRequest
    {
        private ReachyBaselineBehaviorRequest(
            ReachyBaselineBehaviorKind kind,
            ReachyBaselineSpeakingDrive? speakingDrive,
            double? normalizedSpeechEnergy,
            string? gazeEntityId,
            long? searchCoverageTimestampNanoseconds,
            WorldCoverageContext? searchCoverage,
            ReachyBaselineLifecycleAction prePlanningLifecycleAction,
            ReachyBaselineLifecycleAction postExecutionLifecycleAction)
        {
            Kind = kind;
            SpeakingDrive = speakingDrive;
            NormalizedSpeechEnergy = normalizedSpeechEnergy;
            GazeEntityId = gazeEntityId;
            SearchCoverageTimestampNanoseconds = searchCoverageTimestampNanoseconds;
            SearchCoverage = searchCoverage;
            PrePlanningLifecycleAction = prePlanningLifecycleAction;
            RequiredPostExecutionLifecycleAction = postExecutionLifecycleAction;
        }

        public ReachyBaselineBehaviorKind Kind { get; }

        public ReachyBaselineSpeakingDrive? SpeakingDrive { get; }

        public double? NormalizedSpeechEnergy { get; }

        public string? GazeEntityId { get; }

        public long? SearchCoverageTimestampNanoseconds { get; }

        public WorldCoverageContext? SearchCoverage { get; }

        public ReachyBaselineLifecycleAction PrePlanningLifecycleAction { get; }

        internal ReachyBaselineLifecycleAction RequiredPostExecutionLifecycleAction { get; }

        public static ReachyBaselineBehaviorRequest NeutralIdle()
        {
            return Create(ReachyBaselineBehaviorKind.NeutralIdle);
        }

        public static ReachyBaselineBehaviorRequest Listening()
        {
            return Create(ReachyBaselineBehaviorKind.Listening);
        }

        public static ReachyBaselineBehaviorRequest SpeakingFromTiming()
        {
            return new ReachyBaselineBehaviorRequest(
                ReachyBaselineBehaviorKind.Speaking,
                speakingDrive: ReachyBaselineSpeakingDrive.Timing,
                normalizedSpeechEnergy: null,
                gazeEntityId: null,
                searchCoverageTimestampNanoseconds: null,
                searchCoverage: null,
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.None,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.None);
        }

        public static ReachyBaselineBehaviorRequest SpeakingFromAudioEnergy(
            double normalizedSpeechEnergy)
        {
            if (!IsFinite(normalizedSpeechEnergy) ||
                normalizedSpeechEnergy < 0.0 ||
                normalizedSpeechEnergy > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(normalizedSpeechEnergy));
            }
            return new ReachyBaselineBehaviorRequest(
                ReachyBaselineBehaviorKind.Speaking,
                speakingDrive: ReachyBaselineSpeakingDrive.AudioEnergy,
                normalizedSpeechEnergy: normalizedSpeechEnergy,
                gazeEntityId: null,
                searchCoverageTimestampNanoseconds: null,
                searchCoverage: null,
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.None,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.None);
        }

        public static ReachyBaselineBehaviorRequest Acknowledgment()
        {
            return Create(ReachyBaselineBehaviorKind.Acknowledgment);
        }

        public static ReachyBaselineBehaviorRequest Curiosity()
        {
            return Create(ReachyBaselineBehaviorKind.Curiosity);
        }

        public static ReachyBaselineBehaviorRequest Surprise()
        {
            return Create(ReachyBaselineBehaviorKind.Surprise);
        }

        public static ReachyBaselineBehaviorRequest GazeAcquisition(
            string entityId)
        {
            return CreateGaze(
                ReachyBaselineBehaviorKind.GazeAcquisition,
                entityId);
        }

        public static ReachyBaselineBehaviorRequest GazeSearch(
            string entityId,
            long coverageTimestampNanoseconds,
            WorldCoverageContext currentCoverage)
        {
            if (coverageTimestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coverageTimestampNanoseconds));
            }
            return new ReachyBaselineBehaviorRequest(
                ReachyBaselineBehaviorKind.GazeSearch,
                speakingDrive: null,
                normalizedSpeechEnergy: null,
                gazeEntityId: RequireEntityId(entityId),
                searchCoverageTimestampNanoseconds: coverageTimestampNanoseconds,
                searchCoverage: currentCoverage ??
                    throw new ArgumentNullException(nameof(currentCoverage)),
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.None,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.None);
        }

        public static ReachyBaselineBehaviorRequest UnavailableError()
        {
            return Create(ReachyBaselineBehaviorKind.UnavailableError);
        }

        public static ReachyBaselineBehaviorRequest SleepRest()
        {
            return new ReachyBaselineBehaviorRequest(
                ReachyBaselineBehaviorKind.SleepRest,
                speakingDrive: null,
                normalizedSpeechEnergy: null,
                gazeEntityId: null,
                searchCoverageTimestampNanoseconds: null,
                searchCoverage: null,
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.None,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.EnterSleepRest);
        }

        public static ReachyBaselineBehaviorRequest Wake()
        {
            return new ReachyBaselineBehaviorRequest(
                ReachyBaselineBehaviorKind.Wake,
                speakingDrive: null,
                normalizedSpeechEnergy: null,
                gazeEntityId: null,
                searchCoverageTimestampNanoseconds: null,
                searchCoverage: null,
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.WakeNeutral,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.None);
        }

        private static ReachyBaselineBehaviorRequest Create(
            ReachyBaselineBehaviorKind kind)
        {
            return new ReachyBaselineBehaviorRequest(
                kind,
                speakingDrive: null,
                normalizedSpeechEnergy: null,
                gazeEntityId: null,
                searchCoverageTimestampNanoseconds: null,
                searchCoverage: null,
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.None,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.None);
        }

        private static ReachyBaselineBehaviorRequest CreateGaze(
            ReachyBaselineBehaviorKind kind,
            string entityId)
        {
            return new ReachyBaselineBehaviorRequest(
                kind,
                speakingDrive: null,
                normalizedSpeechEnergy: null,
                gazeEntityId: RequireEntityId(entityId),
                searchCoverageTimestampNanoseconds: null,
                searchCoverage: null,
                prePlanningLifecycleAction: ReachyBaselineLifecycleAction.None,
                postExecutionLifecycleAction: ReachyBaselineLifecycleAction.None);
        }

        private static string RequireEntityId(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) ||
                entityId.Length > ReachyBehaviorIntentPolicy.MaximumEntityIdCharacters ||
                !entityId.StartsWith("entity-", StringComparison.Ordinal) ||
                entityId.Length == "entity-".Length)
            {
                throw new ArgumentException(
                    "Baseline gaze requires a bounded canonical entity ID.",
                    nameof(entityId));
            }
            for (int index = "entity-".Length; index < entityId.Length; ++index)
            {
                char current = entityId[index];
                if (current < '0' || current > '9')
                {
                    throw new ArgumentException(
                        "Baseline gaze entity IDs must match entity-[0-9]+.",
                        nameof(entityId));
                }
            }
            return entityId;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class ReachyBaselineBehaviorPlanResult
    {
        internal ReachyBaselineBehaviorPlanResult(
            ReachyBaselineBehaviorRequest request,
            ReachyBehaviorPlanResult plannerResult)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            PlannerResult = plannerResult ??
                throw new ArgumentNullException(nameof(plannerResult));
        }

        public ReachyBaselineBehaviorRequest Request { get; }

        public ReachyBehaviorPlanResult PlannerResult { get; }

        public ReachyBaselineLifecycleAction PrePlanningLifecycleAction =>
            Request.PrePlanningLifecycleAction;

        public bool Succeeded => PlannerResult.Succeeded;

        public ReachyBaselineLifecycleAction ResolvePostExecutionLifecycleAction(
            ReachyBehaviorTrajectoryExecutionResult executionResult)
        {
            if (executionResult == null)
            {
                throw new ArgumentNullException(nameof(executionResult));
            }
            ReachyBehaviorTrajectoryPlan? plan = PlannerResult.Plan;
            if (!Succeeded ||
                plan == null ||
                !executionResult.Completed ||
                executionResult.SubmittedFrameCount != plan.Frames.Count)
            {
                return ReachyBaselineLifecycleAction.None;
            }
            return Request.RequiredPostExecutionLifecycleAction;
        }
    }
}

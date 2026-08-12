#nullable enable

using System;
using System.Threading;
using ReachyMini.Behavior;
using ReachyMini.Interop;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma153BaselineBehaviorLibraryContractTests
    {
        private static void GazeAcquisitionAndLostSearchAreFailClosed()
        {
            ReachyBaselineBehaviorLibrary library = CreateLibrary();
            WorldModelSnapshot visible = CreateWorldSnapshot(
                "entity-20",
                visible: true,
                confidence: 0.9,
                VisionCoverageState.Normal,
                validFraction: 0.95,
                stopTurning: false,
                centerX: 0.65,
                centerY: 0.45);
            ReachyBaselineBehaviorPlanResult acquisition = Plan(
                library,
                ReachyBaselineBehaviorRequest.GazeAcquisition("entity-20"),
                visible);
            RequireSucceeded(acquisition, "gaze acquisition");
            Equal(
                "entity-20",
                RequirePlan(acquisition, "gaze acquisition").ResolvedGazeEntityId,
                "gaze acquisition exact entity");

            WorldModelSnapshot lost = CreateWorldSnapshot(
                "entity-20",
                visible: false,
                confidence: 0.8,
                VisionCoverageState.Degraded,
                validFraction: 0.75,
                stopTurning: false,
                centerX: 0.70,
                centerY: 0.50);
            ReachyBaselineBehaviorPlanResult searchA = Plan(
                library,
                ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-20",
                    SnapshotTimestamp,
                    Coverage(VisionCoverageState.Degraded, 0.75, stopTurning: false)),
                lost);
            ReachyBaselineBehaviorPlanResult searchB = Plan(
                library,
                ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-20",
                    SnapshotTimestamp,
                    Coverage(VisionCoverageState.Degraded, 0.75, stopTurning: false)),
                lost);
            RequireSucceeded(searchA, "lost-target search A");
            RequireSucceeded(searchB, "lost-target search B");
            AssertPlansEqual(
                RequirePlan(searchA, "lost-target search A"),
                RequirePlan(searchB, "lost-target search B"),
                "deterministic lost-target search");
            AssertReturnsToSource(
                RequirePlan(searchA, "lost-target search A"),
                NeutralMotion().PositionsRadians,
                "lost-target search returns to source");

            ReachyBaselineBehaviorPlanResult staleCoverageSearch = Plan(
                library,
                ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-20",
                    PlanningTimestamp - 1_100_000_000L,
                    Coverage(VisionCoverageState.Normal, 0.95, stopTurning: false)),
                lost);
            Equal(
                ReachyBehaviorPlannerStatus.GazeCoverageBlocked,
                staleCoverageSearch.PlannerResult.Status,
                "search rejects stale current coverage");

            ReachyBaselineBehaviorPlanResult visibleSearch = Plan(
                library,
                ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-20",
                    SnapshotTimestamp,
                    Coverage(VisionCoverageState.Normal, 0.95, stopTurning: false)),
                visible);
            Equal(
                ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                visibleSearch.PlannerResult.Status,
                "search rejects currently visible target");

            ReachyBaselineBehaviorPlanResult blockedSearch = Plan(
                library,
                ReachyBaselineBehaviorRequest.GazeSearch(
                    "entity-20",
                    SnapshotTimestamp,
                    Coverage(VisionCoverageState.Unavailable, 0.20, stopTurning: true)),
                lost);
            Equal(
                ReachyBehaviorPlannerStatus.GazeCoverageBlocked,
                blockedSearch.PlannerResult.Status,
                "search obeys transformed-image coverage stop");
        }

        private static void SleepAndWakeExposeExplicitLifecycleSequence()
        {
            ReachyBaselineBehaviorRequest sleepRequest =
                ReachyBaselineBehaviorRequest.SleepRest();
            Equal(
                ReachyBaselineLifecycleAction.None,
                sleepRequest.PrePlanningLifecycleAction,
                "sleep has no pre-planning reset");
            Equal(
                ReachyBaselineLifecycleAction.EnterSleepRest,
                sleepRequest.RequiredPostExecutionLifecycleAction,
                "sleep declares simulator rest after successful execution");
            Equal(
                ReachySimResetPose.SleepRest,
                ReachyBaselineLifecycleResetMapping.RequireResetPose(
                    sleepRequest.RequiredPostExecutionLifecycleAction),
                "sleep lifecycle action maps to native sleep-rest reset");
            ReachyBaselineBehaviorPlanResult sleep = Plan(
                CreateLibrary(),
                sleepRequest,
                worldSnapshot: null,
                MotionAt(0.20));
            RequireSucceeded(sleep, "sleep safe-rest plan");
            Equal(
                ReachyBehaviorTrajectoryPurpose.SafeRest,
                RequirePlan(sleep, "sleep safe-rest plan").Purpose,
                "sleep uses safe-rest planner path");
            var completed = new ReachyBehaviorTrajectoryExecutionResult(
                ReachyBehaviorTrajectoryExecutionStatus.Completed,
                "test-completed",
                RequirePlan(sleep, "sleep safe-rest plan").Frames.Count,
                submissionStatus: null);
            Equal(
                ReachyBaselineLifecycleAction.EnterSleepRest,
                sleep.ResolvePostExecutionLifecycleAction(completed),
                "completed safe-rest execution releases sleep reset");
            var wrongCompleted = new ReachyBehaviorTrajectoryExecutionResult(
                ReachyBehaviorTrajectoryExecutionStatus.Completed,
                "test-wrong-completed",
                RequirePlan(sleep, "sleep safe-rest plan").Frames.Count + 1,
                submissionStatus: null);
            Equal(
                ReachyBaselineLifecycleAction.None,
                sleep.ResolvePostExecutionLifecycleAction(wrongCompleted),
                "different completed trajectory cannot release sleep reset");
            var rejected = new ReachyBehaviorTrajectoryExecutionResult(
                ReachyBehaviorTrajectoryExecutionStatus.SubmissionRejected,
                "test-rejected",
                submittedFrameCount: 0,
                submissionStatus: ReachyBehaviorTargetSubmissionStatus.QueueFull);
            Equal(
                ReachyBaselineLifecycleAction.None,
                sleep.ResolvePostExecutionLifecycleAction(rejected),
                "rejected safe-rest execution cannot release sleep reset");

            ReachyBaselineBehaviorRequest wakeRequest =
                ReachyBaselineBehaviorRequest.Wake();
            Equal(
                ReachyBaselineLifecycleAction.WakeNeutral,
                wakeRequest.PrePlanningLifecycleAction,
                "wake requires neutral-awake reset before planning");
            Equal(
                ReachySimResetPose.NeutralAwake,
                ReachyBaselineLifecycleResetMapping.RequireResetPose(
                    wakeRequest.PrePlanningLifecycleAction),
                "wake lifecycle action maps to native neutral-awake reset");
            ReachyBaselineBehaviorPlanResult wake = Plan(
                CreateLibrary(),
                wakeRequest);
            RequireSucceeded(wake, "wake from fresh neutral state");
            Equal(
                ReachyBehaviorTrajectoryPurpose.Intent,
                RequirePlan(wake, "wake from fresh neutral state").Purpose,
                "wake expressive phase uses normal intent trajectory path");

            ReachyBaselineBehaviorPlanResult staleWake = Plan(
                CreateLibrary(),
                wakeRequest,
                worldSnapshot: null,
                MotionAt(0.10));
            Equal(
                ReachyBehaviorPlannerStatus.MotionStateInvalid,
                staleWake.PlannerResult.Status,
                "wake rejects non-neutral source without explicit reset");
        }

        private static void SafetyInterlocksAndCancellationProduceNoHiddenMotion()
        {
            ReachyBaselineBehaviorLibrary library = CreateLibrary();
            var blocked = new ReachyBehaviorSafetySnapshot(
                motionPathAvailable: true,
                workspaceClear: false,
                activeFault: false,
                activeCollision: false,
                activeHardStop: false,
                loadLimitActive: false);
            ReachyBaselineBehaviorPlanResult interlocked = library.Plan(
                ReachyBaselineBehaviorRequest.Surprise(),
                worldSnapshot: null,
                NeutralMotion(),
                blocked,
                PlanningTimestamp);
            Equal(
                ReachyBehaviorPlannerStatus.SafetyInterlockActive,
                interlocked.PlannerResult.Status,
                "expressive behavior obeys planner safety interlock");
            Equal(null, interlocked.PlannerResult.Plan, "interlocked plan is absent");

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ReachyBaselineBehaviorPlanResult cancelled = library.Plan(
                ReachyBaselineBehaviorRequest.SleepRest(),
                worldSnapshot: null,
                NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp,
                cancellation.Token);
            Equal(
                ReachyBehaviorPlannerStatus.Cancelled,
                cancelled.PlannerResult.Status,
                "cancelled sleep does not fabricate rest motion");
            var notExecuted = new ReachyBehaviorTrajectoryExecutionResult(
                ReachyBehaviorTrajectoryExecutionStatus.Cancelled,
                "test-cancelled",
                submittedFrameCount: 0,
                submissionStatus: null);
            Equal(
                ReachyBaselineLifecycleAction.None,
                cancelled.ResolvePostExecutionLifecycleAction(notExecuted),
                "failed plan cannot authorize post-execution sleep action");
            Equal(null, cancelled.PlannerResult.Plan, "cancelled sleep plan is absent");
        }
    }
}

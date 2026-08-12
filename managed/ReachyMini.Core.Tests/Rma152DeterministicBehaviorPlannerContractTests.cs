#nullable enable

using System.Runtime.CompilerServices;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma152DeterministicBehaviorPlannerContractTests
    {
        private const long SnapshotTimestamp = 10_000_000_000L;
        private const long PlanningTimestamp = 10_100_000_000L;

        [ModuleInitializer]
        internal static void Run()
        {
            CurrentHighConfidenceGazeTargetIsResolved();
            UnsafeGazeTargetsFailClosed();
            GestureTrajectoryIsDeterministicAndBounded();
            TrajectoryFramesSlewInsteadOfDelayedTargetStep();
            RecoilPreservesUnrelatedBodyYaw();
            SafeRestCoversFullSoftEnvelope();
            MotionPlanningIsRelativeToAuthoritativeState();
            SafetyInterlocksBlockMotionWithoutBlockingSpeechOnlyIntent();
            AuthoritativeStateMapsIntoPlannerSafetyAndMotion();
            TooShortTimingCannotOverrideMotionLimits();
            CancellationRequiresExplicitFreshSafeRestPlan();
            ExecutionCancellationAndSubmissionFailureStopWithoutRetry();
            SafeRestReturnsAllActuatorsToNeutral();
        }
    }
}

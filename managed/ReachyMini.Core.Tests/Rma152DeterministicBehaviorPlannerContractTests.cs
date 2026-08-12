#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ReachyMini.Behavior;
using ReachyMini.Perception;
using ReachyMini.Interop;

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

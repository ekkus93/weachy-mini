#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma152DeterministicBehaviorPlannerContractTests
    {
        private static void AuthoritativeStateMapsIntoPlannerSafetyAndMotion()
        {
            var layout = new ReachySimAuthoritativeStateLayout(
                byteCount: 1,
                modelHash: 1UL,
                qposCount: 0,
                qvelCount: 0,
                actuatorObservationCount: ReachyBehaviorPlannerActuators.Count,
                bodyPoseCount: 0);
            var state = new ReachySimAuthoritativeStateFrame(layout)
            {
                ContactCount = 1U,
                HealthFlags = (1U << 2) | (1U << 3),
            };
            for (int index = 0;
                index < ReachyBehaviorPlannerActuators.Count;
                ++index)
            {
                state.SetActuatorObservation(
                    index,
                    new ReachySimActuatorObservationSnapshot(
                        checked((uint)index),
                        controlValue: 0.0,
                        actuatorForce: 0.0,
                        length: index * 0.01,
                        velocity: index * 0.001));
            }

            ReachyBehaviorMotionSnapshot motion =
                ReachyBehaviorAuthoritativeSafety.CreateMotionSnapshot(state);
            Equal(0.08, motion.PositionsRadians[8], "authoritative actuator position");
            Equal(0.008, motion.VelocitiesRadiansPerSecond[8], "authoritative velocity");

            ReachyBehaviorSafetySnapshot safety =
                ReachyBehaviorAuthoritativeSafety.CreateSafetySnapshot(
                    state,
                    normalControllerAvailable: true,
                    workspaceClear: true);
            Equal(true, safety.ActiveCollision, "authoritative contact interlock");
            Equal(true, safety.ActiveHardStop, "authoritative hard-stop interlock");
            Equal(true, safety.LoadLimitActive, "authoritative overload interlock");
            Equal(false, safety.AllowsMotion, "authoritative safety blocks motion");
        }

        private static void TooShortTimingCannotOverrideMotionLimits()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Surprised,
                gesture: ReachyBehaviorGesture.Recoil,
                urgency: ReachyBehaviorUrgency.High,
                timing: new ReachyBehaviorTimingConstraints(
                    startDelayMilliseconds: 0,
                    maximumDurationMilliseconds: 100));

            ReachyBehaviorPlanResult result = planner.Plan(
                intent,
                worldSnapshot: null,
                NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp);

            Equal(
                ReachyBehaviorPlannerStatus.TrajectoryConstraintRejected,
                result.Status,
                "unsafe timing compression rejection");
            Equal(null, result.Plan, "unsafe timing produces no plan");
        }

        private static void CancellationRequiresExplicitFreshSafeRestPlan()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Attentive,
                gesture: ReachyBehaviorGesture.Nod,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);
            ReachyBehaviorPlanResult cancelled;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                cancelled = planner.Plan(
                    intent,
                    worldSnapshot: null,
                    NeutralMotion(),
                    SafeMotion(),
                    PlanningTimestamp,
                    cancellation.Token);
            }
            Equal(
                ReachyBehaviorPlannerStatus.Cancelled,
                cancelled.Status,
                "cancelled plan status");
            Equal(null, cancelled.Plan, "cancelled plan is not fabricated");
            Equal(
                "cancelled-safe-rest-replan-required",
                cancelled.DiagnosticCode,
                "fresh safe-rest requirement diagnostic");
        }

        private static void ExecutionCancellationAndSubmissionFailureStopWithoutRetry()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            ReachyBehaviorIntent intent = new ReachyBehaviorIntent(
                speech: null,
                gazeTarget: null,
                expression: ReachyBehaviorExpression.Curious,
                gesture: ReachyBehaviorGesture.Nod,
                urgency: ReachyBehaviorUrgency.Normal,
                timing: null);
            ReachyBehaviorTrajectoryPlan plan = planner.Plan(
                intent,
                worldSnapshot: null,
                NeutralMotion(),
                SafeMotion(),
                PlanningTimestamp).Plan ??
                throw new InvalidOperationException(
                    "Execution fixture could not create a trajectory plan.");

            using (var cancellation = new CancellationTokenSource())
            {
                var sink = new RecordingTargetSink(
                    ReachyBehaviorTargetSubmissionStatus.Accepted);
                var executor = new ReachyBehaviorTrajectoryExecutor(
                    sink,
                    new CancelAfterFirstFrameDelay(cancellation));
                ReachyBehaviorTrajectoryExecutionResult result =
                    executor.ExecuteAsync(plan, cancellation.Token)
                        .GetAwaiter()
                        .GetResult();
                Equal(
                    ReachyBehaviorTrajectoryExecutionStatus.Cancelled,
                    result.Status,
                    "execution cancellation status");
                Equal(1, sink.SubmissionCount, "cancelled execution submission count");
                Equal(1, result.SubmittedFrameCount, "cancelled execution result count");
            }

            var rejectingSink = new RecordingTargetSink(
                ReachyBehaviorTargetSubmissionStatus.QueueFull);
            var rejectingExecutor = new ReachyBehaviorTrajectoryExecutor(
                rejectingSink,
                new ImmediateTrajectoryDelay());
            ReachyBehaviorTrajectoryExecutionResult rejected =
                rejectingExecutor.ExecuteAsync(plan).GetAwaiter().GetResult();
            Equal(
                ReachyBehaviorTrajectoryExecutionStatus.SubmissionRejected,
                rejected.Status,
                "controller rejection execution status");
            Equal(1, rejectingSink.SubmissionCount, "controller rejection is not retried");
            Equal(0, rejected.SubmittedFrameCount, "rejected frame not counted accepted");
        }

        private static void SafeRestReturnsAllActuatorsToNeutral()
        {
            ReachyDeterministicBehaviorPlanner planner = CreatePlanner();
            var positions = new[]
            {
                0.20,
                0.10,
                -0.10,
                0.08,
                -0.08,
                0.07,
                -0.07,
                0.15,
                -0.15,
            };
            var velocities = new double[ReachyBehaviorPlannerActuators.Count];
            var motion = new ReachyBehaviorMotionSnapshot(positions, velocities);

            ReachyBehaviorPlanResult result = planner.PlanSafeRest(
                motion,
                SafeMotion());
            Equal(true, result.Succeeded, "safe-rest plan success");
            ReachyBehaviorTrajectoryPlan plan = result.Plan ??
                throw new InvalidOperationException("Safe-rest plan was null.");
            Equal(
                ReachyBehaviorTrajectoryPurpose.SafeRest,
                plan.Purpose,
                "safe-rest purpose");
            ReachyBehaviorTrajectoryFrame final =
                plan.Frames[plan.Frames.Count - 1];
            for (int actuator = 0;
                actuator < ReachyBehaviorPlannerActuators.Count;
                ++actuator)
            {
                Equal(
                    0.0,
                    final.TargetPositionsRadians[actuator],
                    "safe-rest neutral actuator " + actuator);
            }
        }

        private sealed class RecordingTargetSink :
            IReachyBehaviorControllerTargetSink
        {
            private readonly ReachyBehaviorTargetSubmissionStatus status;

            internal RecordingTargetSink(
                ReachyBehaviorTargetSubmissionStatus status)
            {
                this.status = status;
            }

            internal int SubmissionCount { get; private set; }

            public ReachyBehaviorTargetSubmissionStatus Submit(
                ReachyBehaviorTrajectoryFrame frame)
            {
                if (frame == null)
                {
                    throw new ArgumentNullException(nameof(frame));
                }
                ++SubmissionCount;
                return status;
            }
        }

        private sealed class ImmediateTrajectoryDelay :
            IReachyBehaviorTrajectoryDelay
        {
            public Task DelayAsync(
                int milliseconds,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }

        private sealed class CancelAfterFirstFrameDelay :
            IReachyBehaviorTrajectoryDelay
        {
            private readonly CancellationTokenSource cancellation;
            private int delayCount;

            internal CancelAfterFirstFrameDelay(
                CancellationTokenSource cancellation)
            {
                this.cancellation = cancellation ??
                    throw new ArgumentNullException(nameof(cancellation));
            }

            public Task DelayAsync(
                int milliseconds,
                CancellationToken cancellationToken)
            {
                ++delayCount;
                if (delayCount == 2)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled(cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma154VisualServoGazeLoopContractTests
    {
        private const string EntityId = "entity-154";
        private const long BaseTimestamp = 30_000_000_000L;

        private static ReachyVisualServoGazeLoop CreateLoop(
            IReachyVisualServoFeedbackSource feedback,
            RecordingTargetSink sink,
            ReachyVisualServoPolicy? policy = null)
        {
            var planner = new ReachyDeterministicBehaviorPlanner(
                ReachyBehaviorPlannerPolicy.CreateMobileDefault());
            var executor = new ReachyBehaviorTrajectoryExecutor(
                sink,
                new ImmediateTrajectoryDelay());
            return new ReachyVisualServoGazeLoop(
                planner,
                executor,
                feedback,
                new ImmediatePollDelay(),
                policy ?? ReachyVisualServoPolicy.CreateMobileDefault());
        }

        private static ReachyVisualServoFeedbackSample Sample(
            ulong sourceSequence,
            ulong authoritativeSequence,
            double centerX,
            double centerY,
            double bodyYaw,
            bool visible = true,
            VisionCoverageState coverageState = VisionCoverageState.Normal,
            double validFraction = 1.0,
            bool stopTurning = false,
            bool loadLimit = false,
            bool activeFault = false,
            uint continuityId = 1U,
            ulong sourceSessionId = 7UL,
            ulong? frameAuthoritativeSequence = null)
        {
            long timestamp = checked(
                BaseTimestamp + checked((long)sourceSequence * 10_000_000L));
            var positions = new double[ReachyBehaviorPlannerActuators.Count];
            positions[ReachyBehaviorPlannerActuators.BodyYaw] = bodyYaw;
            var motion = new ReachyBehaviorMotionSnapshot(
                positions,
                new double[ReachyBehaviorPlannerActuators.Count]);
            var safety = new ReachyBehaviorSafetySnapshot(
                motionPathAvailable: true,
                workspaceClear: true,
                activeFault,
                activeCollision: false,
                activeHardStop: false,
                loadLimitActive: loadLimit);
            return new ReachyVisualServoFeedbackSample(
                timestamp,
                authoritativeSequence,
                World(
                    sourceSequence,
                    frameAuthoritativeSequence ?? authoritativeSequence,
                    timestamp,
                    centerX,
                    centerY,
                    visible,
                    coverageState,
                    validFraction,
                    stopTurning,
                    continuityId,
                    sourceSessionId),
                motion,
                safety);
        }

        private static WorldModelSnapshot World(
            ulong sourceSequence,
            ulong authoritativeSequence,
            long timestamp,
            double centerX,
            double centerY,
            bool visible,
            VisionCoverageState coverageState,
            double validFraction,
            bool stopTurning,
            uint continuityId,
            ulong sourceSessionId)
        {
            double left = Math.Max(0.0, Math.Min(0.90, centerX - 0.05));
            double top = Math.Max(0.0, Math.Min(0.90, centerY - 0.05));
            var bounds = new NormalizedVisionBounds(left, top, 0.10, 0.10);
            var frame = new ReachyVisionFrameIdentity(
                "reachy-eye",
                sourceSessionId,
                sourceSequence,
                timestamp,
                authoritativeSequence,
                continuityId);
            var coverage = new WorldCoverageContext(
                coverageState,
                validFraction,
                stopTurning,
                "rma154-test-coverage");
            var entity = new WorldEntitySnapshot(
                EntityId,
                "track-154",
                "face",
                "tracker-154",
                frame,
                visible
                    ? WorldEntityVisibility.CurrentlyVisible
                    : WorldEntityVisibility.RecentlySeen,
                firstSeenTimestampNanoseconds: BaseTimestamp - 100_000_000L,
                lastSeenTimestampNanoseconds: timestamp,
                confidence: 0.95,
                bounds,
                WorldPositionEstimate.UnknownFromTwoDimensionalTracking(),
                WorldDirectionEstimate.FromBounds(bounds),
                coverage,
                Array.Empty<WorldObservationSnapshot>(),
                Array.Empty<WorldDescriptionSnapshot>(),
                timestamp,
                droppedObservationCount: 0L,
                droppedDescriptionCount: 0L);
            var diagnostics = new WorldModelDiagnosticsSnapshot(
                acceptedTrackingBatchCount: checked((long)sourceSequence),
                duplicateTrackingBatchCount: 0L,
                staleTrackingBatchCount: 0L,
                invalidCoverageBatchCount: 0L,
                capacityRejectedBatchCount: 0L,
                classificationConflictCount: 0L,
                acceptedDescriptionCount: 0L,
                duplicateDescriptionCount: 0L,
                rejectedDescriptionCount: 0L,
                expiredEntityCount: 0L,
                activeScopeCursorCount: 0,
                droppedScopeCursorCount: 0L);
            return new WorldModelSnapshot(
                timestamp,
                new[] { entity },
                diagnostics);
        }

        private static ReachyVisualServoPolicy FastPolicy()
        {
            return new ReachyVisualServoPolicy(
                horizontalToleranceNormalized: 0.06,
                verticalToleranceNormalized: 0.06,
                minimumValidCoverageFraction: 0.50,
                minimumObservedMotionRadians: 1.0e-5,
                feedbackPollDelayMilliseconds: 1,
                maximumIterations: 8,
                maximumLoopDurationMilliseconds: 2_000);
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "RMA-154 contract failed for " + description +
                    ": expected=" + expected + "; actual=" + actual + ".");
            }
        }

        private static void Near(
            double expected,
            double actual,
            double tolerance,
            string description)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    "RMA-154 contract failed for " + description +
                    ": expected=" + expected + "; actual=" + actual + ".");
            }
        }

        private sealed class ScriptedFeedbackSource :
            IReachyVisualServoFeedbackSource
        {
            private readonly Queue<ReachyVisualServoFeedbackSample> samples;
            private readonly CancellationTokenSource? cancelWhenExhausted;

            internal ScriptedFeedbackSource(
                IEnumerable<ReachyVisualServoFeedbackSample> samples,
                CancellationTokenSource? cancelWhenExhausted = null)
            {
                this.samples = new Queue<ReachyVisualServoFeedbackSample>(samples);
                this.cancelWhenExhausted = cancelWhenExhausted;
            }

            internal int CaptureCount { get; private set; }

            public ValueTask<ReachyVisualServoFeedbackSample> CaptureAsync(
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ++CaptureCount;
                if (samples.Count != 0)
                {
                    return new ValueTask<ReachyVisualServoFeedbackSample>(
                        samples.Dequeue());
                }

                cancelWhenExhausted?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "RMA-154 scripted feedback was exhausted unexpectedly.");
            }
        }

        private sealed class RecordingTargetSink :
            IReachyBehaviorControllerTargetSink
        {
            private readonly List<double[]> submitted = new List<double[]>();

            internal List<double[]> Submitted => submitted;

            public ReachyBehaviorTargetSubmissionStatus Submit(
                ReachyBehaviorTrajectoryFrame frame)
            {
                submitted.Add(frame.CopyTargetPositionsRadians());
                return ReachyBehaviorTargetSubmissionStatus.Accepted;
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

        private sealed class ImmediatePollDelay : IReachyVisualServoPollDelay
        {
            public Task DelayAsync(
                int milliseconds,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using ReachyMini.Behavior;
using ReachyMini.Perception;

namespace ReachyMini.Core.Tests
{
    internal static partial class Rma154VisualServoGazeLoopContractTests
    {
        private static void EdgeTargetRecentersOnlyAfterAuthoritativeMotionAndNewFrame()
        {
            var feedback = new ScriptedFeedbackSource(
                new[]
                {
                    Sample(1UL, 10UL, 0.90, 0.50, 0.00),
                    // Actual MuJoCo-side motion is visible, but tracking still comes
                    // from the pre-motion transformed frame. The loop must wait.
                    Sample(
                        1UL,
                        20UL,
                        0.90,
                        0.50,
                        0.05,
                        frameAuthoritativeSequence: 10UL),
                    // A new transformed frame is now tied to that moved state.
                    Sample(2UL, 20UL, 0.70, 0.50, 0.05),
                    // Second bounded adjustment physically moves again and the next
                    // transformed frame places the face inside tolerance.
                    Sample(3UL, 30UL, 0.53, 0.50, 0.10),
                });
            var sink = new RecordingTargetSink();
            ReachyVisualServoGazeLoop loop = CreateLoop(
                feedback,
                sink,
                FastPolicy());

            ReachyVisualServoResult result = loop.CenterAsync(EntityId)
                .GetAwaiter()
                .GetResult();

            Equal(ReachyVisualServoStatus.Centered, result.Status, "edge recenter status");
            Equal(2, result.AdjustmentCount, "edge recenter adjustment count");
            Equal(4, feedback.CaptureCount, "edge recenter feedback consumption");
            Equal(true, result.SubmittedFrameCount > 0, "edge recenter submitted frames");
            for (int frame = 0; frame < sink.Submitted.Count; ++frame)
            {
                Near(
                    0.0,
                    sink.Submitted[frame][ReachyBehaviorPlannerActuators.AntennaLeft],
                    0.0,
                    "visual servo does not accumulate left expressive antenna offset");
                Near(
                    0.0,
                    sink.Submitted[frame][ReachyBehaviorPlannerActuators.AntennaRight],
                    0.0,
                    "visual servo does not accumulate right expressive antenna offset");
            }
            Near(0.03, result.HorizontalErrorNormalized, 1.0e-12, "final horizontal error");
            Near(0.0, result.VerticalErrorNormalized, 1.0e-12, "final vertical error");
        }

        private static void RequestedTargetsDoNotCountAsMotionFeedback()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var feedback = new ScriptedFeedbackSource(
                    new[]
                    {
                        Sample(1UL, 10UL, 0.90, 0.50, 0.00),
                        // This is a newer transformed frame, but authoritative
                        // actuator positions did not move. A requested target alone
                        // must not unlock the next visual-servo iteration.
                        Sample(2UL, 20UL, 0.82, 0.50, 0.00),
                    },
                    cancel);
                var sink = new RecordingTargetSink();
                ReachyVisualServoGazeLoop loop = CreateLoop(
                    feedback,
                    sink,
                    FastPolicy());

                ReachyVisualServoResult result = loop.CenterAsync(
                        EntityId,
                        cancel.Token)
                    .GetAwaiter()
                    .GetResult();

                Equal(
                    ReachyVisualServoStatus.Cancelled,
                    result.Status,
                    "requested target not accepted as motion proof");
                Equal(
                    1,
                    result.AdjustmentCount,
                    "no second adjustment without physical motion");
                Equal(
                    3,
                    feedback.CaptureCount,
                    "feedback continued after target submission");
            }
        }

        private static void StopConditionsAreFailClosed()
        {
            AssertInitialStop(
                Sample(1UL, 10UL, 0.90, 0.50, 0.0, visible: false),
                ReachyVisualServoStatus.TargetLost,
                "target loss");
            AssertInitialStop(
                Sample(
                    1UL,
                    10UL,
                    0.90,
                    0.50,
                    0.0,
                    coverageState: VisionCoverageState.Unusable,
                    validFraction: 0.40,
                    stopTurning: true),
                ReachyVisualServoStatus.CoverageBlocked,
                "invalid transformed coverage");
            AssertInitialStop(
                Sample(1UL, 10UL, 0.90, 0.50, 0.0, loadLimit: true),
                ReachyVisualServoStatus.LoadLimit,
                "authoritative load limit");
            AssertInitialStop(
                Sample(1UL, 10UL, 0.90, 0.50, 0.0, activeFault: true),
                ReachyVisualServoStatus.SafetyInterlock,
                "authoritative safety fault");

            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                var feedback = new ScriptedFeedbackSource(
                    new[] { Sample(1UL, 10UL, 0.90, 0.50, 0.0) });
                var sink = new RecordingTargetSink();
                ReachyVisualServoResult result = CreateLoop(
                        feedback,
                        sink,
                        FastPolicy())
                    .CenterAsync(EntityId, cancel.Token)
                    .GetAwaiter()
                    .GetResult();
                Equal(
                    ReachyVisualServoStatus.Cancelled,
                    result.Status,
                    "pre-cancelled visual servo");
                Equal(0, sink.Submitted.Count, "pre-cancel submits no motion");
            }
        }

        private static void AssertInitialStop(
            ReachyVisualServoFeedbackSample sample,
            ReachyVisualServoStatus expected,
            string description)
        {
            var feedback = new ScriptedFeedbackSource(new[] { sample });
            var sink = new RecordingTargetSink();
            ReachyVisualServoResult result = CreateLoop(
                    feedback,
                    sink,
                    FastPolicy())
                .CenterAsync(EntityId)
                .GetAwaiter()
                .GetResult();
            Equal(expected, result.Status, description + " status");
            Equal(0, result.AdjustmentCount, description + " adjustment count");
            Equal(0, sink.Submitted.Count, description + " submits no motion");
        }

        private static void FeedbackRegressionFailsClosed()
        {
            var feedback = new ScriptedFeedbackSource(
                new[]
                {
                    Sample(2UL, 10UL, 0.90, 0.50, 0.00),
                    Sample(1UL, 20UL, 0.80, 0.50, 0.05),
                });
            var sink = new RecordingTargetSink();
            ReachyVisualServoResult result = CreateLoop(
                    feedback,
                    sink,
                    FastPolicy())
                .CenterAsync(EntityId)
                .GetAwaiter()
                .GetResult();

            Equal(
                ReachyVisualServoStatus.FrameDiscontinuity,
                result.Status,
                "regressed transformed frame fails closed");
            Equal(1, result.AdjustmentCount, "regression stops after first adjustment");
        }

        private static void ObservationReplayProducesRepeatableTrajectories()
        {
            ReachyVisualServoFeedbackSample[] stream =
            {
                Sample(1UL, 10UL, 0.88, 0.58, 0.00),
                Sample(2UL, 20UL, 0.67, 0.54, 0.05),
                Sample(3UL, 30UL, 0.52, 0.51, 0.10),
            };

            var firstSink = new RecordingTargetSink();
            ReachyVisualServoResult first = CreateLoop(
                    new ScriptedFeedbackSource(stream),
                    firstSink,
                    FastPolicy())
                .CenterAsync(EntityId)
                .GetAwaiter()
                .GetResult();
            var secondSink = new RecordingTargetSink();
            ReachyVisualServoResult second = CreateLoop(
                    new ScriptedFeedbackSource(stream),
                    secondSink,
                    FastPolicy())
                .CenterAsync(EntityId)
                .GetAwaiter()
                .GetResult();

            Equal(first.Status, second.Status, "replay status");
            Equal(first.AdjustmentCount, second.AdjustmentCount, "replay adjustments");
            Equal(firstSink.Submitted.Count, secondSink.Submitted.Count, "replay frame count");
            for (int frame = 0; frame < firstSink.Submitted.Count; ++frame)
            {
                double[] left = firstSink.Submitted[frame];
                double[] right = secondSink.Submitted[frame];
                Equal(left.Length, right.Length, "replay actuator count");
                for (int actuator = 0; actuator < left.Length; ++actuator)
                {
                    Near(
                        left[actuator],
                        right[actuator],
                        0.0,
                        "replay trajectory actuator");
                }
            }
        }
    }
}

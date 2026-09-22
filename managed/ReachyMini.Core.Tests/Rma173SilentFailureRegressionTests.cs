#nullable enable

using System;
using ReachyMini.Interop;
using ReachyMini.Simulation;

namespace ReachyMini.Core.Tests
{
    internal static class Rma173SilentFailureRegressionTests
    {
        public static void RunAll()
        {
            UnreadStatePublicationOverwriteIsCounted();
        }

        private static void UnreadStatePublicationOverwriteIsCounted()
        {
            var buffer = new ReachySimulationSnapshotPublicationBuffer();
            buffer.Publish(Snapshot(1UL));
            Equal(0UL, buffer.DroppedSnapshotCount, "first publish drop count");

            buffer.Publish(Snapshot(2UL));
            Equal(1UL, buffer.DroppedSnapshotCount, "unread overwrite drop count");
            True(
                buffer.TryRead(out ReachyPublishedSimulationSnapshot latest),
                "latest snapshot readable");
            Equal(2UL, latest.PublicationSequence, "latest publication sequence");

            buffer.Publish(Snapshot(3UL));
            Equal(
                1UL,
                buffer.DroppedSnapshotCount,
                "read snapshot is not counted as dropped");
            buffer.Publish(Snapshot(4UL));
            Equal(
                2UL,
                buffer.DroppedSnapshotCount,
                "second unread overwrite drop count");
        }

        private static ReachyPublishedSimulationSnapshot Snapshot(ulong sequence)
        {
            var timing = new ReachySimulationTimingSnapshot(
                totalStepCount: sequence,
                deadlineMissCount: 0UL,
                solverWarningCount: 0UL,
                commandQueueOverflowCount: 0UL,
                discardedCommandCount: 0UL,
                droppedStateSnapshotCount: 0UL,
                accumulatedLagSeconds: 0.0,
                lastStepDurationSeconds: 0.001,
                maximumStepDurationSeconds: 0.001);
            return new ReachyPublishedSimulationSnapshot(
                sequence,
                default(ReachySimStateSnapshot),
                timing);
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + " expected true.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    label + ": expected=" + expected + "; actual=" + actual);
            }
        }
    }
}

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
            Equal(
                0UL,
                buffer.DroppedSnapshotCount,
                "drop count updates on observation");
            True(
                buffer.TryRead(out ReachyPublishedSimulationSnapshot latest),
                "latest snapshot readable");
            Equal(2UL, latest.PublicationSequence, "latest publication sequence");
            Equal(1UL, buffer.DroppedSnapshotCount, "unread sequence gap count");

            buffer.Publish(Snapshot(3UL));
            True(
                buffer.TryRead(out ReachyPublishedSimulationSnapshot third),
                "third snapshot readable");
            Equal(3UL, third.PublicationSequence, "third publication sequence");
            Equal(
                1UL,
                buffer.DroppedSnapshotCount,
                "contiguous read adds no drop");

            buffer.Publish(Snapshot(4UL));
            buffer.Publish(Snapshot(5UL));
            True(
                buffer.TryRead(out ReachyPublishedSimulationSnapshot fifth),
                "fifth snapshot readable");
            Equal(5UL, fifth.PublicationSequence, "fifth publication sequence");
            Equal(
                2UL,
                buffer.DroppedSnapshotCount,
                "second unread sequence gap count");
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

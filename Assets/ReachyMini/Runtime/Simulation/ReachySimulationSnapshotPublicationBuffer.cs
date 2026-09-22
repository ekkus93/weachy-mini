#nullable enable

using System.Threading;

namespace ReachyMini.Simulation
{
    internal sealed class ReachySimulationSnapshotPublicationBuffer
    {
        private readonly SnapshotSlot[] slots =
        {
            new SnapshotSlot(),
            new SnapshotSlot(),
            new SnapshotSlot(),
        };
        private int publishedIndex = -1;
        private int writerIndex = -1;
        private long lastReadPublicationSequence;
        private long droppedSnapshotCount;

        internal ulong DroppedSnapshotCount => checked(
            (ulong)System.Math.Max(0L, Interlocked.Read(ref droppedSnapshotCount)));

        internal void Publish(
            ReachyPublishedSimulationSnapshot snapshot)
        {
            int currentPublishedIndex = Volatile.Read(ref publishedIndex);
            if (currentPublishedIndex >= 0)
            {
                ulong currentPublishedSequence =
                    slots[currentPublishedIndex].Value.PublicationSequence;
                ulong lastReadSequence = checked(
                    (ulong)System.Math.Max(
                        0L,
                        Interlocked.Read(ref lastReadPublicationSequence)));
                if (currentPublishedSequence > lastReadSequence)
                {
                    Interlocked.Increment(ref droppedSnapshotCount);
                }
            }

            int nextIndex = (writerIndex + 1) % slots.Length;
            SnapshotSlot slot = slots[nextIndex];
            long version = Volatile.Read(ref slot.Version);
            if ((version & 1L) != 0L)
            {
                ++version;
            }

            Volatile.Write(ref slot.Version, checked(version + 1L));
            slot.Value = snapshot;
            Volatile.Write(ref slot.Version, checked(version + 2L));
            writerIndex = nextIndex;
            Volatile.Write(ref publishedIndex, nextIndex);
        }

        internal bool TryRead(
            out ReachyPublishedSimulationSnapshot snapshot)
        {
            for (int attempt = 0; attempt < 8; ++attempt)
            {
                int index = Volatile.Read(ref publishedIndex);
                if (index < 0)
                {
                    snapshot = default;
                    return false;
                }

                SnapshotSlot slot = slots[index];
                long before = Volatile.Read(ref slot.Version);
                if ((before & 1L) != 0L)
                {
                    Thread.Yield();
                    continue;
                }

                ReachyPublishedSimulationSnapshot candidate = slot.Value;
                long after = Volatile.Read(ref slot.Version);
                if (before == after && (after & 1L) == 0L)
                {
                    snapshot = candidate;
                    RecordReadSequence(candidate.PublicationSequence);
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        private void RecordReadSequence(ulong publicationSequence)
        {
            long candidate = checked((long)publicationSequence);
            while (true)
            {
                long observed = Interlocked.Read(ref lastReadPublicationSequence);
                if (candidate <= observed)
                {
                    return;
                }
                if (Interlocked.CompareExchange(
                        ref lastReadPublicationSequence,
                        candidate,
                        observed) == observed)
                {
                    return;
                }
            }
        }

        private sealed class SnapshotSlot
        {
            internal long Version;
            internal ReachyPublishedSimulationSnapshot Value;
        }
    }
}

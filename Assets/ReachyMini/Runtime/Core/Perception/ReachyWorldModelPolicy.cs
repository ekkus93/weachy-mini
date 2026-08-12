#nullable enable

using System;

namespace ReachyMini.Perception
{
    public enum WorldEntityVisibility
    {
        CurrentlyVisible = 0,
        RecentlySeen = 1,
    }

    public enum WorldModelUpdateStatus
    {
        Accepted = 0,
        DuplicateIgnored = 1,
        StaleRejected = 2,
        InvalidCoverageRejected = 3,
        CapacityExceeded = 4,
        ClassificationConflict = 5,
        EntityNotFound = 6,
        DescriptionAccepted = 7,
        DescriptionDuplicate = 8,
        DescriptionRejected = 9,
    }

    public sealed class WorldModelPolicy
    {
        public WorldModelPolicy(
            int maximumEntities,
            int maximumObservationHistoryPerEntity,
            int maximumDescriptionsPerEntity,
            int maximumDescriptionCharacters,
            long entityExpiryNanoseconds,
            int maximumScopeCursors = 256)
        {
            if (maximumEntities <= 0 || maximumEntities > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEntities));
            }
            if (maximumObservationHistoryPerEntity <= 0 ||
                maximumObservationHistoryPerEntity > 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumObservationHistoryPerEntity));
            }
            if (maximumDescriptionsPerEntity <= 0 ||
                maximumDescriptionsPerEntity > 128)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDescriptionsPerEntity));
            }
            if (maximumDescriptionCharacters <= 0 ||
                maximumDescriptionCharacters > 8192)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDescriptionCharacters));
            }
            if (entityExpiryNanoseconds <= 0L ||
                entityExpiryNanoseconds > 86_400_000_000_000L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entityExpiryNanoseconds));
            }
            if (maximumScopeCursors <= 0 || maximumScopeCursors > 4096)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumScopeCursors));
            }
            if (maximumScopeCursors < maximumEntities)
            {
                throw new ArgumentException(
                    "Scope-cursor capacity must cover every retained entity scope.",
                    nameof(maximumScopeCursors));
            }

            MaximumEntities = maximumEntities;
            MaximumObservationHistoryPerEntity =
                maximumObservationHistoryPerEntity;
            MaximumDescriptionsPerEntity = maximumDescriptionsPerEntity;
            MaximumDescriptionCharacters = maximumDescriptionCharacters;
            EntityExpiryNanoseconds = entityExpiryNanoseconds;
            MaximumScopeCursors = maximumScopeCursors;
        }

        public int MaximumEntities { get; }

        public int MaximumObservationHistoryPerEntity { get; }

        public int MaximumDescriptionsPerEntity { get; }

        public int MaximumDescriptionCharacters { get; }

        public long EntityExpiryNanoseconds { get; }

        public int MaximumScopeCursors { get; }

        public static WorldModelPolicy CreateMobileDefault()
        {
            return new WorldModelPolicy(
                maximumEntities: 128,
                maximumObservationHistoryPerEntity: 16,
                maximumDescriptionsPerEntity: 8,
                maximumDescriptionCharacters: 2048,
                entityExpiryNanoseconds: 5_000_000_000L,
                maximumScopeCursors: 256);
        }
    }
}

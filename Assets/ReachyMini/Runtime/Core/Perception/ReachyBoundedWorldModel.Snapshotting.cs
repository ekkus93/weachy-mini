#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Perception
{
    public sealed partial class BoundedWorldModel
    {
        private WorldModelUpdateResult Result(
            WorldModelUpdateStatus status,
            string diagnostic,
            long timestampNanoseconds)
        {
            return new WorldModelUpdateResult(
                status,
                diagnostic,
                CreateSnapshot(timestampNanoseconds));
        }

        private WorldModelSnapshot CreateSnapshot(
            long timestampNanoseconds)
        {
            var snapshots = new List<WorldEntitySnapshot>(entitiesById.Count);
            foreach (EntityState entity in entitiesById.Values)
            {
                var descriptions = new List<WorldDescriptionSnapshot>(
                    entity.Descriptions.Count);
                for (int index = 0;
                    index < entity.Descriptions.Count;
                    ++index)
                {
                    DescriptionState description =
                        entity.Descriptions[index];
                    descriptions.Add(
                        new WorldDescriptionSnapshot(
                            description.Text,
                            description.ProviderInstanceId,
                            description.FirstObservedTimestampNanoseconds,
                            description.LastConfirmedTimestampNanoseconds,
                            description.ConfirmationCount,
                            description.SourceFrameIdentity));
                }

                snapshots.Add(
                    new WorldEntitySnapshot(
                        entity.EntityId,
                        entity.TrackingLocalId,
                        entity.Classification,
                        entity.TrackingProviderInstanceId,
                        entity.LatestFrameIdentity,
                        entity.IsCurrentlyVisible
                            ? WorldEntityVisibility.CurrentlyVisible
                            : WorldEntityVisibility.RecentlySeen,
                        entity.FirstSeenTimestampNanoseconds,
                        entity.LastSeenTimestampNanoseconds,
                        entity.Confidence,
                        entity.Bounds,
                        entity.Position,
                        entity.Direction,
                        entity.Coverage,
                        entity.Observations,
                        descriptions,
                        timestampNanoseconds,
                        entity.DroppedObservationCount,
                        entity.DroppedDescriptionCount));
            }
            snapshots.Sort(
                (left, right) => string.CompareOrdinal(
                    left.EntityId,
                    right.EntityId));

            return new WorldModelSnapshot(
                timestampNanoseconds,
                snapshots,
                new WorldModelDiagnosticsSnapshot(
                    acceptedTrackingBatchCount,
                    duplicateTrackingBatchCount,
                    staleTrackingBatchCount,
                    invalidCoverageBatchCount,
                    capacityRejectedBatchCount,
                    classificationConflictCount,
                    acceptedDescriptionCount,
                    duplicateDescriptionCount,
                    rejectedDescriptionCount,
                    expiredEntityCount,
                    cursorsByScope.Count,
                    droppedScopeCursorCount));
        }

        private static void RequirePositiveTimestamp(
            long timestampNanoseconds)
        {
            if (timestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampNanoseconds));
            }
        }

        private void AdvanceModelTime(long timestampNanoseconds)
        {
            if (timestampNanoseconds > lastModelTimestampNanoseconds)
            {
                lastModelTimestampNanoseconds = timestampNanoseconds;
            }
        }
    }
}

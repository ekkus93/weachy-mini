#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Perception
{
    public sealed partial class BoundedWorldModel
    {
        private readonly object sync = new object();
        private readonly WorldModelPolicy policy;
        private readonly Dictionary<string, EntityState> entitiesById =
            new Dictionary<string, EntityState>(StringComparer.Ordinal);
        private readonly Dictionary<string, EntityState> entitiesByAssociation =
            new Dictionary<string, EntityState>(StringComparer.Ordinal);
        private readonly Dictionary<string, FrameCursor> cursorsByScope =
            new Dictionary<string, FrameCursor>(StringComparer.Ordinal);
        private ulong nextEntityNumber = 1UL;
        private long lastModelTimestampNanoseconds;
        private long acceptedTrackingBatchCount;
        private long duplicateTrackingBatchCount;
        private long staleTrackingBatchCount;
        private long invalidCoverageBatchCount;
        private long capacityRejectedBatchCount;
        private long classificationConflictCount;
        private long acceptedDescriptionCount;
        private long duplicateDescriptionCount;
        private long rejectedDescriptionCount;
        private long expiredEntityCount;
        private long droppedScopeCursorCount;

        public BoundedWorldModel(WorldModelPolicy policy)
        {
            this.policy = policy ??
                throw new ArgumentNullException(nameof(policy));
        }

        public WorldModelPolicy Policy => policy;

        public WorldModelUpdateResult ApplyTracking(
            WorldModelTrackingBatch batch)
        {
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            lock (sync)
            {
                long timestamp =
                    batch.FrameIdentity.SourceTimestampNanoseconds;
                RequirePositiveTimestamp(timestamp);
                if (timestamp < lastModelTimestampNanoseconds)
                {
                    staleTrackingBatchCount = checked(
                        staleTrackingBatchCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.StaleRejected,
                        "The tracking frame timestamp precedes the world-model clock.",
                        lastModelTimestampNanoseconds);
                }
                string scopeKey = BuildScopeKey(
                    batch.ProviderInstanceId,
                    batch.FrameIdentity);
                if (cursorsByScope.TryGetValue(
                    scopeKey,
                    out FrameCursor? cursor))
                {
                    if (IsExactDuplicate(cursor, batch.FrameIdentity))
                    {
                        duplicateTrackingBatchCount = checked(
                            duplicateTrackingBatchCount + 1L);
                        return Result(
                            WorldModelUpdateStatus.DuplicateIgnored,
                            "The exact tracking frame was already applied.",
                            lastModelTimestampNanoseconds);
                    }
                    if (batch.FrameIdentity.SourceSequence <=
                            cursor.SourceSequence ||
                        timestamp <= cursor.SourceTimestampNanoseconds ||
                        batch.FrameIdentity.AuthoritativeSequence <=
                            cursor.AuthoritativeSequence)
                    {
                        staleTrackingBatchCount = checked(
                            staleTrackingBatchCount + 1L);
                        return Result(
                            WorldModelUpdateStatus.StaleRejected,
                            "The tracking frame is stale or conflicts with accepted ordering.",
                            lastModelTimestampNanoseconds);
                    }
                }

                if (!TrySelectCursorEviction(
                    scopeKey,
                    timestamp,
                    out string? cursorToEvict))
                {
                    capacityRejectedBatchCount = checked(
                        capacityRejectedBatchCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.CapacityExceeded,
                        "Ordering cursor capacity is occupied by retained entity scopes; the batch was rejected without mutation.",
                        lastModelTimestampNanoseconds);
                }

                AdvanceModelTime(timestamp);
                ExpireInternal(timestamp);
                MarkOtherGenerationsNotVisible(
                    batch.ProviderInstanceId,
                    batch.FrameIdentity.CameraId,
                    scopeKey);

                if (!batch.Coverage.CanCreateVisualObservations)
                {
                    MarkScopeNotVisible(scopeKey);
                    CommitCursor(
                        scopeKey,
                        batch.FrameIdentity,
                        cursorToEvict);
                    invalidCoverageBatchCount = checked(
                        invalidCoverageBatchCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.InvalidCoverageRejected,
                        "Coverage is unusable or unavailable; no observation was created.",
                        timestamp);
                }

                int newEntityCount = CountNewEntities(batch, scopeKey);
                if (entitiesById.Count + newEntityCount >
                    policy.MaximumEntities)
                {
                    MarkScopeNotVisible(scopeKey);
                    CommitCursor(
                        scopeKey,
                        batch.FrameIdentity,
                        cursorToEvict);
                    capacityRejectedBatchCount = checked(
                        capacityRejectedBatchCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.CapacityExceeded,
                        "The bounded entity capacity would be exceeded; the batch was rejected.",
                        timestamp);
                }

                for (int index = 0; index < batch.Objects.Count; ++index)
                {
                    TrackedObject tracked = batch.Objects[index];
                    string associationKey = BuildAssociationKey(
                        scopeKey,
                        tracked.LocalId);
                    if (entitiesByAssociation.TryGetValue(
                            associationKey,
                            out EntityState? existing) &&
                        !string.Equals(
                            existing.Classification,
                            tracked.Classification,
                            StringComparison.Ordinal))
                    {
                        MarkScopeNotVisible(scopeKey);
                        CommitCursor(
                            scopeKey,
                            batch.FrameIdentity,
                            cursorToEvict);
                        classificationConflictCount = checked(
                            classificationConflictCount + 1L);
                        return Result(
                            WorldModelUpdateStatus.ClassificationConflict,
                            "A stable tracker ID changed classification; the batch was rejected.",
                            timestamp);
                    }
                }

                MarkScopeNotVisible(scopeKey);
                WorldCoverageContext coverage =
                    WorldCoverageContext.From(batch.Coverage);
                for (int index = 0; index < batch.Objects.Count; ++index)
                {
                    ApplyObject(
                        batch.ProviderInstanceId,
                        batch.FrameIdentity,
                        scopeKey,
                        batch.Objects[index],
                        coverage);
                }

                CommitCursor(
                    scopeKey,
                    batch.FrameIdentity,
                    cursorToEvict);
                acceptedTrackingBatchCount = checked(
                    acceptedTrackingBatchCount + 1L);
                return Result(
                    WorldModelUpdateStatus.Accepted,
                    "Tracking observations were applied.",
                    timestamp);
            }
        }

        public WorldModelUpdateResult ApplyDescription(
            WorldModelDescriptionUpdate update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            lock (sync)
            {
                long timestamp = update.AppliedAtTimestampNanoseconds;
                RequirePositiveTimestamp(timestamp);
                if (timestamp < lastModelTimestampNanoseconds)
                {
                    rejectedDescriptionCount = checked(
                        rejectedDescriptionCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.DescriptionRejected,
                        "The semantic result timestamp precedes the world-model clock.",
                        lastModelTimestampNanoseconds);
                }
                AdvanceModelTime(timestamp);
                ExpireInternal(timestamp);
                if (!entitiesById.TryGetValue(
                    update.EntityId,
                    out EntityState? entity))
                {
                    rejectedDescriptionCount = checked(
                        rejectedDescriptionCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.EntityNotFound,
                        "The target entity is absent or expired.",
                        timestamp);
                }
                if (!SameObservationContinuity(
                    entity.LatestFrameIdentity,
                    update.SourceFrameIdentity) ||
                    !HasObservation(entity, update.SourceFrameIdentity))
                {
                    rejectedDescriptionCount = checked(
                        rejectedDescriptionCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.DescriptionRejected,
                        "The semantic result does not belong to the entity generation.",
                        timestamp);
                }

                if (update.Text.Length > policy.MaximumDescriptionCharacters)
                {
                    rejectedDescriptionCount = checked(
                        rejectedDescriptionCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.DescriptionRejected,
                        "The semantic description exceeds the configured character limit.",
                        timestamp);
                }

                string normalized = NormalizeDescription(update.Text);
                if (normalized.Length > policy.MaximumDescriptionCharacters)
                {
                    rejectedDescriptionCount = checked(
                        rejectedDescriptionCount + 1L);
                    return Result(
                        WorldModelUpdateStatus.DescriptionRejected,
                        "The semantic description exceeds the configured character limit.",
                        timestamp);
                }

                for (int index = 0;
                    index < entity.Descriptions.Count;
                    ++index)
                {
                    DescriptionState description =
                        entity.Descriptions[index];
                    if (string.Equals(
                        description.Text,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        description.LastConfirmedTimestampNanoseconds =
                            timestamp;
                        description.ProviderInstanceId =
                            update.ProviderInstanceId;
                        description.ConfirmationCount = checked(
                            description.ConfirmationCount + 1);
                        description.SourceFrameIdentity =
                            update.SourceFrameIdentity;
                        entity.Descriptions.RemoveAt(index);
                        entity.Descriptions.Add(description);
                        duplicateDescriptionCount = checked(
                            duplicateDescriptionCount + 1L);
                        return Result(
                            WorldModelUpdateStatus.DescriptionDuplicate,
                            "The description matched an existing semantic record and was confirmed without adding history.",
                            timestamp);
                    }
                }

                entity.Descriptions.Add(
                    new DescriptionState(
                        normalized,
                        update.ProviderInstanceId,
                        timestamp,
                        update.SourceFrameIdentity));
                if (entity.Descriptions.Count >
                    policy.MaximumDescriptionsPerEntity)
                {
                    entity.Descriptions.RemoveAt(0);
                    entity.DroppedDescriptionCount = checked(
                        entity.DroppedDescriptionCount + 1L);
                }
                acceptedDescriptionCount = checked(
                    acceptedDescriptionCount + 1L);
                return Result(
                    WorldModelUpdateStatus.DescriptionAccepted,
                    "The semantic description was attached to the entity.",
                    timestamp);
            }
        }

        public WorldModelSnapshot GetSnapshot(
            long timestampNanoseconds)
        {
            lock (sync)
            {
                RequirePositiveTimestamp(timestampNanoseconds);
                if (timestampNanoseconds < lastModelTimestampNanoseconds)
                {
                    throw new InvalidOperationException(
                        "World-model snapshot time cannot move backward.");
                }
                AdvanceModelTime(timestampNanoseconds);
                ExpireInternal(timestampNanoseconds);
                return CreateSnapshot(timestampNanoseconds);
            }
        }
    }
}

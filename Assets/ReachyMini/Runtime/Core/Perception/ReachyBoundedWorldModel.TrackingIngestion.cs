#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Perception
{
    public sealed partial class BoundedWorldModel
    {
        private static bool IsExactDuplicate(
            FrameCursor cursor,
            ReachyVisionFrameIdentity identity)
        {
            return cursor.SourceSequence == identity.SourceSequence &&
                cursor.SourceTimestampNanoseconds ==
                    identity.SourceTimestampNanoseconds &&
                cursor.AuthoritativeSequence ==
                    identity.AuthoritativeSequence;
        }

        private int CountNewEntities(
            WorldModelTrackingBatch batch,
            string scopeKey)
        {
            int count = 0;
            for (int index = 0; index < batch.Objects.Count; ++index)
            {
                string key = BuildAssociationKey(
                    scopeKey,
                    batch.Objects[index].LocalId);
                if (!entitiesByAssociation.ContainsKey(key))
                {
                    count = checked(count + 1);
                }
            }
            return count;
        }

        private void ApplyObject(
            string providerInstanceId,
            ReachyVisionFrameIdentity frameIdentity,
            string scopeKey,
            TrackedObject tracked,
            WorldCoverageContext coverage)
        {
            string associationKey = BuildAssociationKey(
                scopeKey,
                tracked.LocalId);
            WorldDirectionEstimate direction =
                WorldDirectionEstimate.FromBounds(tracked.Bounds);
            if (!entitiesByAssociation.TryGetValue(
                associationKey,
                out EntityState? entity))
            {
                string entityId = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "entity-{0:D6}",
                    nextEntityNumber);
                nextEntityNumber = checked(nextEntityNumber + 1UL);
                entity = new EntityState(
                    entityId,
                    associationKey,
                    scopeKey,
                    tracked.LocalId,
                    tracked.Classification,
                    providerInstanceId,
                    frameIdentity,
                    frameIdentity.SourceTimestampNanoseconds,
                    tracked.Confidence,
                    tracked.Bounds,
                    direction,
                    coverage);
                entitiesById.Add(entityId, entity);
                entitiesByAssociation.Add(associationKey, entity);
            }
            else
            {
                entity.LatestFrameIdentity = frameIdentity;
                entity.LastSeenTimestampNanoseconds =
                    frameIdentity.SourceTimestampNanoseconds;
                entity.Confidence = tracked.Confidence;
                entity.Bounds = tracked.Bounds;
                entity.Direction = direction;
                entity.Coverage = coverage;
                entity.IsCurrentlyVisible = true;
            }

            entity.Observations.Add(
                new WorldObservationSnapshot(
                    frameIdentity.SourceTimestampNanoseconds,
                    frameIdentity.SourceSequence,
                    frameIdentity.AuthoritativeSequence,
                    tracked.Confidence,
                    tracked.Bounds,
                    direction,
                    coverage));
            if (entity.Observations.Count >
                policy.MaximumObservationHistoryPerEntity)
            {
                entity.Observations.RemoveAt(0);
                entity.DroppedObservationCount = checked(
                    entity.DroppedObservationCount + 1L);
            }
        }

        private void MarkScopeNotVisible(string scopeKey)
        {
            foreach (EntityState entity in entitiesById.Values)
            {
                if (string.Equals(
                    entity.ScopeKey,
                    scopeKey,
                    StringComparison.Ordinal))
                {
                    entity.IsCurrentlyVisible = false;
                }
            }
        }

        private void MarkOtherGenerationsNotVisible(
            string providerInstanceId,
            string cameraId,
            string currentScopeKey)
        {
            string prefix = string.Concat(
                providerInstanceId,
                "\u001f",
                cameraId,
                "\u001f");
            foreach (EntityState entity in entitiesById.Values)
            {
                if (entity.ScopeKey.StartsWith(
                        prefix,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        entity.ScopeKey,
                        currentScopeKey,
                        StringComparison.Ordinal))
                {
                    entity.IsCurrentlyVisible = false;
                }
            }
        }


        private void ExpireInternal(long timestampNanoseconds)
        {
            var expiredIds = new List<string>();
            foreach (KeyValuePair<string, EntityState> entry in entitiesById)
            {
                long age = checked(
                    timestampNanoseconds -
                    entry.Value.LastSeenTimestampNanoseconds);
                if (age >= policy.EntityExpiryNanoseconds)
                {
                    expiredIds.Add(entry.Key);
                }
            }

            for (int index = 0; index < expiredIds.Count; ++index)
            {
                EntityState entity = entitiesById[expiredIds[index]];
                entitiesById.Remove(entity.EntityId);
                entitiesByAssociation.Remove(entity.AssociationKey);
                expiredEntityCount = checked(expiredEntityCount + 1L);
            }
        }

        private bool TrySelectCursorEviction(
            string scopeKey,
            long timestampNanoseconds,
            out string? cursorToEvict)
        {
            cursorToEvict = null;
            if (cursorsByScope.ContainsKey(scopeKey) ||
                cursorsByScope.Count < policy.MaximumScopeCursors)
            {
                return true;
            }

            string? oldestKey = null;
            long oldestTimestamp = long.MaxValue;
            foreach (KeyValuePair<string, FrameCursor> entry in cursorsByScope)
            {
                if (HasEntityRetainedForScopeAt(
                    entry.Key,
                    timestampNanoseconds))
                {
                    continue;
                }
                if (entry.Value.SourceTimestampNanoseconds < oldestTimestamp ||
                    (entry.Value.SourceTimestampNanoseconds == oldestTimestamp &&
                     string.CompareOrdinal(entry.Key, oldestKey) < 0))
                {
                    oldestKey = entry.Key;
                    oldestTimestamp =
                        entry.Value.SourceTimestampNanoseconds;
                }
            }
            if (oldestKey == null)
            {
                return false;
            }
            cursorToEvict = oldestKey;
            return true;
        }

        private bool HasEntityRetainedForScopeAt(
            string scopeKey,
            long timestampNanoseconds)
        {
            foreach (EntityState entity in entitiesById.Values)
            {
                if (!string.Equals(
                    entity.ScopeKey,
                    scopeKey,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                long age = checked(
                    timestampNanoseconds -
                    entity.LastSeenTimestampNanoseconds);
                if (age < policy.EntityExpiryNanoseconds)
                {
                    return true;
                }
            }
            return false;
        }

        private void CommitCursor(
            string scopeKey,
            ReachyVisionFrameIdentity frameIdentity,
            string? cursorToEvict)
        {
            if (cursorToEvict != null)
            {
                if (!cursorsByScope.Remove(cursorToEvict))
                {
                    throw new InvalidOperationException(
                        "The selected ordering cursor disappeared before commit.");
                }
                droppedScopeCursorCount = checked(
                    droppedScopeCursorCount + 1L);
            }
            cursorsByScope[scopeKey] = new FrameCursor(
                frameIdentity.SourceSequence,
                frameIdentity.SourceTimestampNanoseconds,
                frameIdentity.AuthoritativeSequence);
        }

        private static string BuildScopeKey(
            string providerInstanceId,
            ReachyVisionFrameIdentity identity)
        {
            return string.Concat(
                providerInstanceId,
                "\u001f",
                identity.CameraId,
                "\u001f",
                identity.SourceSessionId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                "\u001f",
                identity.ContinuityId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string BuildAssociationKey(
            string scopeKey,
            string localId)
        {
            return string.Concat(scopeKey, "\u001f", localId);
        }
    }
}

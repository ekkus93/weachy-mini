#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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

    public sealed class WorldPositionEstimate
    {
        private WorldPositionEstimate(
            bool isKnown,
            double? xMeters,
            double? yMeters,
            double? zMeters,
            string coordinateFrame,
            string method)
        {
            IsKnown = isKnown;
            XMeters = xMeters;
            YMeters = yMeters;
            ZMeters = zMeters;
            CoordinateFrame = RequireText(
                coordinateFrame,
                nameof(coordinateFrame));
            Method = RequireText(method, nameof(method));
        }

        public bool IsKnown { get; }

        public double? XMeters { get; }

        public double? YMeters { get; }

        public double? ZMeters { get; }

        public string CoordinateFrame { get; }

        public string Method { get; }

        public static WorldPositionEstimate UnknownFromTwoDimensionalTracking()
        {
            return new WorldPositionEstimate(
                isKnown: false,
                xMeters: null,
                yMeters: null,
                zMeters: null,
                coordinateFrame: "unknown",
                method: "unavailable_from_2d_tracking");
        }

        public static WorldPositionEstimate Known(
            double xMeters,
            double yMeters,
            double zMeters,
            string coordinateFrame,
            string method)
        {
            RequireFinite(xMeters, nameof(xMeters));
            RequireFinite(yMeters, nameof(yMeters));
            RequireFinite(zMeters, nameof(zMeters));
            return new WorldPositionEstimate(
                isKnown: true,
                xMeters: xMeters,
                yMeters: yMeters,
                zMeters: zMeters,
                coordinateFrame: coordinateFrame,
                method: method);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "World-model text cannot be empty.",
                    name);
            }
            return value;
        }
    }

    public sealed class WorldDirectionEstimate
    {
        public WorldDirectionEstimate(
            double x,
            double y,
            double z,
            string coordinateFrame,
            string method)
        {
            RequireFinite(x, nameof(x));
            RequireFinite(y, nameof(y));
            RequireFinite(z, nameof(z));
            double magnitude = Math.Sqrt((x * x) + (y * y) + (z * z));
            if (magnitude <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(z));
            }

            X = x / magnitude;
            Y = y / magnitude;
            Z = z / magnitude;
            CoordinateFrame = RequireText(
                coordinateFrame,
                nameof(coordinateFrame));
            Method = RequireText(method, nameof(method));
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public string CoordinateFrame { get; }

        public string Method { get; }

        internal static WorldDirectionEstimate FromBounds(
            NormalizedVisionBounds bounds)
        {
            if (bounds == null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }

            return new WorldDirectionEstimate(
                (2.0 * bounds.CenterX) - 1.0,
                1.0 - (2.0 * bounds.CenterY),
                1.0,
                "transformed_reachy_eye_normalized",
                "normalized_image_ray_without_metric_intrinsics");
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "World-model text cannot be empty.",
                    name);
            }
            return value;
        }
    }

    public sealed class WorldCoverageContext
    {
        public WorldCoverageContext(
            VisionCoverageState state,
            double validFraction,
            bool shouldStopVisionDrivenTurning,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(VisionCoverageState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            if (double.IsNaN(validFraction) ||
                double.IsInfinity(validFraction) ||
                validFraction < 0.0 ||
                validFraction > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(validFraction));
            }
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                throw new ArgumentException(
                    "Coverage diagnostics cannot be empty.",
                    nameof(diagnostic));
            }
            if (diagnostic.Length > 512)
            {
                throw new ArgumentOutOfRangeException(nameof(diagnostic));
            }

            State = state;
            ValidFraction = validFraction;
            ShouldStopVisionDrivenTurning =
                shouldStopVisionDrivenTurning;
            Diagnostic = diagnostic;
        }

        public VisionCoverageState State { get; }

        public double ValidFraction { get; }

        public bool ShouldStopVisionDrivenTurning { get; }

        public string Diagnostic { get; }

        internal static WorldCoverageContext From(
            ReachyVisionCoverage coverage)
        {
            if (coverage == null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }
            return new WorldCoverageContext(
                coverage.State,
                coverage.Fraction,
                coverage.ShouldStopVisionDrivenTurning,
                coverage.Diagnostic);
        }
    }

    public sealed class BoundedWorldModel
    {
        private sealed class FrameCursor
        {
            public FrameCursor(
                ulong sourceSequence,
                long sourceTimestampNanoseconds,
                ulong authoritativeSequence)
            {
                SourceSequence = sourceSequence;
                SourceTimestampNanoseconds = sourceTimestampNanoseconds;
                AuthoritativeSequence = authoritativeSequence;
            }

            public ulong SourceSequence { get; }

            public long SourceTimestampNanoseconds { get; }

            public ulong AuthoritativeSequence { get; }
        }

        private sealed class DescriptionState
        {
            public DescriptionState(
                string text,
                string providerInstanceId,
                long timestampNanoseconds,
                ReachyVisionFrameIdentity sourceFrameIdentity)
            {
                Text = text;
                ProviderInstanceId = providerInstanceId;
                FirstObservedTimestampNanoseconds = timestampNanoseconds;
                LastConfirmedTimestampNanoseconds = timestampNanoseconds;
                ConfirmationCount = 1;
                SourceFrameIdentity = sourceFrameIdentity;
            }

            public string Text { get; }

            public string ProviderInstanceId { get; set; }

            public long FirstObservedTimestampNanoseconds { get; }

            public long LastConfirmedTimestampNanoseconds { get; set; }

            public int ConfirmationCount { get; set; }

            public ReachyVisionFrameIdentity SourceFrameIdentity { get; set; }
        }

        private sealed class EntityState
        {
            public EntityState(
                string entityId,
                string associationKey,
                string scopeKey,
                string trackingLocalId,
                string classification,
                string trackingProviderInstanceId,
                ReachyVisionFrameIdentity frameIdentity,
                long timestampNanoseconds,
                double confidence,
                NormalizedVisionBounds bounds,
                WorldDirectionEstimate direction,
                WorldCoverageContext coverage)
            {
                EntityId = entityId;
                AssociationKey = associationKey;
                ScopeKey = scopeKey;
                TrackingLocalId = trackingLocalId;
                Classification = classification;
                TrackingProviderInstanceId =
                    trackingProviderInstanceId;
                LatestFrameIdentity = frameIdentity;
                FirstSeenTimestampNanoseconds = timestampNanoseconds;
                LastSeenTimestampNanoseconds = timestampNanoseconds;
                Confidence = confidence;
                Bounds = bounds;
                Position =
                    WorldPositionEstimate.UnknownFromTwoDimensionalTracking();
                Direction = direction;
                Coverage = coverage;
                IsCurrentlyVisible = true;
                Observations = new List<WorldObservationSnapshot>();
                Descriptions = new List<DescriptionState>();
            }

            public string EntityId { get; }

            public string AssociationKey { get; }

            public string ScopeKey { get; }

            public string TrackingLocalId { get; }

            public string Classification { get; }

            public string TrackingProviderInstanceId { get; }

            public ReachyVisionFrameIdentity LatestFrameIdentity { get; set; }

            public long FirstSeenTimestampNanoseconds { get; }

            public long LastSeenTimestampNanoseconds { get; set; }

            public double Confidence { get; set; }

            public NormalizedVisionBounds Bounds { get; set; }

            public WorldPositionEstimate Position { get; set; }

            public WorldDirectionEstimate Direction { get; set; }

            public WorldCoverageContext Coverage { get; set; }

            public bool IsCurrentlyVisible { get; set; }

            public List<WorldObservationSnapshot> Observations { get; }

            public List<DescriptionState> Descriptions { get; }

            public long DroppedObservationCount { get; set; }

            public long DroppedDescriptionCount { get; set; }
        }

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

        private static bool HasObservation(
            EntityState entity,
            ReachyVisionFrameIdentity identity)
        {
            for (int index = 0; index < entity.Observations.Count; ++index)
            {
                WorldObservationSnapshot observation =
                    entity.Observations[index];
                if (observation.SourceSequence == identity.SourceSequence &&
                    observation.TimestampNanoseconds ==
                        identity.SourceTimestampNanoseconds &&
                    observation.AuthoritativeSequence ==
                        identity.AuthoritativeSequence)
                {
                    return true;
                }
            }
            return false;
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

        private static bool SameObservationContinuity(
            ReachyVisionFrameIdentity left,
            ReachyVisionFrameIdentity right)
        {
            return string.Equals(
                    left.CameraId,
                    right.CameraId,
                    StringComparison.Ordinal) &&
                left.SourceSessionId == right.SourceSessionId &&
                left.ContinuityId == right.ContinuityId;
        }

        private static string NormalizeDescription(string text)
        {
            string trimmed = text.Trim();
            var characters = new char[trimmed.Length];
            int count = 0;
            bool previousWhitespace = false;
            for (int index = 0; index < trimmed.Length; ++index)
            {
                char character = trimmed[index];
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWhitespace)
                    {
                        characters[count] = ' ';
                        count = checked(count + 1);
                        previousWhitespace = true;
                    }
                }
                else
                {
                    characters[count] = character;
                    count = checked(count + 1);
                    previousWhitespace = false;
                }
            }
            return new string(characters, 0, count);
        }
    }
}

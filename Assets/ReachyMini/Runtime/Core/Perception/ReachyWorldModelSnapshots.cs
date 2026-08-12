#nullable enable

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Perception
{
    public sealed class WorldObservationSnapshot
    {
        internal WorldObservationSnapshot(
            long timestampNanoseconds,
            ulong sourceSequence,
            ulong authoritativeSequence,
            double confidence,
            NormalizedVisionBounds bounds,
            WorldDirectionEstimate direction,
            WorldCoverageContext coverage)
        {
            TimestampNanoseconds = timestampNanoseconds;
            SourceSequence = sourceSequence;
            AuthoritativeSequence = authoritativeSequence;
            Confidence = confidence;
            Bounds = bounds;
            Direction = direction;
            Coverage = coverage;
        }

        public long TimestampNanoseconds { get; }

        public ulong SourceSequence { get; }

        public ulong AuthoritativeSequence { get; }

        public double Confidence { get; }

        public NormalizedVisionBounds Bounds { get; }

        public WorldDirectionEstimate Direction { get; }

        public WorldCoverageContext Coverage { get; }
    }

    public sealed class WorldDescriptionSnapshot
    {
        internal WorldDescriptionSnapshot(
            string text,
            string providerInstanceId,
            long firstObservedTimestampNanoseconds,
            long lastConfirmedTimestampNanoseconds,
            int confirmationCount,
            ReachyVisionFrameIdentity sourceFrameIdentity)
        {
            Text = text;
            ProviderInstanceId = providerInstanceId;
            FirstObservedTimestampNanoseconds =
                firstObservedTimestampNanoseconds;
            LastConfirmedTimestampNanoseconds =
                lastConfirmedTimestampNanoseconds;
            ConfirmationCount = confirmationCount;
            SourceFrameIdentity = sourceFrameIdentity;
        }

        public string Text { get; }

        public string ProviderInstanceId { get; }

        public long FirstObservedTimestampNanoseconds { get; }

        public long LastConfirmedTimestampNanoseconds { get; }

        public int ConfirmationCount { get; }

        public ReachyVisionFrameIdentity SourceFrameIdentity { get; }
    }

    public sealed class WorldEntitySnapshot
    {
        private readonly ReadOnlyCollection<WorldObservationSnapshot>
            observations;
        private readonly ReadOnlyCollection<WorldDescriptionSnapshot>
            descriptions;

        internal WorldEntitySnapshot(
            string entityId,
            string trackingLocalId,
            string classification,
            string trackingProviderInstanceId,
            ReachyVisionFrameIdentity latestFrameIdentity,
            WorldEntityVisibility visibility,
            long firstSeenTimestampNanoseconds,
            long lastSeenTimestampNanoseconds,
            double confidence,
            NormalizedVisionBounds bounds,
            WorldPositionEstimate position,
            WorldDirectionEstimate direction,
            WorldCoverageContext coverage,
            IReadOnlyList<WorldObservationSnapshot> observations,
            IReadOnlyList<WorldDescriptionSnapshot> descriptions,
            long snapshotTimestampNanoseconds,
            long droppedObservationCount,
            long droppedDescriptionCount)
        {
            EntityId = entityId;
            TrackingLocalId = trackingLocalId;
            Classification = classification;
            TrackingProviderInstanceId = trackingProviderInstanceId;
            LatestFrameIdentity = latestFrameIdentity;
            Visibility = visibility;
            FirstSeenTimestampNanoseconds = firstSeenTimestampNanoseconds;
            LastSeenTimestampNanoseconds = lastSeenTimestampNanoseconds;
            Confidence = confidence;
            Bounds = bounds;
            Position = position;
            Direction = direction;
            Coverage = coverage;
            this.observations = CopyObservations(observations);
            this.descriptions = CopyDescriptions(descriptions);
            DroppedObservationCount = droppedObservationCount;
            DroppedDescriptionCount = droppedDescriptionCount;

            if (this.descriptions.Count == 0)
            {
                Description = null;
                DescriptionProviderInstanceId = null;
                DescriptionAgeNanoseconds = null;
            }
            else
            {
                WorldDescriptionSnapshot latest =
                    this.descriptions[this.descriptions.Count - 1];
                Description = latest.Text;
                DescriptionProviderInstanceId = latest.ProviderInstanceId;
                DescriptionAgeNanoseconds = checked(
                    snapshotTimestampNanoseconds -
                    latest.LastConfirmedTimestampNanoseconds);
            }
        }

        public string EntityId { get; }

        public string TrackingLocalId { get; }

        public string Classification { get; }

        public string TrackingProviderInstanceId { get; }

        public ReachyVisionFrameIdentity LatestFrameIdentity { get; }

        public WorldEntityVisibility Visibility { get; }

        public bool IsCurrentlyVisible =>
            Visibility == WorldEntityVisibility.CurrentlyVisible;

        public long FirstSeenTimestampNanoseconds { get; }

        public long LastSeenTimestampNanoseconds { get; }

        public double Confidence { get; }

        public NormalizedVisionBounds Bounds { get; }

        public WorldPositionEstimate Position { get; }

        public WorldDirectionEstimate Direction { get; }

        public WorldCoverageContext Coverage { get; }

        public string? Description { get; }

        public string? DescriptionProviderInstanceId { get; }

        public long? DescriptionAgeNanoseconds { get; }

        public IReadOnlyList<WorldObservationSnapshot> Observations =>
            observations;

        public IReadOnlyList<WorldDescriptionSnapshot> Descriptions =>
            descriptions;

        public long DroppedObservationCount { get; }

        public long DroppedDescriptionCount { get; }

        private static ReadOnlyCollection<WorldObservationSnapshot>
            CopyObservations(
                IReadOnlyList<WorldObservationSnapshot> source)
        {
            var copy = new List<WorldObservationSnapshot>(source.Count);
            for (int index = 0; index < source.Count; ++index)
            {
                copy.Add(source[index]);
            }
            return copy.AsReadOnly();
        }

        private static ReadOnlyCollection<WorldDescriptionSnapshot>
            CopyDescriptions(
                IReadOnlyList<WorldDescriptionSnapshot> source)
        {
            var copy = new List<WorldDescriptionSnapshot>(source.Count);
            for (int index = 0; index < source.Count; ++index)
            {
                copy.Add(source[index]);
            }
            return copy.AsReadOnly();
        }
    }

    public sealed class WorldModelDiagnosticsSnapshot
    {
        internal WorldModelDiagnosticsSnapshot(
            long acceptedTrackingBatchCount,
            long duplicateTrackingBatchCount,
            long staleTrackingBatchCount,
            long invalidCoverageBatchCount,
            long capacityRejectedBatchCount,
            long classificationConflictCount,
            long acceptedDescriptionCount,
            long duplicateDescriptionCount,
            long rejectedDescriptionCount,
            long expiredEntityCount,
            int activeScopeCursorCount,
            long droppedScopeCursorCount)
        {
            AcceptedTrackingBatchCount = acceptedTrackingBatchCount;
            DuplicateTrackingBatchCount = duplicateTrackingBatchCount;
            StaleTrackingBatchCount = staleTrackingBatchCount;
            InvalidCoverageBatchCount = invalidCoverageBatchCount;
            CapacityRejectedBatchCount = capacityRejectedBatchCount;
            ClassificationConflictCount = classificationConflictCount;
            AcceptedDescriptionCount = acceptedDescriptionCount;
            DuplicateDescriptionCount = duplicateDescriptionCount;
            RejectedDescriptionCount = rejectedDescriptionCount;
            ExpiredEntityCount = expiredEntityCount;
            ActiveScopeCursorCount = activeScopeCursorCount;
            DroppedScopeCursorCount = droppedScopeCursorCount;
        }

        public long AcceptedTrackingBatchCount { get; }

        public long DuplicateTrackingBatchCount { get; }

        public long StaleTrackingBatchCount { get; }

        public long InvalidCoverageBatchCount { get; }

        public long CapacityRejectedBatchCount { get; }

        public long ClassificationConflictCount { get; }

        public long AcceptedDescriptionCount { get; }

        public long DuplicateDescriptionCount { get; }

        public long RejectedDescriptionCount { get; }

        public long ExpiredEntityCount { get; }

        public int ActiveScopeCursorCount { get; }

        public long DroppedScopeCursorCount { get; }
    }

    public sealed class WorldModelSnapshot
    {
        private readonly ReadOnlyCollection<WorldEntitySnapshot> entities;
        private readonly ReadOnlyCollection<WorldEntitySnapshot>
            currentlyVisibleEntities;
        private readonly ReadOnlyCollection<WorldEntitySnapshot>
            recentlySeenEntities;

        internal WorldModelSnapshot(
            long timestampNanoseconds,
            IReadOnlyList<WorldEntitySnapshot> entities,
            WorldModelDiagnosticsSnapshot diagnostics)
        {
            TimestampNanoseconds = timestampNanoseconds;
            this.entities = Copy(entities);

            var visible = new List<WorldEntitySnapshot>();
            var recent = new List<WorldEntitySnapshot>();
            for (int index = 0; index < this.entities.Count; ++index)
            {
                WorldEntitySnapshot entity = this.entities[index];
                if (entity.IsCurrentlyVisible)
                {
                    visible.Add(entity);
                }
                else
                {
                    recent.Add(entity);
                }
            }

            currentlyVisibleEntities = visible.AsReadOnly();
            recentlySeenEntities = recent.AsReadOnly();
            Diagnostics = diagnostics;
        }

        public long TimestampNanoseconds { get; }

        public IReadOnlyList<WorldEntitySnapshot> Entities => entities;

        public IReadOnlyList<WorldEntitySnapshot> CurrentlyVisibleEntities =>
            currentlyVisibleEntities;

        public IReadOnlyList<WorldEntitySnapshot> RecentlySeenEntities =>
            recentlySeenEntities;

        public WorldModelDiagnosticsSnapshot Diagnostics { get; }

        private static ReadOnlyCollection<WorldEntitySnapshot> Copy(
            IReadOnlyList<WorldEntitySnapshot> source)
        {
            var copy = new List<WorldEntitySnapshot>(source.Count);
            for (int index = 0; index < source.Count; ++index)
            {
                copy.Add(source[index]);
            }
            return copy.AsReadOnly();
        }
    }

    public sealed class WorldModelUpdateResult
    {
        internal WorldModelUpdateResult(
            WorldModelUpdateStatus status,
            string diagnostic,
            WorldModelSnapshot snapshot)
        {
            Status = status;
            Diagnostic = diagnostic;
            Snapshot = snapshot;
        }

        public WorldModelUpdateStatus Status { get; }

        public string Diagnostic { get; }

        public WorldModelSnapshot Snapshot { get; }

        public bool Accepted =>
            Status == WorldModelUpdateStatus.Accepted ||
            Status == WorldModelUpdateStatus.DescriptionAccepted ||
            Status == WorldModelUpdateStatus.DescriptionDuplicate ||
            Status == WorldModelUpdateStatus.DuplicateIgnored;
    }
}

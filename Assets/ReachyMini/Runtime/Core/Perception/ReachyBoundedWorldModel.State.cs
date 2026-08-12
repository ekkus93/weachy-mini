#nullable enable

using System.Collections.Generic;

namespace ReachyMini.Perception
{
    public sealed partial class BoundedWorldModel
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
    }
}

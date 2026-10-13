#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Performance;

namespace ReachyMini.Perception
{
    public sealed class ReachyStableTrackStore
    {
        private readonly object sync = new object();
        private readonly ReachyStableTrackingPolicy policy;
        private readonly List<Track> tracks = new List<Track>();
        private ulong nextFaceId = 1UL;
        private ulong nextPersonId = 1UL;
        private ReachyVisionFrameIdentity? latestIdentity;

        public ReachyStableTrackStore(
            ReachyStableTrackingPolicy? policy = null)
        {
            this.policy =
                policy ?? ReachyStableTrackingPolicy.MobileBaseline;
        }

        public IReadOnlyList<TrackedObject> Update(
            ReachyVisionFrameIdentity identity,
            IReadOnlyList<ReachyTrackingDetection> detections)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            if (detections == null)
            {
                throw new ArgumentNullException(nameof(detections));
            }

            lock (sync)
            {
                ValidateOrderingOrReset(identity);
                ExpireBeforeAssociation(identity.SourceTimestampNanoseconds);
                for (int index = 0; index < tracks.Count; ++index)
                {
                    tracks[index].MissedFrames = checked(
                        tracks[index].MissedFrames + 1);
                }

                var ordered = detections
                    .Select((detection, index) =>
                        new IndexedDetection(detection, index))
                    .OrderBy(item => item.Detection.DetectionClass)
                    .ThenBy(item => item.Detection.Bounds.Left)
                    .ThenBy(item => item.Detection.Bounds.Top)
                    .ThenBy(item => item.OriginalIndex)
                    .ToList();
                var assigned = new HashSet<string>(StringComparer.Ordinal);
                var output = new List<TrackedObject>(ordered.Count);

                for (int index = 0; index < ordered.Count; ++index)
                {
                    ReachyTrackingDetection detection =
                        ordered[index].Detection;
                    Track? track = FindBestTrack(detection, assigned);
                    if (track == null)
                    {
                        track = CreateTrack(detection, identity);
                        tracks.Add(track);
                    }
                    else
                    {
                        track.Bounds = detection.Bounds;
                        track.Confidence = detection.Confidence;
                        track.ProviderTrackingId =
                            detection.ProviderTrackingId;
                        track.LastSeenTimestampNanoseconds =
                            identity.SourceTimestampNanoseconds;
                        track.LastSeenSequence = identity.SourceSequence;
                        track.MissedFrames = 0;
                    }
                    assigned.Add(track.LocalId);
                    output.Add(new TrackedObject(
                        track.LocalId,
                        detection.Classification,
                        detection.Confidence,
                        detection.Bounds));
                }

                tracks.RemoveAll(track =>
                    track.MissedFrames > policy.MaximumMissedFrames);
                latestIdentity = identity;
                output.Sort((left, right) =>
                    string.CompareOrdinal(left.LocalId, right.LocalId));
                return new ReadOnlyCollection<TrackedObject>(output);
            }
        }

        public void Reset()
        {
            lock (sync)
            {
                tracks.Clear();
                latestIdentity = null;
            }
        }

        private void ValidateOrderingOrReset(
            ReachyVisionFrameIdentity identity)
        {
            ReachyVisionFrameIdentity? latest = latestIdentity;
            if (latest == null)
            {
                return;
            }
            bool newContinuity =
                latest.SourceSessionId != identity.SourceSessionId ||
                latest.ContinuityId != identity.ContinuityId ||
                !string.Equals(
                    latest.CameraId,
                    identity.CameraId,
                    StringComparison.Ordinal);
            if (newContinuity)
            {
                tracks.Clear();
                latestIdentity = null;
                return;
            }
            if (identity.SourceSequence <= latest.SourceSequence ||
                identity.SourceTimestampNanoseconds <=
                    latest.SourceTimestampNanoseconds ||
                identity.AuthoritativeSequence <
                    latest.AuthoritativeSequence)
            {
                throw new InvalidOperationException(
                    "Tracking frames must be strictly monotonic within a camera session and simulation continuity.");
            }
        }

        private void ExpireBeforeAssociation(long timestampNanoseconds)
        {
            long expiryNanoseconds = checked(
                (long)(policy.MaximumUnseenDuration.TotalSeconds *
                    1_000_000_000.0));
            tracks.RemoveAll(track =>
                timestampNanoseconds - track.LastSeenTimestampNanoseconds >
                    expiryNanoseconds);
        }

        private Track? FindBestTrack(
            ReachyTrackingDetection detection,
            HashSet<string> assigned)
        {
            Track? best = null;
            double bestScore = double.NegativeInfinity;
            for (int index = 0; index < tracks.Count; ++index)
            {
                Track candidate = tracks[index];
                if (assigned.Contains(candidate.LocalId) ||
                    candidate.DetectionClass != detection.DetectionClass)
                {
                    continue;
                }

                double centerDistance = CenterDistance(
                    candidate.Bounds,
                    detection.Bounds);
                bool providerIdMatch =
                    detection.ProviderTrackingId != null &&
                    candidate.ProviderTrackingId != null &&
                    string.Equals(
                        detection.ProviderTrackingId,
                        candidate.ProviderTrackingId,
                        StringComparison.Ordinal) &&
                    centerDistance <=
                        policy.MaximumProviderIdCenterDistance;
                double intersectionOverUnion = IntersectionOverUnion(
                    candidate.Bounds,
                    detection.Bounds);
                bool geometricMatch =
                    intersectionOverUnion >=
                        policy.MinimumIntersectionOverUnion ||
                    centerDistance <= policy.MaximumCenterDistance;
                if (!providerIdMatch && !geometricMatch)
                {
                    continue;
                }

                double score =
                    (providerIdMatch ? 2.0 : 0.0) +
                    intersectionOverUnion - centerDistance;
                if (best == null ||
                    score > bestScore ||
                    (score == bestScore &&
                     string.CompareOrdinal(
                         candidate.LocalId,
                         best.LocalId) < 0))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private Track CreateTrack(
            ReachyTrackingDetection detection,
            ReachyVisionFrameIdentity identity)
        {
            ulong sequence;
            string prefix;
            if (detection.DetectionClass ==
                ReachyTrackingDetectionClass.Face)
            {
                sequence = nextFaceId++;
                prefix = "face";
            }
            else
            {
                sequence = nextPersonId++;
                prefix = "person";
            }
            return new Track(
                $"{prefix}-{sequence:D6}",
                detection.DetectionClass,
                detection.ProviderTrackingId,
                detection.Confidence,
                detection.Bounds,
                identity.SourceSequence,
                identity.SourceTimestampNanoseconds);
        }

        private static double IntersectionOverUnion(
            NormalizedVisionBounds left,
            NormalizedVisionBounds right)
        {
            double intersectionLeft = Math.Max(left.Left, right.Left);
            double intersectionTop = Math.Max(left.Top, right.Top);
            double intersectionRight = Math.Min(
                left.Left + left.Width,
                right.Left + right.Width);
            double intersectionBottom = Math.Min(
                left.Top + left.Height,
                right.Top + right.Height);
            double intersectionWidth = Math.Max(
                0.0,
                intersectionRight - intersectionLeft);
            double intersectionHeight = Math.Max(
                0.0,
                intersectionBottom - intersectionTop);
            double intersection =
                intersectionWidth * intersectionHeight;
            double union =
                (left.Width * left.Height) +
                (right.Width * right.Height) -
                intersection;
            return union <= 0.0 ? 0.0 : intersection / union;
        }

        private static double CenterDistance(
            NormalizedVisionBounds left,
            NormalizedVisionBounds right)
        {
            double x = left.CenterX - right.CenterX;
            double y = left.CenterY - right.CenterY;
            return Math.Sqrt((x * x) + (y * y));
        }

        private sealed class Track
        {
            public Track(
                string localId,
                ReachyTrackingDetectionClass detectionClass,
                string? providerTrackingId,
                double confidence,
                NormalizedVisionBounds bounds,
                ulong lastSeenSequence,
                long lastSeenTimestampNanoseconds)
            {
                LocalId = localId;
                DetectionClass = detectionClass;
                ProviderTrackingId = providerTrackingId;
                Confidence = confidence;
                Bounds = bounds;
                LastSeenSequence = lastSeenSequence;
                LastSeenTimestampNanoseconds =
                    lastSeenTimestampNanoseconds;
            }

            public string LocalId { get; }

            public ReachyTrackingDetectionClass DetectionClass { get; }

            public string? ProviderTrackingId { get; set; }

            public double Confidence { get; set; }

            public NormalizedVisionBounds Bounds { get; set; }

            public ulong LastSeenSequence { get; set; }

            public long LastSeenTimestampNanoseconds { get; set; }

            public int MissedFrames { get; set; }
        }

        private sealed class IndexedDetection
        {
            public IndexedDetection(
                ReachyTrackingDetection detection,
                int originalIndex)
            {
                Detection = detection ??
                    throw new ArgumentNullException(nameof(detection));
                OriginalIndex = originalIndex;
            }

            public ReachyTrackingDetection Detection { get; }

            public int OriginalIndex { get; }
        }
    }

}

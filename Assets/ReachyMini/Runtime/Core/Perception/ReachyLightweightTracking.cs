#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public enum ReachyTrackingDetectionClass
    {
        Face = 0,
        Person = 1,
    }

    public sealed class ReachyTrackingDetection
    {
        public ReachyTrackingDetection(
            ReachyTrackingDetectionClass detectionClass,
            string? providerTrackingId,
            double confidence,
            NormalizedVisionBounds bounds)
        {
            if (!Enum.IsDefined(
                    typeof(ReachyTrackingDetectionClass),
                    detectionClass))
            {
                throw new ArgumentOutOfRangeException(nameof(detectionClass));
            }
            if (providerTrackingId != null &&
                string.IsNullOrWhiteSpace(providerTrackingId))
            {
                throw new ArgumentException(
                    "Provider tracking identifiers must be null or non-empty.",
                    nameof(providerTrackingId));
            }
            if (double.IsNaN(confidence) ||
                double.IsInfinity(confidence) ||
                confidence < 0.0 ||
                confidence > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(confidence));
            }

            DetectionClass = detectionClass;
            ProviderTrackingId = providerTrackingId;
            Confidence = confidence;
            Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        }

        public ReachyTrackingDetectionClass DetectionClass { get; }

        public string? ProviderTrackingId { get; }

        public double Confidence { get; }

        public NormalizedVisionBounds Bounds { get; }

        public string Classification =>
            DetectionClass == ReachyTrackingDetectionClass.Face
                ? "face"
                : "person";
    }

    public sealed class ReachyTrackingFramePixels
    {
        private readonly byte[] rgbaTopLeft;
        private readonly byte[] validityTopLeft;

        public ReachyTrackingFramePixels(
            ReachyVisionFrameIdentity identity,
            int width,
            int height,
            byte[] rgbaTopLeft,
            byte[] validityTopLeft)
        {
            Identity = identity ??
                throw new ArgumentNullException(nameof(identity));
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            if (rgbaTopLeft == null)
            {
                throw new ArgumentNullException(nameof(rgbaTopLeft));
            }
            if (validityTopLeft == null)
            {
                throw new ArgumentNullException(nameof(validityTopLeft));
            }

            int pixelCount = checked(width * height);
            int expectedColorLength = checked(pixelCount * 4);
            if (rgbaTopLeft.Length != expectedColorLength)
            {
                throw new ArgumentException(
                    $"Tracking RGBA length must be {expectedColorLength} bytes.",
                    nameof(rgbaTopLeft));
            }
            if (validityTopLeft.Length != pixelCount)
            {
                throw new ArgumentException(
                    $"Tracking validity length must be {pixelCount} bytes.",
                    nameof(validityTopLeft));
            }

            Width = width;
            Height = height;
            this.rgbaTopLeft = (byte[])rgbaTopLeft.Clone();
            this.validityTopLeft = (byte[])validityTopLeft.Clone();
        }

        public ReachyVisionFrameIdentity Identity { get; }

        public int Width { get; }

        public int Height { get; }

        public ReadOnlyMemory<byte> RgbaTopLeft => rgbaTopLeft;

        public ReadOnlyMemory<byte> ValidityTopLeft => validityTopLeft;

        public byte[] CopyRgbaTopLeft()
        {
            return (byte[])rgbaTopLeft.Clone();
        }

        public bool IsDetectionCenterValid(
            NormalizedVisionBounds bounds,
            byte minimumValidity = 128)
        {
            if (bounds == null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }

            int x = Math.Min(
                Width - 1,
                Math.Max(0, (int)Math.Floor(bounds.CenterX * Width)));
            int y = Math.Min(
                Height - 1,
                Math.Max(0, (int)Math.Floor(bounds.CenterY * Height)));
            return validityTopLeft[checked(y * Width + x)] >=
                minimumValidity;
        }
    }

    public interface IReachyTrackingPixelSource
    {
        ValueTask<ReachyTrackingFramePixels> StageAsync(
            ReachyVisionFrame frame,
            int maximumDimension,
            CancellationToken cancellationToken);
    }

    public interface IReachyTrackingDetectionBackend : IAsyncDisposable
    {
        string BackendId { get; }

        string BackendVersion { get; }

        bool SupportsFaces { get; }

        bool SupportsPeople { get; }

        ValueTask<IReadOnlyList<ReachyTrackingDetection>> DetectAsync(
            ReachyTrackingFramePixels pixels,
            CancellationToken cancellationToken);
    }

    public sealed class ReachyStableTrackingPolicy
    {
        public static ReachyStableTrackingPolicy MobileBaseline { get; } =
            new ReachyStableTrackingPolicy(
                minimumIntersectionOverUnion: 0.20,
                maximumCenterDistance: 0.18,
                maximumProviderIdCenterDistance: 0.45,
                maximumMissedFrames: 4,
                maximumUnseenDuration: TimeSpan.FromSeconds(1.5));

        public ReachyStableTrackingPolicy(
            double minimumIntersectionOverUnion,
            double maximumCenterDistance,
            double maximumProviderIdCenterDistance,
            int maximumMissedFrames,
            TimeSpan maximumUnseenDuration)
        {
            MinimumIntersectionOverUnion = RequireUnit(
                minimumIntersectionOverUnion,
                nameof(minimumIntersectionOverUnion));
            MaximumCenterDistance = RequireUnit(
                maximumCenterDistance,
                nameof(maximumCenterDistance));
            MaximumProviderIdCenterDistance = RequireUnit(
                maximumProviderIdCenterDistance,
                nameof(maximumProviderIdCenterDistance));
            if (maximumProviderIdCenterDistance < maximumCenterDistance)
            {
                throw new ArgumentException(
                    "Provider-ID association distance must be at least the geometric association distance.",
                    nameof(maximumProviderIdCenterDistance));
            }
            if (maximumMissedFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMissedFrames));
            }
            if (maximumUnseenDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumUnseenDuration));
            }

            MaximumMissedFrames = maximumMissedFrames;
            MaximumUnseenDuration = maximumUnseenDuration;
        }

        public double MinimumIntersectionOverUnion { get; }

        public double MaximumCenterDistance { get; }

        public double MaximumProviderIdCenterDistance { get; }

        public int MaximumMissedFrames { get; }

        public TimeSpan MaximumUnseenDuration { get; }

        private static double RequireUnit(double value, string name)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0.0 ||
                value > 1.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            return value;
        }
    }

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

    public sealed class ReachyOnDeviceLightweightTracker : IVisualTracker
    {
        public const int DefaultMaximumTrackingDimension = 640;

        private readonly IReachyTrackingDetectionBackend backend;
        private readonly ReachyStableTrackStore trackStore;
        private readonly int maximumTrackingDimension;
        private int inFlight;
        private int disposed;

        public ReachyOnDeviceLightweightTracker(
            string instanceId,
            IReachyTrackingDetectionBackend backend,
            ReachyStableTrackingPolicy? trackingPolicy = null,
            int maximumTrackingDimension =
                DefaultMaximumTrackingDimension)
        {
            if (backend == null)
            {
                throw new ArgumentNullException(nameof(backend));
            }
            if (!backend.SupportsFaces && !backend.SupportsPeople)
            {
                throw new ArgumentException(
                    "The lightweight tracker backend must support faces or people.",
                    nameof(backend));
            }
            if (maximumTrackingDimension < 64 ||
                maximumTrackingDimension > 2048)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTrackingDimension));
            }

            this.backend = backend;
            this.maximumTrackingDimension =
                maximumTrackingDimension;
            trackStore = new ReachyStableTrackStore(trackingPolicy);
            Descriptor = new ProviderDescriptor(
                VisionProviderKind.LightweightTracker,
                "weachy.mlkit.lightweight-tracker",
                instanceId,
                "On-device lightweight tracker",
                backend.BackendVersion,
                VisionProviderLocation.OnDevice);
            Capabilities = new TrackerCapabilities(
                supportsFaces: backend.SupportsFaces,
                supportsPeople: backend.SupportsPeople,
                supportsObjects: false,
                supportsMotion: false,
                consumesGpuFrames: true,
                supportsCancellation: true,
                maximumConcurrentOperations: 1);
        }

        public ProviderDescriptor Descriptor { get; }

        public TrackerCapabilities Capabilities { get; }

        public async ValueTask<TrackingResult> AnalyzeAsync(
            TrackingRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Unavailable,
                    Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The on-device lightweight tracker is busy; requests are not queued or retried.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.Frame.IsObservationEligible)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.InvalidFrame,
                        Descriptor,
                        request,
                        requiresProviderReset: false,
                        "Tracking requires an observation-eligible transformed Reachy-eye frame.");
                }
                if (!request.Frame.Resources.TryGetResource<IReachyTrackingPixelSource>(
                        VisionResourceKind.Color,
                        out IReachyTrackingPixelSource? pixelSource) ||
                    pixelSource == null)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.Unavailable,
                        Descriptor,
                        request,
                        requiresProviderReset: false,
                        "The transformed frame has no bounded tracking pixel source.");
                }

                ReachyTrackingFramePixels pixels =
                    await pixelSource.StageAsync(
                        request.Frame,
                        maximumTrackingDimension,
                        cancellationToken).ConfigureAwait(false);
                if (!pixels.Identity.Matches(request.Frame.Identity))
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.ContractViolation,
                        Descriptor,
                        request,
                        requiresProviderReset: true,
                        "Tracking pixel staging changed frame identity.");
                }

                IReadOnlyList<ReachyTrackingDetection> detections =
                    await backend.DetectAsync(
                        pixels,
                        cancellationToken).ConfigureAwait(false);
                if (detections == null)
                {
                    return TrackingResult.Failure(
                        VisionOperationStatus.ContractViolation,
                        Descriptor,
                        request,
                        requiresProviderReset: true,
                        "The on-device tracking backend returned a null detection list.");
                }

                var accepted = new List<ReachyTrackingDetection>(
                    detections.Count);
                for (int index = 0; index < detections.Count; ++index)
                {
                    ReachyTrackingDetection detection =
                        detections[index] ??
                        throw new InvalidOperationException(
                            "The on-device tracking backend returned a null detection.");
                    bool supported =
                        (detection.DetectionClass ==
                            ReachyTrackingDetectionClass.Face &&
                         Capabilities.SupportsFaces) ||
                        (detection.DetectionClass ==
                            ReachyTrackingDetectionClass.Person &&
                         Capabilities.SupportsPeople);
                    if (!supported)
                    {
                        return TrackingResult.Failure(
                            VisionOperationStatus.ContractViolation,
                            Descriptor,
                            request,
                            requiresProviderReset: true,
                            "The tracking backend returned an undeclared detection class.");
                    }
                    if (pixels.IsDetectionCenterValid(detection.Bounds))
                    {
                        accepted.Add(detection);
                    }
                }

                IReadOnlyList<TrackedObject> tracked =
                    trackStore.Update(request.Frame.Identity, accepted);
                return TrackingResult.Success(
                    Descriptor,
                    request,
                    tracked);
            }
            catch (OperationCanceledException)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.Cancelled,
                    Descriptor,
                    request,
                    requiresProviderReset: false,
                    "The on-device lightweight tracking request was cancelled.");
            }
            catch (Exception exception)
            {
                return TrackingResult.Failure(
                    VisionOperationStatus.ProviderFailure,
                    Descriptor,
                    request,
                    requiresProviderReset: true,
                    $"On-device lightweight tracking failed closed ({exception.GetType().Name}): {exception.Message}");
            }
            finally
            {
                Volatile.Write(ref inFlight, 0);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            trackStore.Reset();
            await backend.DisposeAsync().ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyOnDeviceLightweightTracker));
            }
        }
    }
}

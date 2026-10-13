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

}

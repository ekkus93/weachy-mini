#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class RemoteVlmImageDimensions
    {
        public RemoteVlmImageDimensions(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }
    }

    public sealed class RemoteVlmImagePolicy
    {
        public RemoteVlmImagePolicy(
            int maximumWidth,
            int maximumHeight,
            int maximumEncodedBytes,
            RemoteVlmImageFormat preferredFormat,
            int lossyQuality,
            RemoteVlmImageDetail detail,
            RemoteVlmInvalidPixelPolicy invalidPixelPolicy,
            bool allowUpscaling)
        {
            if (maximumWidth <= 0 || maximumWidth > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumWidth));
            }
            if (maximumHeight <= 0 || maximumHeight > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHeight));
            }
            if (maximumEncodedBytes <= 0 || maximumEncodedBytes > 32 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
            }
            if (!Enum.IsDefined(typeof(RemoteVlmImageFormat), preferredFormat))
            {
                throw new ArgumentOutOfRangeException(nameof(preferredFormat));
            }
            if (lossyQuality < 1 || lossyQuality > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(lossyQuality));
            }
            if (!Enum.IsDefined(typeof(RemoteVlmImageDetail), detail))
            {
                throw new ArgumentOutOfRangeException(nameof(detail));
            }
            if (!Enum.IsDefined(
                    typeof(RemoteVlmInvalidPixelPolicy),
                    invalidPixelPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(invalidPixelPolicy));
            }
            if (allowUpscaling)
            {
                throw new ArgumentException(
                    "RMA-115 remote VLM image policy does not permit upscaling.",
                    nameof(allowUpscaling));
            }

            MaximumWidth = maximumWidth;
            MaximumHeight = maximumHeight;
            MaximumEncodedBytes = maximumEncodedBytes;
            PreferredFormat = preferredFormat;
            LossyQuality = lossyQuality;
            Detail = detail;
            InvalidPixelPolicy = invalidPixelPolicy;
            AllowUpscaling = false;
        }

        public int MaximumWidth { get; }

        public int MaximumHeight { get; }

        public int MaximumEncodedBytes { get; }

        public RemoteVlmImageFormat PreferredFormat { get; }

        public int LossyQuality { get; }

        public RemoteVlmImageDetail Detail { get; }

        public RemoteVlmInvalidPixelPolicy InvalidPixelPolicy { get; }

        public bool AllowUpscaling { get; }

        public static RemoteVlmImagePolicy CreateDefault()
        {
            return new RemoteVlmImagePolicy(
                maximumWidth: 1024,
                maximumHeight: 1024,
                maximumEncodedBytes: 4 * 1024 * 1024,
                preferredFormat: RemoteVlmImageFormat.Jpeg,
                lossyQuality: 85,
                detail: RemoteVlmImageDetail.Auto,
                invalidPixelPolicy:
                    RemoteVlmInvalidPixelPolicy.ReplaceWithOpaqueBlack,
                allowUpscaling: false);
        }

        public RemoteVlmImageDimensions ComputeTargetDimensions(
            int sourceWidth,
            int sourceHeight)
        {
            if (sourceWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            }
            if (sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));
            }

            double widthScale = (double)MaximumWidth / sourceWidth;
            double heightScale = (double)MaximumHeight / sourceHeight;
            double scale = Math.Min(1.0, Math.Min(widthScale, heightScale));
            int width = Math.Max(
                1,
                (int)Math.Floor(sourceWidth * scale));
            int height = Math.Max(
                1,
                (int)Math.Floor(sourceHeight * scale));
            return new RemoteVlmImageDimensions(width, height);
        }
    }
}

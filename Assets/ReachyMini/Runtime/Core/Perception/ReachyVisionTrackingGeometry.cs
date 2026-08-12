#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class NormalizedVisionBounds
    {
        public NormalizedVisionBounds(
            double left,
            double top,
            double width,
            double height)
        {
            RequireUnit(left, nameof(left));
            RequireUnit(top, nameof(top));
            if (double.IsNaN(width) ||
                double.IsInfinity(width) ||
                width <= 0.0 ||
                width > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (double.IsNaN(height) ||
                double.IsInfinity(height) ||
                height <= 0.0 ||
                height > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            if (left + width > 1.0 || top + height > 1.0)
            {
                throw new ArgumentException(
                    "Normalized bounds must remain inside the frame.");
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public double Left { get; }

        public double Top { get; }

        public double Width { get; }

        public double Height { get; }

        public double CenterX => Left + (Width * 0.5);

        public double CenterY => Top + (Height * 0.5);

        private static void RequireUnit(double value, string name)
        {
            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < 0.0 ||
                value > 1.0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public sealed class TrackedObject
    {
        public TrackedObject(
            string localId,
            string classification,
            double confidence,
            NormalizedVisionBounds bounds)
        {
            LocalId = ProviderDescriptor.RequireText(
                localId,
                nameof(localId));
            Classification = ProviderDescriptor.RequireText(
                classification,
                nameof(classification));
            if (double.IsNaN(confidence) ||
                double.IsInfinity(confidence) ||
                confidence < 0.0 ||
                confidence > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(confidence));
            }
            Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
            Confidence = confidence;
        }

        public string LocalId { get; }

        public string Classification { get; }

        public double Confidence { get; }

        public NormalizedVisionBounds Bounds { get; }
    }
}

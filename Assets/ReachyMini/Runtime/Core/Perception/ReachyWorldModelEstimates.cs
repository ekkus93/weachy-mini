#nullable enable

using System;

namespace ReachyMini.Perception
{
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
}

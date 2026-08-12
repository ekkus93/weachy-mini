#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed class ReachyCameraIntrinsicMatrix
    {
        public ReachyCameraIntrinsicMatrix(
            int imageWidth,
            int imageHeight,
            ReachyMatrix3x3 pixelFromOpticalRay)
        {
            if (imageWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageWidth),
                    imageWidth,
                    "An intrinsic image width must be positive.");
            }
            if (imageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageHeight),
                    imageHeight,
                    "An intrinsic image height must be positive.");
            }
            if (Math.Abs(pixelFromOpticalRay.M20) > 1.0e-12 ||
                Math.Abs(pixelFromOpticalRay.M21) > 1.0e-12 ||
                Math.Abs(pixelFromOpticalRay.M22 - 1.0) > 1.0e-12)
            {
                throw new ArgumentException(
                    "A camera intrinsic matrix must retain affine pixel projection in its final row.",
                    nameof(pixelFromOpticalRay));
            }
            if (Math.Abs(pixelFromOpticalRay.Determinant) <= 1.0e-12)
            {
                throw new ArgumentException(
                    "A camera intrinsic matrix must be invertible.",
                    nameof(pixelFromOpticalRay));
            }

            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            PixelFromOpticalRay = pixelFromOpticalRay;
        }

        public int ImageWidth { get; }

        public int ImageHeight { get; }

        public ReachyMatrix3x3 PixelFromOpticalRay { get; }

        public ReachyMatrix3x3 OpticalRayFromPixel =>
            PixelFromOpticalRay.Inverse();

        public static ReachyCameraIntrinsicMatrix CreatePinhole(
            int imageWidth,
            int imageHeight,
            double focalLengthX,
            double focalLengthY,
            double principalPointX,
            double principalPointY,
            double skew = 0.0)
        {
            RequireFinitePositive(focalLengthX, nameof(focalLengthX));
            RequireFinitePositive(focalLengthY, nameof(focalLengthY));
            RequireFinite(principalPointX, nameof(principalPointX));
            RequireFinite(principalPointY, nameof(principalPointY));
            RequireFinite(skew, nameof(skew));
            return new ReachyCameraIntrinsicMatrix(
                imageWidth,
                imageHeight,
                new ReachyMatrix3x3(
                    focalLengthX,
                    skew,
                    principalPointX,
                    0.0,
                    focalLengthY,
                    principalPointY,
                    0.0,
                    0.0,
                    1.0));
        }

        public ReachyCameraIntrinsicMatrix TransformPixels(
            ReachyMatrix3x3 normalizedFromCurrentPixels,
            int outputWidth,
            int outputHeight)
        {
            return new ReachyCameraIntrinsicMatrix(
                outputWidth,
                outputHeight,
                normalizedFromCurrentPixels * PixelFromOpticalRay);
        }

        public ReachyVector3D GetOpticalRay(double pixelX, double pixelY)
        {
            return OpticalRayFromPixel.Transform(
                new ReachyVector3D(pixelX, pixelY, 1.0)).Normalized();
        }

        public ReachyVector3D ProjectOpticalRay(ReachyVector3D ray)
        {
            if (ray.Z <= 1.0e-12)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ray),
                    "Only rays in front of the camera can be projected.");
            }
            return PixelFromOpticalRay.TransformPixel(
                ray.X / ray.Z,
                ray.Y / ray.Z);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera intrinsic values must be finite.");
            }
        }

        private static void RequireFinitePositive(double value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera focal lengths must be positive.");
            }
        }
    }

    public sealed class ReachyCameraImageNormalization
    {
        public ReachyCameraImageNormalization(
            int sourceWidth,
            int sourceHeight,
            int cropLeft,
            int cropTop,
            int cropWidth,
            int cropHeight,
            int clockwiseRotationDegrees,
            bool mirrorHorizontally)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceWidth),
                    "Camera source dimensions must be positive.");
            }
            if (cropLeft < 0 || cropTop < 0 ||
                cropWidth <= 0 || cropHeight <= 0 ||
                cropLeft + cropWidth > sourceWidth ||
                cropTop + cropHeight > sourceHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cropLeft),
                    "The camera crop must have positive extent and remain inside the source image.");
            }
            ValidateRightAngle(clockwiseRotationDegrees);

            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            CropLeft = cropLeft;
            CropTop = cropTop;
            CropWidth = cropWidth;
            CropHeight = cropHeight;
            ClockwiseRotationDegrees = clockwiseRotationDegrees;
            MirrorHorizontally = mirrorHorizontally;

            OutputWidth = clockwiseRotationDegrees == 90 ||
                clockwiseRotationDegrees == 270
                    ? cropHeight
                    : cropWidth;
            OutputHeight = clockwiseRotationDegrees == 90 ||
                clockwiseRotationDegrees == 270
                    ? cropWidth
                    : cropHeight;

            ReachyMatrix3x3 crop = new ReachyMatrix3x3(
                1.0,
                0.0,
                -cropLeft,
                0.0,
                1.0,
                -cropTop,
                0.0,
                0.0,
                1.0);
            ReachyMatrix3x3 rotation = CreateRotation(
                cropWidth,
                cropHeight,
                clockwiseRotationDegrees);
            ReachyMatrix3x3 mirror = mirrorHorizontally
                ? new ReachyMatrix3x3(
                    -1.0,
                    0.0,
                    OutputWidth - 1.0,
                    0.0,
                    1.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0)
                : ReachyMatrix3x3.Identity;
            NormalizedFromSourcePixels = mirror * rotation * crop;
            SourceFromNormalizedPixels = NormalizedFromSourcePixels.Inverse();
        }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int CropLeft { get; }

        public int CropTop { get; }

        public int CropWidth { get; }

        public int CropHeight { get; }

        public int ClockwiseRotationDegrees { get; }

        public bool MirrorHorizontally { get; }

        public int OutputWidth { get; }

        public int OutputHeight { get; }

        public ReachyMatrix3x3 NormalizedFromSourcePixels { get; }

        public ReachyMatrix3x3 SourceFromNormalizedPixels { get; }

        public ReachyCameraIntrinsicMatrix NormalizeIntrinsics(
            ReachyCameraIntrinsicMatrix sourceIntrinsics)
        {
            if (sourceIntrinsics == null)
            {
                throw new ArgumentNullException(nameof(sourceIntrinsics));
            }
            if (sourceIntrinsics.ImageWidth != SourceWidth ||
                sourceIntrinsics.ImageHeight != SourceHeight)
            {
                throw new ArgumentException(
                    "Source intrinsics must match the pre-crop camera buffer dimensions.",
                    nameof(sourceIntrinsics));
            }
            return sourceIntrinsics.TransformPixels(
                NormalizedFromSourcePixels,
                OutputWidth,
                OutputHeight);
        }

        private static ReachyMatrix3x3 CreateRotation(
            int width,
            int height,
            int clockwiseRotationDegrees)
        {
            return clockwiseRotationDegrees switch
            {
                0 => ReachyMatrix3x3.Identity,
                90 => new ReachyMatrix3x3(
                    0.0,
                    -1.0,
                    height - 1.0,
                    1.0,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    1.0),
                180 => new ReachyMatrix3x3(
                    -1.0,
                    0.0,
                    width - 1.0,
                    0.0,
                    -1.0,
                    height - 1.0,
                    0.0,
                    0.0,
                    1.0),
                270 => new ReachyMatrix3x3(
                    0.0,
                    1.0,
                    0.0,
                    -1.0,
                    0.0,
                    width - 1.0,
                    0.0,
                    0.0,
                    1.0),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(clockwiseRotationDegrees),
                    clockwiseRotationDegrees,
                    "Camera rotation must be a right angle."),
            };
        }

        private static void ValidateRightAngle(int value)
        {
            if (value != 0 && value != 90 && value != 180 && value != 270)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Camera rotation must be 0, 90, 180, or 270 degrees.");
            }
        }
    }
}

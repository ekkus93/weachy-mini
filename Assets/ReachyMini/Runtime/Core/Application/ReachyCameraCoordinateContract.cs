#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyCameraCalibrationProvenance
    {
        Unknown = 0,
        AndroidPlatformMetadata = 1,
        MeasuredCheckerboard = 2,
        UserSupplied = 3,
        UncalibratedEstimate = 4,
    }

    public enum ReachyCameraReprojectionMode
    {
        RotationOnly = 0,
    }

    public enum ReachyCameraCalibrationSelectionStatus
    {
        Missing = 0,
        CameraMismatch = 1,
        ImageSizeMismatch = 2,
        ModelMismatch = 3,
        ExactUncalibratedEstimate = 4,
        ExactCalibrated = 5,
    }

    public static class ReachyCameraCoordinateContract
    {
        public const string AndroidPixelAxes =
            "origin=upper-left; +u=right; +v=down";
        public const string PhoneOpticalAxes =
            "+X=image-right; +Y=image-down; +Z=forward";
        public const string ReachyOpticalAxes =
            "+X=image-right; +Y=image-down; +Z=forward";
        public const string UnityWorldAxes =
            "+X=right; +Y=up; +Z=forward";
        public const string MujocoWorldAxes =
            "+X=right; +Y=forward; +Z=up";

        // Column-vector basis change matching ReachyCoordinateConverter:
        // Unity(x, y, z) = MuJoCo(x, z, y).
        public static ReachyMatrix3x3 UnityWorldFromMujocoWorld =>
            new ReachyMatrix3x3(
                1.0, 0.0, 0.0,
                0.0, 0.0, 1.0,
                0.0, 1.0, 0.0);

        // Unity camera local coordinates use +Y up. The optical convention used
        // by calibration and homographies uses +Y down. Mirroring is not part
        // of this basis change; it remains an explicit pixel transform.
        public static ReachyMatrix3x3 OpticalFromUnityCamera =>
            new ReachyMatrix3x3(
                1.0, 0.0, 0.0,
                0.0, -1.0, 0.0,
                0.0, 0.0, 1.0);

        public static ReachyMatrix3x3 OpticalFromMujocoCamera =>
            OpticalFromUnityCamera * UnityWorldFromMujocoWorld;

        public static ReachyMatrix3x3 ConvertRotation(
            ReachyMatrix3x3 rotation,
            ReachyMatrix3x3 targetFromSourceBasis)
        {
            if (!rotation.IsProperRotation())
            {
                throw new ArgumentException(
                    "Only proper source rotations can cross coordinate systems.",
                    nameof(rotation));
            }
            ReachyMatrix3x3 converted = targetFromSourceBasis *
                rotation *
                targetFromSourceBasis.Inverse();
            if (!converted.IsProperRotation())
            {
                throw new InvalidOperationException(
                    "The coordinate basis conversion did not preserve a proper rotation.");
            }
            return converted;
        }
    }
}

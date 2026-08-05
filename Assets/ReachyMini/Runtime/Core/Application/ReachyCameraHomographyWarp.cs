#nullable enable

using System;

namespace ReachyMini.AppState
{
    public enum ReachyCameraHomographyBuildStatus
    {
        Success = 0,
        InvalidFrameIdentity = 1,
        CameraMismatch = 2,
        SourceSizeMismatch = 3,
        CalibrationModelMismatch = 4,
        TimestampMismatch = 5,
        InvalidRotation = 6,
        InvalidHomography = 7,
    }

    public sealed class ReachyCameraHomographyPlan
    {
        internal ReachyCameraHomographyPlan(
            ReachyCameraCalibrationProfile calibration,
            ReachyCameraRelativeRotationSample rotation,
            ulong sourceSessionId,
            ulong sourceSequence,
            long sourceTimestampNanoseconds,
            ReachyMatrix3x3 phoneToReachyPixels,
            ReachyMatrix3x3 reachyToPhonePixels)
        {
            CalibrationProfileId = calibration.ProfileId;
            CameraId = calibration.CameraId;
            Facing = calibration.Facing;
            ModelCompatibility = calibration.ModelCompatibility;
            SourceSessionId = sourceSessionId;
            SourceSequence = sourceSequence;
            SourceTimestampNanoseconds = sourceTimestampNanoseconds;
            SourceWidth = calibration.PhoneIntrinsics.ImageWidth;
            SourceHeight = calibration.PhoneIntrinsics.ImageHeight;
            OutputWidth = calibration.ReachyIntrinsics.ImageWidth;
            OutputHeight = calibration.ReachyIntrinsics.ImageHeight;
            ModelHash = rotation.ModelHash;
            AuthoritativeSequence = rotation.AuthoritativeSequence;
            SimulationTimeSeconds = rotation.SimulationTimeSeconds;
            ContinuityId = rotation.ContinuityId;
            PhoneOrientationTimestampNanoseconds =
                rotation.PhoneTimestampNanoseconds;
            CameraBodyId = rotation.CameraBodyId;
            PhoneToReachyPixels = phoneToReachyPixels;
            ReachyToPhonePixels = reachyToPhonePixels;
        }

        public string CalibrationProfileId { get; }

        public string CameraId { get; }

        public ReachyDeviceCameraFacing Facing { get; }

        public string ModelCompatibility { get; }

        public ulong SourceSessionId { get; }

        public ulong SourceSequence { get; }

        public long SourceTimestampNanoseconds { get; }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int OutputWidth { get; }

        public int OutputHeight { get; }

        public ulong ModelHash { get; }

        public ulong AuthoritativeSequence { get; }

        public double SimulationTimeSeconds { get; }

        public uint ContinuityId { get; }

        public long PhoneOrientationTimestampNanoseconds { get; }

        public uint CameraBodyId { get; }

        public ReachyMatrix3x3 PhoneToReachyPixels { get; }

        public ReachyMatrix3x3 ReachyToPhonePixels { get; }

        public bool TryMapReachyPixelToPhonePixel(
            double outputPixelX,
            double outputPixelY,
            out ReachyVector3D sourcePixel)
        {
            sourcePixel = default;
            if (!IsFinite(outputPixelX) ||
                !IsFinite(outputPixelY) ||
                outputPixelX < 0.0 ||
                outputPixelX > OutputWidth - 1.0 ||
                outputPixelY < 0.0 ||
                outputPixelY > OutputHeight - 1.0)
            {
                return false;
            }

            ReachyVector3D homogeneous = ReachyToPhonePixels.Transform(
                new ReachyVector3D(outputPixelX, outputPixelY, 1.0));
            if (homogeneous.Z <= 1.0e-9)
            {
                return false;
            }

            double sourceX = homogeneous.X / homogeneous.Z;
            double sourceY = homogeneous.Y / homogeneous.Z;
            if (!IsFinite(sourceX) ||
                !IsFinite(sourceY) ||
                sourceX < 0.0 ||
                sourceX > SourceWidth - 1.0 ||
                sourceY < 0.0 ||
                sourceY > SourceHeight - 1.0)
            {
                return false;
            }

            sourcePixel = new ReachyVector3D(sourceX, sourceY, 1.0);
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class ReachyCameraHomographyBuildResult
    {
        public ReachyCameraHomographyBuildResult(
            ReachyCameraHomographyBuildStatus status,
            ReachyCameraHomographyPlan? plan,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Homography construction requires diagnostics.",
                    nameof(message));
            }
            bool succeeded =
                status == ReachyCameraHomographyBuildStatus.Success;
            if (succeeded != (plan != null))
            {
                throw new ArgumentException(
                    "Homography status and plan disagree.",
                    nameof(plan));
            }

            Status = status;
            Plan = plan;
            Message = message;
        }

        public ReachyCameraHomographyBuildStatus Status { get; }

        public ReachyCameraHomographyPlan? Plan { get; }

        public string Message { get; }

        public bool Succeeded =>
            Status == ReachyCameraHomographyBuildStatus.Success;
    }

    public static class ReachyCameraHomographyCalculator
    {
        private const double NumericalCanonicalizationTolerance = 1.0e-12;

        public static ReachyCameraHomographyBuildResult Build(
            ReachyCameraCalibrationProfile calibration,
            ReachyCameraRelativeRotationSample rotation,
            ulong sourceSessionId,
            ulong sourceSequence,
            long sourceTimestampNanoseconds,
            string sourceCameraId,
            ReachyDeviceCameraFacing sourceFacing,
            int sourceWidth,
            int sourceHeight)
        {
            if (calibration == null)
            {
                throw new ArgumentNullException(nameof(calibration));
            }
            if (rotation == null)
            {
                throw new ArgumentNullException(nameof(rotation));
            }
            if (sourceSessionId == 0UL ||
                sourceSequence == 0UL ||
                sourceTimestampNanoseconds <= 0L)
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.InvalidFrameIdentity,
                    "The normalized source texture requires nonzero session, sequence, and timestamp metadata.");
            }
            if (string.IsNullOrWhiteSpace(sourceCameraId))
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.CameraMismatch,
                    "The normalized source texture has no camera identifier.");
            }
            if (!string.Equals(
                    calibration.CameraId,
                    sourceCameraId,
                    StringComparison.Ordinal) ||
                calibration.Facing != sourceFacing)
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.CameraMismatch,
                    "The normalized source texture does not match the selected calibration camera.");
            }
            if (sourceWidth != calibration.PhoneIntrinsics.ImageWidth ||
                sourceHeight != calibration.PhoneIntrinsics.ImageHeight)
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.SourceSizeMismatch,
                    "The normalized source texture dimensions do not match K_phone.");
            }
            if (!string.Equals(
                    calibration.ModelCompatibility,
                    ReachyCameraMujocoOpticalBinding
                        .OfficialModelCompatibility,
                    StringComparison.Ordinal))
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus
                        .CalibrationModelMismatch,
                    "The selected calibration does not match the authoritative Reachy model.");
            }
            if (sourceTimestampNanoseconds !=
                rotation.PhoneTimestampNanoseconds)
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.TimestampMismatch,
                    "The camera texture and phone orientation are not timestamp-corresponding.");
            }
            ReachyMatrix3x3 reachyFromPhone =
                rotation.CurrentReachyFromCurrentPhone;
            if (!reachyFromPhone.IsProperRotation())
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.InvalidRotation,
                    "The RMA-101 phone-to-Reachy transform is not a proper rotation.");
            }

            try
            {
                ReachyMatrix3x3 phoneToReachyPixels = CanonicalizeNumericalNoise(
                    calibration.ReachyIntrinsics.PixelFromOpticalRay *
                    reachyFromPhone *
                    calibration.PhoneIntrinsics.OpticalRayFromPixel);
                ReachyMatrix3x3 reachyToPhonePixels = CanonicalizeNumericalNoise(
                    calibration.PhoneIntrinsics.PixelFromOpticalRay *
                    reachyFromPhone.Transposed() *
                    calibration.ReachyIntrinsics.OpticalRayFromPixel);

                ReachyMatrix3x3 roundTrip =
                    phoneToReachyPixels * reachyToPhonePixels;
                if (!roundTrip.ApproximatelyEquals(
                        ReachyMatrix3x3.Identity,
                        1.0e-7))
                {
                    return Failure(
                        ReachyCameraHomographyBuildStatus.InvalidHomography,
                        "Forward and inverse homographies do not compose to identity.");
                }

                return new ReachyCameraHomographyBuildResult(
                    ReachyCameraHomographyBuildStatus.Success,
                    new ReachyCameraHomographyPlan(
                        calibration,
                        rotation,
                        sourceSessionId,
                        sourceSequence,
                        sourceTimestampNanoseconds,
                        phoneToReachyPixels,
                        reachyToPhonePixels),
                    "Built the GPU inverse-mapping homography from exact calibration and authoritative rotation.");
            }
            catch (InvalidOperationException exception)
            {
                return Failure(
                    ReachyCameraHomographyBuildStatus.InvalidHomography,
                    $"Homography construction failed closed: {exception.Message}");
            }
        }

        private static ReachyMatrix3x3 CanonicalizeNumericalNoise(
            ReachyMatrix3x3 matrix)
        {
            return new ReachyMatrix3x3(
                CanonicalizeScalar(matrix.M00),
                CanonicalizeScalar(matrix.M01),
                CanonicalizeScalar(matrix.M02),
                CanonicalizeScalar(matrix.M10),
                CanonicalizeScalar(matrix.M11),
                CanonicalizeScalar(matrix.M12),
                CanonicalizeScalar(matrix.M20),
                CanonicalizeScalar(matrix.M21),
                CanonicalizeScalar(matrix.M22));
        }

        private static double CanonicalizeScalar(double value)
        {
            if (Math.Abs(value) <= NumericalCanonicalizationTolerance)
            {
                return 0.0;
            }
            if (Math.Abs(value - 1.0) <=
                NumericalCanonicalizationTolerance)
            {
                return 1.0;
            }
            if (Math.Abs(value + 1.0) <=
                NumericalCanonicalizationTolerance)
            {
                return -1.0;
            }
            return value;
        }

        private static ReachyCameraHomographyBuildResult Failure(
            ReachyCameraHomographyBuildStatus status,
            string message)
        {
            return new ReachyCameraHomographyBuildResult(
                status,
                null,
                message);
        }
    }
}

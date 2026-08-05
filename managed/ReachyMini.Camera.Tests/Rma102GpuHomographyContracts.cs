#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma102GpuHomographyContracts
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            Run();
        }

        internal static void Run()
        {
            IdentityWarpMapsPixelCentersExactly();
            ForwardAndInverseHomographiesRoundTrip();
            OutputResolutionIsIndependentFromSource();
            InvalidAndBehindCameraSamplesAreRejected();
            FrameAndCalibrationMismatchesFailClosed();
            Console.WriteLine(
                "RMA-102 GPU homography contracts passed.");
        }

        private static void IdentityWarpMapsPixelCentersExactly()
        {
            ReachyCameraHomographyPlan plan = BuildPlan(
                Profile(
                    640,
                    480,
                    PhoneIntrinsics(640, 480),
                    PhoneIntrinsics(640, 480)),
                ReachyMatrix3x3.Identity).Plan!;

            True(
                plan.PhoneToReachyPixels.ApproximatelyEquals(
                    ReachyMatrix3x3.Identity,
                    1.0e-9),
                "identity forward homography");
            True(
                plan.ReachyToPhonePixels.ApproximatelyEquals(
                    ReachyMatrix3x3.Identity,
                    1.0e-9),
                "identity inverse homography");
            True(
                plan.TryMapReachyPixelToPhonePixel(
                    319.0,
                    239.0,
                    out ReachyVector3D source),
                "identity center maps inside source");
            Near(319.0, source.X, "identity source x");
            Near(239.0, source.Y, "identity source y");
        }

        private static void ForwardAndInverseHomographiesRoundTrip()
        {
            ReachyMatrix3x3 rotation =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 24.0)
                .ToRotationMatrix();
            ReachyCameraCalibrationProfile profile = Profile(
                640,
                480,
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    510.0,
                    505.0,
                    319.5,
                    239.5,
                    0.75),
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    800,
                    600,
                    640.0,
                    635.0,
                    399.5,
                    299.5,
                    -0.5));
            ReachyCameraHomographyPlan plan =
                BuildPlan(profile, rotation).Plan!;

            ReachyMatrix3x3 expectedForward =
                profile.ReachyIntrinsics.PixelFromOpticalRay *
                rotation *
                profile.PhoneIntrinsics.OpticalRayFromPixel;
            True(
                plan.PhoneToReachyPixels.ApproximatelyEquals(
                    expectedForward,
                    1.0e-9),
                "forward formula");
            True(
                (plan.PhoneToReachyPixels *
                    plan.ReachyToPhonePixels)
                    .ApproximatelyEquals(
                        ReachyMatrix3x3.Identity,
                        1.0e-7),
                "inverse round trip");
        }

        private static void OutputResolutionIsIndependentFromSource()
        {
            ReachyCameraIntrinsicMatrix phone =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    500.0,
                    500.0,
                    319.5,
                    239.5);
            ReachyCameraIntrinsicMatrix reachy =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    320,
                    240,
                    250.0,
                    250.0,
                    159.5,
                    119.5);
            ReachyCameraHomographyPlan plan = BuildPlan(
                Profile(640, 480, phone, reachy),
                ReachyMatrix3x3.Identity).Plan!;

            Equal(640, plan.SourceWidth, "source width");
            Equal(480, plan.SourceHeight, "source height");
            Equal(320, plan.OutputWidth, "output width");
            Equal(240, plan.OutputHeight, "output height");
            True(
                plan.TryMapReachyPixelToPhonePixel(
                    159.5,
                    119.5,
                    out ReachyVector3D source),
                "independent center maps");
            Near(319.5, source.X, "scaled center x");
            Near(239.5, source.Y, "scaled center y");
        }

        private static void InvalidAndBehindCameraSamplesAreRejected()
        {
            ReachyCameraHomographyPlan identity = BuildPlan(
                Profile(
                    640,
                    480,
                    PhoneIntrinsics(640, 480),
                    PhoneIntrinsics(640, 480)),
                ReachyMatrix3x3.Identity).Plan!;
            True(
                !identity.TryMapReachyPixelToPhonePixel(
                    -1.0,
                    0.0,
                    out _),
                "destination outside output rejected");

            ReachyMatrix3x3 quarterYaw =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 2.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan rotated = BuildPlan(
                Profile(
                    640,
                    480,
                    PhoneIntrinsics(640, 480),
                    PhoneIntrinsics(640, 480)),
                quarterYaw).Plan!;
            True(
                !rotated.TryMapReachyPixelToPhonePixel(
                    319.5,
                    239.5,
                    out _),
                "ray at or behind phone plane rejected");
        }

        private static void FrameAndCalibrationMismatchesFailClosed()
        {
            ReachyCameraCalibrationProfile profile = Profile(
                640,
                480,
                PhoneIntrinsics(640, 480),
                PhoneIntrinsics(640, 480));
            ReachyCameraRelativeRotationSample sample =
                RotationSample(ReachyMatrix3x3.Identity);

            Equal(
                ReachyCameraHomographyBuildStatus.CameraMismatch,
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    sample,
                    1UL,
                    1UL,
                    100L,
                    "front-1",
                    ReachyDeviceCameraFacing.Front,
                    640,
                    480).Status,
                "camera mismatch");
            Equal(
                ReachyCameraHomographyBuildStatus.SourceSizeMismatch,
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    sample,
                    1UL,
                    1UL,
                    100L,
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    1280,
                    720).Status,
                "source-size mismatch");
            Equal(
                ReachyCameraHomographyBuildStatus.TimestampMismatch,
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    sample,
                    1UL,
                    1UL,
                    101L,
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    640,
                    480).Status,
                "timestamp mismatch");
        }

        private static ReachyCameraHomographyBuildResult BuildPlan(
            ReachyCameraCalibrationProfile profile,
            ReachyMatrix3x3 reachyFromPhone)
        {
            return ReachyCameraHomographyCalculator.Build(
                profile,
                RotationSample(reachyFromPhone),
                7UL,
                11UL,
                100L,
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                profile.PhoneIntrinsics.ImageWidth,
                profile.PhoneIntrinsics.ImageHeight);
        }

        private static ReachyCameraRelativeRotationSample RotationSample(
            ReachyMatrix3x3 reachyFromPhone)
        {
            return new ReachyCameraRelativeRotationSample(
                0x1234UL,
                42UL,
                0.25,
                3U,
                100L,
                ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId,
                ReachyMatrix3x3.Identity,
                ReachyMatrix3x3.Identity,
                reachyFromPhone);
        }

        private static ReachyCameraIntrinsicMatrix PhoneIntrinsics(
            int width,
            int height)
        {
            return ReachyCameraIntrinsicMatrix.CreatePinhole(
                width,
                height,
                500.0,
                500.0,
                (width - 1.0) * 0.5,
                (height - 1.0) * 0.5);
        }

        private static ReachyCameraCalibrationProfile Profile(
            int phoneWidth,
            int phoneHeight,
            ReachyCameraIntrinsicMatrix phoneIntrinsics,
            ReachyCameraIntrinsicMatrix reachyIntrinsics)
        {
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "rma102-profile",
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "RMA-102 contract calibration",
                "sha256:rma102-contract",
                ReachyCameraMujocoOpticalBinding
                    .OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                new ReachyCameraImageNormalization(
                    phoneWidth,
                    phoneHeight,
                    0,
                    0,
                    phoneWidth,
                    phoneHeight,
                    0,
                    false),
                phoneIntrinsics,
                reachyIntrinsics,
                ReachyQuaternionD.Identity);
        }

        private static void Near(
            double expected,
            double actual,
            string label,
            double tolerance = 1.0e-8)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, received {actual}.");
            }
        }

        private static void Equal<T>(
            T expected,
            T actual,
            string label)
            where T : notnull
        {
            if (!expected.Equals(actual))
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, received {actual}.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label);
            }
        }
    }
}

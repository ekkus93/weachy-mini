#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma100CameraCalibrationContracts
    {
        [ModuleInitializer]
        internal static void Run()
        {
            CoordinateBasesRemainExplicit();
            ImageNormalizationMatchesRma092Order();
            IntrinsicRaysRoundTrip();
            MirroringIsNotPhysicalOrientation();
            NeutralHomographyIsIdentityForAlignedRearCamera();
            PositiveOpticalYawMovesTheViewRight();
            InvalidCalibrationFailsClosed();
            SelectionRequiresAnExactProfile();
            Console.WriteLine("RMA-100 camera calibration contracts passed.");
        }

        private static void CoordinateBasesRemainExplicit()
        {
            ReachyVector3D mujoco = new ReachyVector3D(1.0, 2.0, 3.0);
            ReachyVector3D unity =
                ReachyCameraCoordinateContract.UnityWorldFromMujocoWorld
                    .Transform(mujoco);
            Near(1.0, unity.X, "MuJoCo to Unity x");
            Near(3.0, unity.Y, "MuJoCo to Unity y");
            Near(2.0, unity.Z, "MuJoCo to Unity z");

            ReachyVector3D optical =
                ReachyCameraCoordinateContract.OpticalFromMujocoCamera
                    .Transform(mujoco);
            Near(1.0, optical.X, "MuJoCo to optical x");
            Near(-3.0, optical.Y, "MuJoCo to optical y");
            Near(2.0, optical.Z, "MuJoCo to optical z");
            True(
                ReachyCameraCoordinateContract.OpticalFromMujocoCamera
                    .IsProperRotation(),
                "MuJoCo camera to optical basis is proper");
            Contains(
                ReachyCameraCoordinateContract.AndroidPixelAxes,
                "+v=down",
                "Android pixel axis contract");
        }

        private static void ImageNormalizationMatchesRma092Order()
        {
            var normalization = new ReachyCameraImageNormalization(
                sourceWidth: 4,
                sourceHeight: 3,
                cropLeft: 1,
                cropTop: 0,
                cropWidth: 3,
                cropHeight: 3,
                clockwiseRotationDegrees: 90,
                mirrorHorizontally: true);

            Equal(3, normalization.OutputWidth, "normalized width");
            Equal(3, normalization.OutputHeight, "normalized height");
            AssertPixel(
                normalization.NormalizedFromSourcePixels,
                sourceX: 1.0,
                sourceY: 0.0,
                expectedX: 0.0,
                expectedY: 0.0,
                "crop-rotate-mirror upper-left");
            AssertPixel(
                normalization.NormalizedFromSourcePixels,
                sourceX: 3.0,
                sourceY: 2.0,
                expectedX: 2.0,
                expectedY: 2.0,
                "crop-rotate-mirror lower-right");

            ReachyVector3D source =
                normalization.SourceFromNormalizedPixels.TransformPixel(0.0, 0.0);
            Near(1.0, source.X, "inverse normalized x");
            Near(0.0, source.Y, "inverse normalized y");
        }

        private static void IntrinsicRaysRoundTrip()
        {
            ReachyCameraIntrinsicMatrix intrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    500.0,
                    510.0,
                    319.5,
                    239.5,
                    0.25);
            ReachyVector3D ray = intrinsics.GetOpticalRay(412.0, 173.0);
            ReachyVector3D projected = intrinsics.ProjectOpticalRay(ray);
            Near(412.0, projected.X, "intrinsic round-trip x", 1.0e-9);
            Near(173.0, projected.Y, "intrinsic round-trip y", 1.0e-9);
        }

        private static void MirroringIsNotPhysicalOrientation()
        {
            ReachyCameraCalibrationProfile front = Profile(
                "front-neutral",
                ReachyDeviceCameraFacing.Front,
                calibrated: true,
                createdUtc: new DateTimeOffset(
                    2026,
                    8,
                    4,
                    22,
                    0,
                    0,
                    TimeSpan.Zero));

            True(
                front.ImageNormalization.MirrorHorizontally,
                "front pixel mirror is explicit");
            True(
                front.NeutralReachyFromPhoneRotation == ReachyQuaternionD.Identity,
                "front mirror does not alter physical neutral rotation");
            Equal(
                ReachyCameraReprojectionMode.RotationOnly,
                front.ReprojectionMode,
                "rotation-only mode");
        }

        private static void NeutralHomographyIsIdentityForAlignedRearCamera()
        {
            ReachyCameraCalibrationProfile rear = Profile(
                "rear-neutral",
                ReachyDeviceCameraFacing.Rear,
                calibrated: true,
                createdUtc: new DateTimeOffset(
                    2026,
                    8,
                    4,
                    22,
                    1,
                    0,
                    TimeSpan.Zero));

            True(
                rear.BuildNeutralHomography().ApproximatelyEquals(
                    ReachyMatrix3x3.Identity,
                    1.0e-9),
                "aligned equal-intrinsic neutral homography");
        }

        private static void PositiveOpticalYawMovesTheViewRight()
        {
            ReachyCameraIntrinsicMatrix intrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    500.0,
                    500.0,
                    320.0,
                    240.0);
            ReachyQuaternionD yaw = ReachyQuaternionD.FromAxisAngle(
                new ReachyVector3D(0.0, 1.0, 0.0),
                Math.PI / 12.0);
            ReachyVector3D rotated = yaw.ToRotationMatrix().Transform(
                new ReachyVector3D(0.0, 0.0, 1.0));
            ReachyVector3D pixel = intrinsics.ProjectOpticalRay(rotated);
            True(pixel.X > 320.0, "positive optical yaw moves image right");
            Near(240.0, pixel.Y, "yaw keeps vertical center", 1.0e-9);
        }

        private static void InvalidCalibrationFailsClosed()
        {
            Throws<ArgumentException>(
                () =>
                {
                    _ = new ReachyCameraIntrinsicMatrix(
                        640,
                        480,
                        new ReachyMatrix3x3(
                            0.0, 0.0, 0.0,
                            0.0, 0.0, 0.0,
                            0.0, 0.0, 1.0));
                },
                "singular intrinsic matrix");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraImageNormalization(
                        640,
                        480,
                        0,
                        0,
                        640,
                        480,
                        45,
                        false);
                },
                "non-right-angle display rotation");
            Throws<ArgumentException>(
                () =>
                {
                    ReachyCameraCalibrationProfile profile = Profile(
                        "bad-front",
                        ReachyDeviceCameraFacing.Front,
                        calibrated: true,
                        createdUtc: DateTimeOffset.UnixEpoch,
                        forceMirror: false);
                    _ = profile;
                },
                "front camera without explicit mirror");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    ReachyCameraCalibrationProfile profile =
                        new ReachyCameraCalibrationProfile(
                            ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                            "unknown-provenance",
                            "rear-0",
                            ReachyDeviceCameraFacing.Rear,
                            ReachyCameraCalibrationProvenance.Unknown,
                            "unknown",
                            "none",
                            "reachy-mini-official-v1",
                            DateTimeOffset.UnixEpoch,
                            Normalization(
                                ReachyDeviceCameraFacing.Rear,
                                forceMirror: null),
                            Pinhole(),
                            Pinhole(),
                            ReachyQuaternionD.Identity);
                    _ = profile;
                },
                "unknown provenance");
        }

        private static void SelectionRequiresAnExactProfile()
        {
            var store = new ReachyCameraCalibrationStateStore();
            store.Upsert(Profile(
                "rear-estimate",
                ReachyDeviceCameraFacing.Rear,
                calibrated: false,
                createdUtc: new DateTimeOffset(
                    2026,
                    8,
                    4,
                    22,
                    0,
                    0,
                    TimeSpan.Zero)));
            store.Upsert(Profile(
                "rear-calibrated",
                ReachyDeviceCameraFacing.Rear,
                calibrated: true,
                createdUtc: new DateTimeOffset(
                    2026,
                    8,
                    4,
                    21,
                    0,
                    0,
                    TimeSpan.Zero)));

            ReachyCameraCalibrationSelectionResult exact = store.SelectExact(
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                640,
                480,
                640,
                480,
                "reachy-mini-official-v1");
            Equal(
                ReachyCameraCalibrationSelectionStatus.ExactCalibrated,
                exact.Status,
                "calibrated profile preferred over newer estimate");
            Equal(
                "rear-calibrated",
                exact.Profile!.ProfileId,
                "selected calibrated profile");

            Equal(
                ReachyCameraCalibrationSelectionStatus.CameraMismatch,
                store.SelectExact(
                    "front-1",
                    ReachyDeviceCameraFacing.Front,
                    640,
                    480,
                    640,
                    480,
                    "reachy-mini-official-v1").Status,
                "camera mismatch remains visible");
            Equal(
                ReachyCameraCalibrationSelectionStatus.ImageSizeMismatch,
                store.SelectExact(
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    1280,
                    720,
                    640,
                    480,
                    "reachy-mini-official-v1").Status,
                "image-size mismatch remains visible");
            Equal(
                ReachyCameraCalibrationSelectionStatus.ModelMismatch,
                store.SelectExact(
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    640,
                    480,
                    640,
                    480,
                    "different-model").Status,
                "model mismatch remains visible");
        }

        private static ReachyCameraCalibrationProfile Profile(
            string profileId,
            ReachyDeviceCameraFacing facing,
            bool calibrated,
            DateTimeOffset createdUtc,
            bool? forceMirror = null)
        {
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                profileId,
                facing == ReachyDeviceCameraFacing.Front
                    ? "front-1"
                    : "rear-0",
                facing,
                calibrated
                    ? ReachyCameraCalibrationProvenance.MeasuredCheckerboard
                    : ReachyCameraCalibrationProvenance.UncalibratedEstimate,
                calibrated
                    ? "checkerboard fit with retained source evidence"
                    : "explicit temporary pinhole estimate",
                calibrated ? "sha256:calibration-dataset" : "active-array-estimate",
                "reachy-mini-official-v1",
                createdUtc,
                Normalization(facing, forceMirror),
                Pinhole(),
                Pinhole(),
                ReachyQuaternionD.Identity);
        }

        private static ReachyCameraImageNormalization Normalization(
            ReachyDeviceCameraFacing facing,
            bool? forceMirror)
        {
            return new ReachyCameraImageNormalization(
                640,
                480,
                0,
                0,
                640,
                480,
                0,
                forceMirror ?? facing == ReachyDeviceCameraFacing.Front);
        }

        private static ReachyCameraIntrinsicMatrix Pinhole()
        {
            return ReachyCameraIntrinsicMatrix.CreatePinhole(
                640,
                480,
                500.0,
                500.0,
                320.0,
                240.0);
        }

        private static void AssertPixel(
            ReachyMatrix3x3 matrix,
            double sourceX,
            double sourceY,
            double expectedX,
            double expectedY,
            string label)
        {
            ReachyVector3D actual = matrix.TransformPixel(sourceX, sourceY);
            Near(expectedX, actual.X, label + " x");
            Near(expectedY, actual.Y, label + " y");
        }

        private static void Near(
            double expected,
            double actual,
            string label,
            double tolerance = 1.0e-10)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, actual {actual}.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, actual {actual}.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + " was false.");
            }
        }

        private static void Contains(
            string value,
            string expected,
            string label)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    $"{label}: '{expected}' was not found in '{value}'.");
            }
        }

        private static void Throws<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"{label}: expected {typeof(TException).Name}.");
        }
    }
}

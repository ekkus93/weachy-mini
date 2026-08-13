#nullable enable

using System;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma101AuthoritativeRotationContracts
    {
        internal static void Run()
        {
            PinnedBindingMatchesOpticalFrame();
            NeutralSolvedPoseProducesNeutralRotation();
            YawPitchAndRollSignsAreExplicit();
            PhoneOrientationComposesAfterNeutralCalibration();
            TranslationIsAbsentFromTheApi();
            ModelMismatchFailsClosed();
            Console.WriteLine(
                "RMA-101 authoritative camera rotation contracts passed.");
        }

        private static void PinnedBindingMatchesOpticalFrame()
        {
            ReachyCameraMujocoOpticalBinding binding =
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini;
            Equal(
                15,
                ReachyCameraMujocoOpticalBinding
                    .CanonicalCameraPresentationIndex,
                "camera presentation index");
            Equal(16U, binding.CameraBodyId, "MuJoCo camera body ID");
            Equal(18, binding.ExpectedBodyPoseCount, "body pose count");
            Equal("__body_15", binding.CanonicalCameraBodyName, "body name");
            Equal("camera_optical", binding.OpticalSiteName, "site name");
            Equal("eye_camera", binding.CameraName, "camera name");
            True(
                binding.CameraBodyFromOptical.IsProperRotation(),
                "camera-body optical offset is proper");
            True(
                binding.NeutralMujocoWorldFromOptical.IsProperRotation(),
                "neutral world optical rotation is proper");
        }

        private static void NeutralSolvedPoseProducesNeutralRotation()
        {
            ReachyCameraRelativeRotationSample sample = Calculate(
                NeutralWorldFromCameraBody(),
                ReachyQuaternionD.Identity,
                ReachyQuaternionD.Identity);
            MatrixNear(
                ReachyMatrix3x3.Identity,
                sample.CurrentReachyFromNeutralReachy,
                "neutral relative rotation");
            MatrixNear(
                ReachyMatrix3x3.Identity,
                sample.CurrentReachyFromCurrentPhone,
                "neutral phone-to-Reachy rotation");
        }

        private static void YawPitchAndRollSignsAreExplicit()
        {
            const double angle = Math.PI / 12.0;
            ReachyQuaternionD yaw = ReachyQuaternionD.FromAxisAngle(
                new ReachyVector3D(0.0, 1.0, 0.0),
                angle);
            ReachyVector3D yawForward = CalculateForOpticalDelta(yaw)
                .CurrentReachyFromNeutralReachy
                .Transform(new ReachyVector3D(0.0, 0.0, 1.0));
            True(
                yawForward.X < 0.0,
                "positive current optical yaw moves neutral forward toward negative current x");
            Near(0.0, yawForward.Y, "yaw vertical sign");

            ReachyQuaternionD pitch = ReachyQuaternionD.FromAxisAngle(
                new ReachyVector3D(1.0, 0.0, 0.0),
                angle);
            ReachyVector3D pitchForward = CalculateForOpticalDelta(pitch)
                .CurrentReachyFromNeutralReachy
                .Transform(new ReachyVector3D(0.0, 0.0, 1.0));
            True(
                pitchForward.Y > 0.0,
                "positive current optical pitch moves neutral forward toward current image-down");

            ReachyQuaternionD roll = ReachyQuaternionD.FromAxisAngle(
                new ReachyVector3D(0.0, 0.0, 1.0),
                angle);
            ReachyVector3D rollRight = CalculateForOpticalDelta(roll)
                .CurrentReachyFromNeutralReachy
                .Transform(new ReachyVector3D(1.0, 0.0, 0.0));
            True(
                rollRight.Y < 0.0,
                "positive current optical roll moves neutral right toward current image-up");
        }

        private static void PhoneOrientationComposesAfterNeutralCalibration()
        {
            ReachyQuaternionD neutralReachyFromPhone =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 18.0);
            ReachyQuaternionD neutralPhoneFromCurrentPhone =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(1.0, 0.0, 0.0),
                    -Math.PI / 20.0);
            ReachyCameraRelativeRotationSample sample = Calculate(
                NeutralWorldFromCameraBody(),
                neutralReachyFromPhone,
                neutralPhoneFromCurrentPhone);
            MatrixNear(
                neutralReachyFromPhone.ToRotationMatrix() *
                    neutralPhoneFromCurrentPhone.ToRotationMatrix(),
                sample.CurrentReachyFromCurrentPhone,
                "phone/calibration composition order");
            Equal(123456789L, sample.PhoneTimestampNanoseconds, "phone timestamp");
            Equal(42UL, sample.AuthoritativeSequence, "authoritative sequence");
        }

        private static void TranslationIsAbsentFromTheApi()
        {
            var parameters = typeof(
                ReachyCameraRelativeRotationCalculator)
                .GetMethod(nameof(
                    ReachyCameraRelativeRotationCalculator.Calculate))!
                .GetParameters();
            foreach (var parameter in parameters)
            {
                string name = parameter.Name ?? string.Empty;
                True(
                    !name.Contains("position", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("translation", StringComparison.OrdinalIgnoreCase),
                    $"Level 1 calculator parameter '{name}' must not carry translation");
            }
        }

        private static void ModelMismatchFailsClosed()
        {
            ReachyCameraCalibrationProfile wrong = Profile(
                ReachyQuaternionD.Identity,
                "different-model");
            Throws<ArgumentException>(
                () => ReachyCameraRelativeRotationCalculator.Calculate(
                    ReachyCameraMujocoOpticalBinding.PinnedReachyMini,
                    wrong,
                    1UL,
                    1UL,
                    0.0,
                    1U,
                    NeutralWorldFromCameraBody(),
                    ReachyPhoneOpticalOrientationSample.Identity(0L)),
                "model mismatch");
        }

        private static ReachyCameraRelativeRotationSample CalculateForOpticalDelta(
            ReachyQuaternionD neutralWorldFromCurrentOpticalDelta)
        {
            ReachyQuaternionD neutralWorldFromOptical =
                new ReachyQuaternionD(-0.5, 0.5, -0.5, 0.5);
            ReachyQuaternionD opticalFromCameraBody =
                new ReachyQuaternionD(-1.0, 0.0, 0.0, 0.0);
            ReachyQuaternionD worldFromCurrentBody =
                neutralWorldFromOptical *
                neutralWorldFromCurrentOpticalDelta *
                opticalFromCameraBody;
            return Calculate(
                worldFromCurrentBody,
                ReachyQuaternionD.Identity,
                ReachyQuaternionD.Identity);
        }

        private static ReachyCameraRelativeRotationSample Calculate(
            ReachyQuaternionD worldFromCameraBody,
            ReachyQuaternionD neutralReachyFromPhone,
            ReachyQuaternionD neutralPhoneFromCurrentPhone)
        {
            return ReachyCameraRelativeRotationCalculator.Calculate(
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini,
                Profile(
                    neutralReachyFromPhone,
                    ReachyCameraMujocoOpticalBinding
                        .OfficialModelCompatibility),
                0x1234UL,
                42UL,
                0.25,
                7U,
                worldFromCameraBody,
                new ReachyPhoneOpticalOrientationSample(
                    123456789L,
                    neutralPhoneFromCurrentPhone));
        }

        private static ReachyQuaternionD NeutralWorldFromCameraBody()
        {
            return new ReachyQuaternionD(0.5, -0.5, -0.5, 0.5);
        }

        private static ReachyCameraCalibrationProfile Profile(
            ReachyQuaternionD neutralReachyFromPhone,
            string modelCompatibility)
        {
            var normalization = new ReachyCameraImageNormalization(
                640,
                480,
                0,
                0,
                640,
                480,
                0,
                false);
            ReachyCameraIntrinsicMatrix intrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    640,
                    480,
                    500.0,
                    500.0,
                    319.5,
                    239.5);
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "rma101-profile",
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "RMA-101 contract calibration",
                "sha256:rma101-contract",
                modelCompatibility,
                DateTimeOffset.UnixEpoch,
                normalization,
                intrinsics,
                intrinsics,
                neutralReachyFromPhone);
        }

        private static void MatrixNear(
            ReachyMatrix3x3 expected,
            ReachyMatrix3x3 actual,
            string label)
        {
            True(
                actual.ApproximatelyEquals(expected, 1.0e-9),
                label);
        }

        private static void Near(
            double expected,
            double actual,
            string label,
            double tolerance = 1.0e-9)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    $"{label}: expected {expected}, received {actual}.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
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

        private static void Throws<TException>(
            Action action,
            string label)
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

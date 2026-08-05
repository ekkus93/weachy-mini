#nullable enable

using System;
using System.Runtime.CompilerServices;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Rma104ReprojectionContracts
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            Run();
        }

        internal static void Run()
        {
            IdentityAndAxisRotationsAreDeterministic();
            IntrinsicScalingChangesTheInverseMap();
            FrontMirrorAndRightAngleNormalizationAreExplicit();
            ActualMujocoRotationOverridesRequestedTarget();
            RotationOnlyContractExcludesTranslation();
            Console.WriteLine(
                "RMA-104 reprojection contracts passed.");
        }

        private static void IdentityAndAxisRotationsAreDeterministic()
        {
            ReachyCameraCalibrationProfile profile = Profile(
                31,
                23,
                27,
                19,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 22.0,
                phoneFocalY: 21.0,
                reachyFocalX: 19.0,
                reachyFocalY: 18.0);

            ReachyCameraHomographyPlan identity = Plan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: 1UL,
                authoritativeSequence: 1UL);
            True(
                (identity.PhoneToReachyPixels *
                    identity.ReachyToPhonePixels)
                    .ApproximatelyEquals(
                        ReachyMatrix3x3.Identity,
                        1.0e-9),
                "identity forward and inverse maps round-trip");

            ReachyVector3D[] axes =
            {
                new ReachyVector3D(1.0, 0.0, 0.0),
                new ReachyVector3D(0.0, 1.0, 0.0),
                new ReachyVector3D(0.0, 0.0, 1.0),
            };
            for (int index = 0; index < axes.Length; ++index)
            {
                ReachyMatrix3x3 rotation =
                    ReachyQuaternionD.FromAxisAngle(
                        axes[index],
                        Math.PI / 24.0)
                    .ToRotationMatrix();
                ReachyCameraHomographyPlan first = Plan(
                    profile,
                    rotation,
                    sourceSequence: (ulong)(index + 2),
                    authoritativeSequence: (ulong)(index + 2));
                ReachyCameraHomographyPlan second = Plan(
                    profile,
                    rotation,
                    sourceSequence: (ulong)(index + 20),
                    authoritativeSequence: (ulong)(index + 20));
                True(
                    first.ReachyToPhonePixels.ApproximatelyEquals(
                        second.ReachyToPhonePixels,
                        1.0e-12),
                    $"axis {index} inverse map is deterministic");
                True(
                    !first.ReachyToPhonePixels.ApproximatelyEquals(
                        identity.ReachyToPhonePixels,
                        1.0e-6),
                    $"axis {index} changes the reprojection");
            }
        }

        private static void IntrinsicScalingChangesTheInverseMap()
        {
            ReachyCameraCalibrationProfile baseline = Profile(
                29,
                21,
                17,
                13,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 24.0,
                phoneFocalY: 18.0,
                reachyFocalX: 14.0,
                reachyFocalY: 12.0);
            ReachyCameraCalibrationProfile scaled = Profile(
                29,
                21,
                17,
                13,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 30.0,
                phoneFocalY: 15.0,
                reachyFocalX: 10.0,
                reachyFocalY: 16.0);

            ReachyCameraHomographyPlan baselinePlan = Plan(
                baseline,
                ReachyMatrix3x3.Identity,
                sourceSequence: 30UL,
                authoritativeSequence: 30UL);
            ReachyCameraHomographyPlan scaledPlan = Plan(
                scaled,
                ReachyMatrix3x3.Identity,
                sourceSequence: 31UL,
                authoritativeSequence: 31UL);

            True(
                !baselinePlan.ReachyToPhonePixels.ApproximatelyEquals(
                    scaledPlan.ReachyToPhonePixels,
                    1.0e-9),
                "camera-intrinsic scaling changes the inverse map");
            Equal(17, scaledPlan.OutputWidth, "scaled output width");
            Equal(13, scaledPlan.OutputHeight, "scaled output height");
        }

        private static void FrontMirrorAndRightAngleNormalizationAreExplicit()
        {
            const int width = 15;
            const int height = 9;
            var frontNormalization =
                new ReachyCameraImageNormalization(
                    width,
                    height,
                    0,
                    0,
                    width,
                    height,
                    0,
                    true);
            ReachyCameraIntrinsicMatrix raw =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    width,
                    height,
                    12.0,
                    12.0,
                    7.0,
                    4.0);
            ReachyCameraIntrinsicMatrix mirrored =
                frontNormalization.NormalizeIntrinsics(raw);
            ReachyCameraCalibrationProfile front = Profile(
                frontNormalization,
                mirrored,
                raw,
                ReachyDeviceCameraFacing.Front,
                "front-0");

            ReachyCameraHomographyPlan frontPlan = Plan(
                front,
                ReachyMatrix3x3.Identity,
                sourceSequence: 40UL,
                authoritativeSequence: 40UL);
            True(
                frontPlan.TryMapReachyPixelToPhonePixel(
                    0.0,
                    4.0,
                    out ReachyVector3D mapped),
                "front mirror maps the left output edge");
            Near(
                width - 1.0,
                mapped.X,
                "front mirror maps left output to right source",
                1.0e-9);

            var portraitNormalization =
                new ReachyCameraImageNormalization(
                    13,
                    9,
                    0,
                    0,
                    13,
                    9,
                    90,
                    false);
            Equal(
                9,
                portraitNormalization.OutputWidth,
                "portrait normalized width");
            Equal(
                13,
                portraitNormalization.OutputHeight,
                "portrait normalized height");
            ReachyVector3D rotated =
                portraitNormalization.NormalizedFromSourcePixels
                    .TransformPixel(0.0, 0.0);
            Near(
                8.0,
                rotated.X,
                "clockwise rotation x",
                1.0e-9);
            Near(
                0.0,
                rotated.Y,
                "clockwise rotation y",
                1.0e-9);
        }

        private static void ActualMujocoRotationOverridesRequestedTarget()
        {
            ReachyCameraCalibrationProfile profile = Profile(
                21,
                15,
                21,
                15,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 16.0,
                phoneFocalY: 16.0,
                reachyFocalX: 16.0,
                reachyFocalY: 16.0);
            ReachyCameraMujocoOpticalBinding binding =
                ReachyCameraMujocoOpticalBinding.PinnedReachyMini;
            ReachyQuaternionD neutralBody =
                new ReachyQuaternionD(
                    0.5,
                    -0.5,
                    -0.5,
                    0.5);
            ReachyQuaternionD actualBody =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 0.0, 1.0),
                    Math.PI / 18.0) *
                neutralBody;
            ReachyQuaternionD requestedTarget =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 0.0, 1.0),
                    -Math.PI / 12.0) *
                neutralBody;

            ReachyCameraRelativeRotationSample actual =
                ReachyCameraRelativeRotationCalculator.Calculate(
                    binding,
                    profile,
                    0x1234UL,
                    50UL,
                    0.5,
                    2U,
                    actualBody,
                    ReachyPhoneOpticalOrientationSample.Identity(500L));
            ReachyCameraRelativeRotationSample requested =
                ReachyCameraRelativeRotationCalculator.Calculate(
                    binding,
                    profile,
                    0x1234UL,
                    51UL,
                    0.51,
                    2U,
                    requestedTarget,
                    ReachyPhoneOpticalOrientationSample.Identity(500L));

            ReachyCameraHomographyPlan actualPlan = Plan(
                profile,
                actual,
                sourceSequence: 50UL);
            ReachyCameraHomographyPlan targetPlan = Plan(
                profile,
                requested,
                sourceSequence: 51UL);

            True(
                !actualPlan.ReachyToPhonePixels.ApproximatelyEquals(
                    targetPlan.ReachyToPhonePixels,
                    1.0e-6),
                "the solved MuJoCo orientation differs from the requested target");
            True(
                actualPlan.ReachyToPhonePixels.ApproximatelyEquals(
                    profile.PhoneIntrinsics.PixelFromOpticalRay *
                    actual.CurrentReachyFromCurrentPhone.Transposed() *
                    profile.ReachyIntrinsics.OpticalRayFromPixel,
                    1.0e-12),
                "the homography is built from the actual authoritative sample");
        }

        private static void RotationOnlyContractExcludesTranslation()
        {
            ReachyCameraCalibrationProfile profile = Profile(
                8,
                6,
                8,
                6,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 8.0,
                phoneFocalY: 8.0,
                reachyFocalX: 8.0,
                reachyFocalY: 8.0);
            Equal(
                ReachyCameraReprojectionMode.RotationOnly,
                profile.ReprojectionMode,
                "Level 1 reprojection mode");
        }

        private static ReachyCameraCalibrationProfile Profile(
            int sourceWidth,
            int sourceHeight,
            int outputWidth,
            int outputHeight,
            ReachyDeviceCameraFacing facing,
            int rotationDegrees,
            bool mirror,
            double phoneFocalX,
            double phoneFocalY,
            double reachyFocalX,
            double reachyFocalY)
        {
            var normalization =
                new ReachyCameraImageNormalization(
                    sourceWidth,
                    sourceHeight,
                    0,
                    0,
                    sourceWidth,
                    sourceHeight,
                    rotationDegrees,
                    mirror);
            ReachyCameraIntrinsicMatrix sourceIntrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    sourceWidth,
                    sourceHeight,
                    phoneFocalX,
                    phoneFocalY,
                    (sourceWidth - 1.0) * 0.5,
                    (sourceHeight - 1.0) * 0.5);
            ReachyCameraIntrinsicMatrix phone =
                normalization.NormalizeIntrinsics(sourceIntrinsics);
            ReachyCameraIntrinsicMatrix reachy =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    outputWidth,
                    outputHeight,
                    reachyFocalX,
                    reachyFocalY,
                    (outputWidth - 1.0) * 0.5,
                    (outputHeight - 1.0) * 0.5);
            string cameraId =
                facing == ReachyDeviceCameraFacing.Front
                    ? "front-0"
                    : "rear-0";
            return Profile(
                normalization,
                phone,
                reachy,
                facing,
                cameraId);
        }

        private static ReachyCameraCalibrationProfile Profile(
            ReachyCameraImageNormalization normalization,
            ReachyCameraIntrinsicMatrix phone,
            ReachyCameraIntrinsicMatrix reachy,
            ReachyDeviceCameraFacing facing,
            string cameraId)
        {
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "rma104-profile-" + cameraId,
                cameraId,
                facing,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "RMA-104 deterministic contract calibration",
                "sha256:rma104-contract",
                ReachyCameraMujocoOpticalBinding
                    .OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                normalization,
                phone,
                reachy,
                ReachyQuaternionD.Identity);
        }

        private static ReachyCameraHomographyPlan Plan(
            ReachyCameraCalibrationProfile profile,
            ReachyMatrix3x3 reachyFromPhone,
            ulong sourceSequence,
            ulong authoritativeSequence)
        {
            var rotation = new ReachyCameraRelativeRotationSample(
                0x1234UL,
                authoritativeSequence,
                authoritativeSequence * 0.002,
                2U,
                500L,
                ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId,
                ReachyMatrix3x3.Identity,
                ReachyMatrix3x3.Identity,
                reachyFromPhone);
            return Plan(profile, rotation, sourceSequence);
        }

        private static ReachyCameraHomographyPlan Plan(
            ReachyCameraCalibrationProfile profile,
            ReachyCameraRelativeRotationSample rotation,
            ulong sourceSequence)
        {
            ReachyCameraHomographyBuildResult result =
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    rotation,
                    3UL,
                    sourceSequence,
                    rotation.PhoneTimestampNanoseconds,
                    profile.CameraId,
                    profile.Facing,
                    profile.PhoneIntrinsics.ImageWidth,
                    profile.PhoneIntrinsics.ImageHeight);
            True(result.Succeeded, result.Message);
            return result.Plan!;
        }

        private static void Near(
            double expected,
            double actual,
            string label,
            double tolerance)
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

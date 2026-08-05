#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed partial class ReachyCameraReprojectionTests
    {
        private static ReachyCameraCalibrationProfile CreateProfile(
            int rawWidth,
            int rawHeight,
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
                    rawWidth,
                    rawHeight,
                    0,
                    0,
                    rawWidth,
                    rawHeight,
                    rotationDegrees,
                    mirror);
            ReachyCameraIntrinsicMatrix rawIntrinsics =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    rawWidth,
                    rawHeight,
                    phoneFocalX,
                    phoneFocalY,
                    (rawWidth - 1.0) * 0.5,
                    (rawHeight - 1.0) * 0.5);
            ReachyCameraIntrinsicMatrix phone =
                normalization.NormalizeIntrinsics(rawIntrinsics);
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
            return new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile
                    .CurrentProfileSchemaVersion,
                "unity-rma104-" + cameraId + "-" +
                    rotationDegrees,
                cameraId,
                facing,
                ReachyCameraCalibrationProvenance
                    .MeasuredCheckerboard,
                "Unity RMA-104 deterministic test calibration",
                "sha256:unity-rma104",
                ReachyCameraMujocoOpticalBinding
                    .OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                normalization,
                phone,
                reachy,
                ReachyQuaternionD.Identity);
        }

        private static ReachyCameraHomographyPlan BuildPlan(
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
                ReachyCameraMujocoOpticalBinding
                    .CanonicalCameraBodyId,
                ReachyMatrix3x3.Identity,
                ReachyMatrix3x3.Identity,
                reachyFromPhone);
            return BuildPlan(profile, rotation, sourceSequence);
        }

        private static ReachyCameraHomographyPlan BuildPlan(
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
            Assert.That(result.Succeeded, Is.True, result.Message);
            return result.Plan!;
        }

        private static ReachyCameraCoverageSnapshot PublishCoverage(
            ReachyCameraHomographyPlan plan)
        {
            var state = new ReachyCameraCoverageStateMachine();
            ReachyCameraCoveragePublishResult publication =
                state.Publish(
                    ReachyCameraValidCoverageCalculator.Calculate(
                        plan));
            Assert.That(
                publication.Succeeded,
                Is.True,
                publication.Message);
            return publication.Snapshot;
        }

        private static void AssertGpuMatchesCpu(
            ReachyCameraHomographyPlan plan,
            TestImage image)
        {
            Shader shader = RequireShader();
            Texture2D? colorReadback = null;
            Texture2D? validityReadback = null;
            try
            {
                ReachyCameraCoverageSnapshot coverage =
                    PublishCoverage(plan);
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(
                        image.Texture,
                        plan,
                        coverage);
                CpuWarpResult expected = WarpCpu(
                    plan,
                    image.TopLeftPixels);
                colorReadback = ReadBack(frame.Color);
                validityReadback = ReadBack(frame.Validity);
                AssertReadbackMatches(
                    plan,
                    expected,
                    colorReadback,
                    validityReadback);
                ReachyCameraCoverageMeasurement measurement =
                    frame.Coverage.Measurement!;
                Assert.That(
                    measurement.ValidPixelCount,
                    Is.EqualTo(expected.ValidPixelCount));
                Assert.That(
                    measurement.TotalPixelCount,
                    Is.EqualTo(expected.TotalPixelCount));
            }
            finally
            {
                Destroy(colorReadback);
                Destroy(validityReadback);
            }
        }

    }
}

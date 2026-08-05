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
        private const float ColorTolerance = 3.0f / 255.0f;
        private const float ValidityTolerance = 0.05f;

        [Test]
        public void IdentityTransformGoldenImageMatchesCpuReference()
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 17,
                rawHeight: 11,
                outputWidth: 17,
                outputHeight: 11,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 14.0,
                phoneFocalY: 14.0,
                reachyFocalX: 14.0,
                reachyFocalY: 14.0);
            ReachyCameraHomographyPlan plan = BuildPlan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: 1UL,
                authoritativeSequence: 1UL);
            using TestImage image = TestImage.CreatePattern(
                plan.SourceWidth,
                plan.SourceHeight);

            Assert.That(
                plan.PhoneToReachyPixels,
                Is.EqualTo(ReachyMatrix3x3.Identity));
            Assert.That(
                plan.ReachyToPhonePixels,
                Is.EqualTo(ReachyMatrix3x3.Identity));
            ReachyCameraCoverageMeasurement coverage =
                ReachyCameraValidCoverageCalculator.Calculate(plan);
            Assert.That(
                coverage.ValidPixelCount,
                Is.EqualTo(coverage.TotalPixelCount));
            Assert.That(
                coverage.TotalPixelCount,
                Is.EqualTo(17L * 11L));
            AssertGpuMatchesCpu(plan, image);
        }

        [TestCase(1.0, 0.0, 0.0, 7.5)]
        [TestCase(0.0, 1.0, 0.0, 8.0)]
        [TestCase(0.0, 0.0, 1.0, 9.0)]
        public void SyntheticYawPitchRollMatchDoublePrecisionCpuReference(
            double axisX,
            double axisY,
            double axisZ,
            double degrees)
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 31,
                rawHeight: 23,
                outputWidth: 27,
                outputHeight: 19,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 22.0,
                phoneFocalY: 21.0,
                reachyFocalX: 19.0,
                reachyFocalY: 18.0);
            ReachyMatrix3x3 rotation =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(axisX, axisY, axisZ),
                    degrees * Math.PI / 180.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan plan = BuildPlan(
                profile,
                rotation,
                sourceSequence: 10UL,
                authoritativeSequence: 10UL);
            using TestImage image = TestImage.CreatePattern(
                plan.SourceWidth,
                plan.SourceHeight);

            Assert.That(
                plan.ReachyToPhonePixels.ApproximatelyEquals(
                    ReachyMatrix3x3.Identity,
                    1.0e-6),
                Is.False);
            AssertGpuMatchesCpu(plan, image);
        }

        [Test]
        public void CameraIntrinsicScalingMatchesCpuReference()
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 29,
                rawHeight: 21,
                outputWidth: 17,
                outputHeight: 13,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 30.0,
                phoneFocalY: 15.0,
                reachyFocalX: 10.0,
                reachyFocalY: 16.0);
            ReachyCameraHomographyPlan plan = BuildPlan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: 20UL,
                authoritativeSequence: 20UL);
            using TestImage image = TestImage.CreatePattern(
                plan.SourceWidth,
                plan.SourceHeight);

            Assert.That(plan.OutputWidth, Is.EqualTo(17));
            Assert.That(plan.OutputHeight, Is.EqualTo(13));
            Assert.That(
                plan.ReachyToPhonePixels.ApproximatelyEquals(
                    ReachyMatrix3x3.Identity,
                    1.0e-6),
                Is.False);
            AssertGpuMatchesCpu(plan, image);
        }

        [Test]
        public void FrontCameraMirroringMatchesCpuReference()
        {
            const int width = 15;
            const int height = 9;
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: width,
                rawHeight: height,
                outputWidth: width,
                outputHeight: height,
                facing: ReachyDeviceCameraFacing.Front,
                rotationDegrees: 0,
                mirror: true,
                phoneFocalX: 12.0,
                phoneFocalY: 12.0,
                reachyFocalX: 12.0,
                reachyFocalY: 12.0);
            ReachyCameraHomographyPlan plan = BuildPlan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: 30UL,
                authoritativeSequence: 30UL);
            using TestImage image = TestImage.CreatePattern(
                plan.SourceWidth,
                plan.SourceHeight);

            Assert.That(
                plan.TryMapReachyPixelToPhonePixel(
                    0.0,
                    (height - 1.0) * 0.5,
                    out ReachyVector3D mapped),
                Is.True);
            Assert.That(
                mapped.X,
                Is.EqualTo(width - 1.0).Within(1.0e-9));
            AssertGpuMatchesCpu(plan, image);
        }

        [TestCase(90)]
        [TestCase(270)]
        public void PortraitLandscapeNormalizationMatchesCpuReference(
            int rotationDegrees)
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 13,
                rawHeight: 9,
                outputWidth: 11,
                outputHeight: 7,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: rotationDegrees,
                mirror: false,
                phoneFocalX: 11.0,
                phoneFocalY: 9.0,
                reachyFocalX: 9.0,
                reachyFocalY: 8.0);
            ReachyCameraHomographyPlan plan = BuildPlan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: (ulong)rotationDegrees,
                authoritativeSequence: (ulong)rotationDegrees);
            using TestImage image = TestImage.CreatePattern(
                plan.SourceWidth,
                plan.SourceHeight);

            Assert.That(plan.SourceWidth, Is.EqualTo(9));
            Assert.That(plan.SourceHeight, Is.EqualTo(13));
            AssertGpuMatchesCpu(plan, image);
        }

    }
}

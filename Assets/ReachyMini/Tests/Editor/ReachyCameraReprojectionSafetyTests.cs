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
        [Test]
        public void InvalidMaskBoundaryClearsPriorFramePixels()
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 23,
                rawHeight: 17,
                outputWidth: 23,
                outputHeight: 17,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 14.0,
                phoneFocalY: 14.0,
                reachyFocalX: 14.0,
                reachyFocalY: 14.0);
            ReachyCameraHomographyPlan firstPlan = BuildPlan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: 40UL,
                authoritativeSequence: 40UL);
            ReachyMatrix3x3 yaw =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    35.0 * Math.PI / 180.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan secondPlan = BuildPlan(
                profile,
                yaw,
                sourceSequence: 41UL,
                authoritativeSequence: 41UL);
            using TestImage firstImage = TestImage.CreateSolid(
                firstPlan.SourceWidth,
                firstPlan.SourceHeight,
                new Color32(255, 0, 255, 255));
            using TestImage secondImage = TestImage.CreateSolid(
                secondPlan.SourceWidth,
                secondPlan.SourceHeight,
                new Color32(0, 255, 0, 255));
            Shader shader = RequireShader();
            Texture2D? colorReadback = null;
            Texture2D? validityReadback = null;

            try
            {
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                ReachyCameraHomographyGpuFrame firstFrame =
                    renderer.Warp(
                        firstImage.Texture,
                        firstPlan,
                        PublishCoverage(firstPlan));
                ReachyCameraHomographyGpuFrame secondFrame =
                    renderer.Warp(
                        secondImage.Texture,
                        secondPlan,
                        PublishCoverage(secondPlan));
                Assert.That(
                    secondFrame.Color,
                    Is.SameAs(firstFrame.Color));
                Assert.That(
                    secondFrame.Validity,
                    Is.SameAs(firstFrame.Validity));

                CpuWarpResult expected = WarpCpu(
                    secondPlan,
                    secondImage.TopLeftPixels);
                colorReadback = ReadBack(secondFrame.Color);
                validityReadback = ReadBack(secondFrame.Validity);
                AssertReadbackMatches(
                    secondPlan,
                    expected,
                    colorReadback,
                    validityReadback);

                Assert.That(expected.ValidPixelCount, Is.GreaterThan(0L));
                Assert.That(
                    expected.ValidPixelCount,
                    Is.LessThan(expected.TotalPixelCount));
                for (int index = 0;
                    index < expected.Validity.Length;
                    ++index)
                {
                    if (expected.Validity[index])
                    {
                        continue;
                    }
                    int x = index % secondPlan.OutputWidth;
                    int topY = index / secondPlan.OutputWidth;
                    Color actual = colorReadback.GetPixel(
                        x,
                        secondPlan.OutputHeight - 1 - topY);
                    Assert.That(
                        actual.r,
                        Is.LessThan(ColorTolerance),
                        $"invalid pixel ({x}, {topY}) retained red");
                    Assert.That(
                        actual.g,
                        Is.LessThan(ColorTolerance),
                        $"invalid pixel ({x}, {topY}) retained green");
                    Assert.That(
                        actual.b,
                        Is.LessThan(ColorTolerance),
                        $"invalid pixel ({x}, {topY}) retained blue");
                }
            }
            finally
            {
                Destroy(colorReadback);
                Destroy(validityReadback);
            }
        }

        [Test]
        public void ActualMujocoHeadRotationDrivesTheWarp()
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 21,
                rawHeight: 15,
                outputWidth: 21,
                outputHeight: 15,
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
                    10.0 * Math.PI / 180.0) *
                neutralBody;
            ReachyQuaternionD requestedTarget =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 0.0, 1.0),
                    -15.0 * Math.PI / 180.0) *
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
            ReachyCameraRelativeRotationSample target =
                ReachyCameraRelativeRotationCalculator.Calculate(
                    binding,
                    profile,
                    0x1234UL,
                    51UL,
                    0.51,
                    2U,
                    requestedTarget,
                    ReachyPhoneOpticalOrientationSample.Identity(500L));
            ReachyCameraHomographyPlan actualPlan = BuildPlan(
                profile,
                actual,
                sourceSequence: 50UL);
            ReachyCameraHomographyPlan targetPlan = BuildPlan(
                profile,
                target,
                sourceSequence: 51UL);
            using TestImage image = TestImage.CreatePattern(
                actualPlan.SourceWidth,
                actualPlan.SourceHeight);

            Assert.That(
                actualPlan.ReachyToPhonePixels.ApproximatelyEquals(
                    targetPlan.ReachyToPhonePixels,
                    1.0e-6),
                Is.False);
            Assert.That(
                profile.ReprojectionMode,
                Is.EqualTo(
                    ReachyCameraReprojectionMode.RotationOnly));

            CpuWarpResult actualReference = WarpCpu(
                actualPlan,
                image.TopLeftPixels);
            CpuWarpResult targetReference = WarpCpu(
                targetPlan,
                image.TopLeftPixels);
            Assert.That(
                CountDifferentPixels(
                    actualReference,
                    targetReference),
                Is.GreaterThan(0));
            AssertGpuMatchesCpu(actualPlan, image);
        }

    }
}

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
        public void VisionConsumersUseTransformedFrameUnlessRawDebugIsExplicit()
        {
            ReachyCameraCalibrationProfile profile = CreateProfile(
                rawWidth: 9,
                rawHeight: 7,
                outputWidth: 9,
                outputHeight: 7,
                facing: ReachyDeviceCameraFacing.Rear,
                rotationDegrees: 0,
                mirror: false,
                phoneFocalX: 8.0,
                phoneFocalY: 8.0,
                reachyFocalX: 8.0,
                reachyFocalY: 8.0);
            ReachyCameraHomographyPlan normalPlan = BuildPlan(
                profile,
                ReachyMatrix3x3.Identity,
                sourceSequence: 60UL,
                authoritativeSequence: 60UL);
            ReachyMatrix3x3 backward =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan unusablePlan = BuildPlan(
                profile,
                backward,
                sourceSequence: 61UL,
                authoritativeSequence: 61UL);
            using TestImage image = TestImage.CreatePattern(
                normalPlan.SourceWidth,
                normalPlan.SourceHeight);
            Shader shader = RequireShader();

            using var renderer =
                new ReachyCameraHomographyWarpRenderer(shader);
            ReachyCameraHomographyGpuFrame normalFrame =
                renderer.Warp(
                    image.Texture,
                    normalPlan,
                    PublishCoverage(normalPlan));
            ReachyVisionFramePurpose[] purposes =
            {
                ReachyVisionFramePurpose.Tracking,
                ReachyVisionFramePurpose.VisionLanguage,
                ReachyVisionFramePurpose.WorldModel,
                ReachyVisionFramePurpose.Behavior,
                ReachyVisionFramePurpose.Diagnostics,
            };
            foreach (ReachyVisionFramePurpose purpose in purposes)
            {
                ReachyVisionFrameRoute route =
                    ReachyVisionFrameRoutingPolicy.Select(
                        purpose,
                        normalFrame);
                Assert.That(
                    route.TransformedFrame,
                    Is.SameAs(normalFrame));
                Assert.That(
                    route.UsesTransformedReachyEyeFrame,
                    Is.True);
                Assert.That(route.AllowsRawPhoneFrame, Is.False);
            }

            Assert.Throws<InvalidOperationException>(
                () => ReachyVisionFrameRoutingPolicy.Select(
                    ReachyVisionFramePurpose.Tracking,
                    null));
            ReachyVisionFrameRoute rawDebug =
                ReachyVisionFrameRoutingPolicy.Select(
                    ReachyVisionFramePurpose.ExplicitRawDebug,
                    null);
            Assert.That(rawDebug.TransformedFrame, Is.Null);
            Assert.That(rawDebug.AllowsRawPhoneFrame, Is.True);

            ReachyCameraHomographyGpuFrame unusableFrame =
                renderer.Warp(
                    image.Texture,
                    unusablePlan,
                    PublishCoverage(unusablePlan));
            Assert.That(
                unusableFrame.Coverage.CanCreateVisualObservations,
                Is.False);
            Assert.Throws<InvalidOperationException>(
                () => ReachyVisionFrameRoutingPolicy.Select(
                    ReachyVisionFramePurpose.VisionLanguage,
                    unusableFrame));
        }

    }
}

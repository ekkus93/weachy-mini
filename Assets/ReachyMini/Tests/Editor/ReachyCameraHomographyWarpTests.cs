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
    public sealed class ReachyCameraHomographyWarpTests
    {
        [Test]
        public void IdentityGpuWarpEmitsColorAndValidityMask()
        {
            Shader shader = RequireShader();
            Texture2D source = CreateCenterMarkerTexture();
            Texture2D? colorReadback = null;
            Texture2D? validityReadback = null;
            try
            {
                ReachyCameraHomographyPlan plan =
                    BuildPlan(3, 3, 3, 3, ReachyMatrix3x3.Identity);
                ReachyCameraCoverageSnapshot coverage =
                    PublishCoverage(plan);
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(source, plan, coverage);

                Assert.That(frame.Color.width, Is.EqualTo(3));
                Assert.That(frame.Color.height, Is.EqualTo(3));
                Assert.That(frame.Validity.width, Is.EqualTo(3));
                Assert.That(frame.Validity.height, Is.EqualTo(3));
                Assert.That(frame.Plan, Is.SameAs(plan));
                Assert.That(frame.Coverage, Is.SameAs(coverage));
                Assert.That(
                    frame.Coverage.State,
                    Is.EqualTo(ReachyCameraCoverageState.Normal));
                ReachyCameraCoverageMeasurement measurement =
                    frame.Coverage.Measurement!;
                Assert.That(
                    measurement.ValidPixelCount,
                    Is.EqualTo(9L));
                Assert.That(
                    measurement.CoverageFraction,
                    Is.EqualTo(1.0));

                colorReadback = ReadBack(frame.Color);
                validityReadback = ReadBack(frame.Validity);
                Color center = colorReadback.GetPixel(1, 1);
                Assert.That(center.r, Is.GreaterThan(0.9f));
                Assert.That(center.g, Is.LessThan(0.1f));
                Assert.That(center.b, Is.LessThan(0.1f));
                Assert.That(
                    validityReadback.GetPixel(1, 1).r,
                    Is.GreaterThan(0.9f));
                Assert.That(
                    validityReadback.GetPixel(0, 0).r,
                    Is.GreaterThan(0.9f));
            }
            finally
            {
                Destroy(source);
                Destroy(colorReadback);
                Destroy(validityReadback);
            }
        }

        [Test]
        public void OutputResolutionIsIndependentAndGpuResident()
        {
            Shader shader = RequireShader();
            Texture2D source = new Texture2D(
                8,
                6,
                TextureFormat.RGBA32,
                false,
                true);
            try
            {
                source.Apply(false, false);
                ReachyCameraHomographyPlan plan =
                    BuildPlan(8, 6, 4, 3, ReachyMatrix3x3.Identity);
                ReachyCameraCoverageSnapshot coverage =
                    PublishCoverage(plan);
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(source, plan, coverage);

                Assert.That(frame.Color.width, Is.EqualTo(4));
                Assert.That(frame.Color.height, Is.EqualTo(3));
                Assert.That(frame.Validity.width, Is.EqualTo(4));
                Assert.That(frame.Validity.height, Is.EqualTo(3));
                Assert.That(frame.Color, Is.Not.SameAs(source));
                Assert.That(frame.Color.IsCreated(), Is.True);
                Assert.That(frame.Validity.IsCreated(), Is.True);
                Assert.That(frame.Coverage.HasValidityMask, Is.True);
                Assert.That(
                    frame.Coverage.CanCreateVisualObservations,
                    Is.True);
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void MismatchedSourceTextureFailsClosedAndClearsOutputs()
        {
            Shader shader = RequireShader();
            Texture2D source = new Texture2D(
                4,
                4,
                TextureFormat.RGBA32,
                false,
                true);
            Texture2D wrong = new Texture2D(
                5,
                4,
                TextureFormat.RGBA32,
                false,
                true);
            try
            {
                ReachyCameraHomographyPlan plan =
                    BuildPlan(4, 4, 4, 4, ReachyMatrix3x3.Identity);
                ReachyCameraCoverageSnapshot coverage =
                    PublishCoverage(plan);
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                renderer.Warp(source, plan, coverage);
                Assert.Throws<ArgumentException>(
                    () => renderer.Warp(wrong, plan, coverage));
                Assert.That(renderer.ColorTexture, Is.Null);
                Assert.That(renderer.ValidityTexture, Is.Null);
            }
            finally
            {
                Destroy(source);
                Destroy(wrong);
            }
        }

        [Test]
        public void CoverageIdentityMismatchIsRejectedBeforeFramePublication()
        {
            Shader shader = RequireShader();
            Texture2D source = new Texture2D(
                4,
                4,
                TextureFormat.RGBA32,
                false,
                true);
            try
            {
                source.Apply(false, false);
                ReachyCameraHomographyPlan plan =
                    BuildPlan(4, 4, 4, 4, ReachyMatrix3x3.Identity);
                ReachyCameraHomographyPlan different =
                    BuildPlan(
                        4,
                        4,
                        4,
                        4,
                        ReachyQuaternionD.FromAxisAngle(
                            new ReachyVector3D(0.0, 1.0, 0.0),
                            Math.PI / 18.0)
                        .ToRotationMatrix());
                ReachyCameraCoverageSnapshot wrongCoverage =
                    PublishCoverage(different);
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);

                Assert.Throws<ArgumentException>(
                    () => renderer.Warp(
                        source,
                        plan,
                        wrongCoverage));
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void CoverageCountMatchesShaderValidityPredicate()
        {
            ReachyMatrix3x3 rotation =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 12.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan plan =
                BuildPlan(19, 13, 17, 11, rotation);
            ReachyCameraCoverageMeasurement measurement =
                ReachyCameraValidCoverageCalculator.Calculate(plan);

            long expected = 0L;
            for (int y = 0; y < plan.OutputHeight; ++y)
            {
                for (int x = 0; x < plan.OutputWidth; ++x)
                {
                    if (IsShaderValid(plan, x, y))
                    {
                        ++expected;
                    }
                }
            }

            Assert.That(
                measurement.ValidPixelCount,
                Is.EqualTo(expected));
            Assert.That(
                measurement.TotalPixelCount,
                Is.EqualTo(187L));
        }

        [Test]
        public void BehindCameraCenterIsInvalidWithoutSampling()
        {
            ReachyMatrix3x3 quarterYaw =
                ReachyQuaternionD.FromAxisAngle(
                    new ReachyVector3D(0.0, 1.0, 0.0),
                    Math.PI / 2.0)
                .ToRotationMatrix();
            ReachyCameraHomographyPlan plan =
                BuildPlan(5, 5, 5, 5, quarterYaw);
            Assert.That(
                plan.TryMapReachyPixelToPhonePixel(
                    2.0,
                    2.0,
                    out _),
                Is.False);
            ReachyCameraCoverageMeasurement measurement =
                ReachyCameraValidCoverageCalculator.Calculate(plan);
            Assert.That(
                measurement.CoverageFraction,
                Is.LessThan(0.5));
        }

        private static ReachyCameraHomographyPlan BuildPlan(
            int sourceWidth,
            int sourceHeight,
            int outputWidth,
            int outputHeight,
            ReachyMatrix3x3 reachyFromPhone)
        {
            ReachyCameraIntrinsicMatrix phone =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    sourceWidth,
                    sourceHeight,
                    4.0,
                    4.0,
                    (sourceWidth - 1.0) * 0.5,
                    (sourceHeight - 1.0) * 0.5);
            ReachyCameraIntrinsicMatrix reachy =
                ReachyCameraIntrinsicMatrix.CreatePinhole(
                    outputWidth,
                    outputHeight,
                    4.0 * outputWidth / sourceWidth,
                    4.0 * outputHeight / sourceHeight,
                    (outputWidth - 1.0) * 0.5,
                    (outputHeight - 1.0) * 0.5);
            var profile = new ReachyCameraCalibrationProfile(
                ReachyCameraCalibrationProfile.CurrentProfileSchemaVersion,
                "unity-rma102",
                "rear-0",
                ReachyDeviceCameraFacing.Rear,
                ReachyCameraCalibrationProvenance.MeasuredCheckerboard,
                "Unity RMA-102/RMA-103 test profile",
                "sha256:unity-rma102-rma103",
                ReachyCameraMujocoOpticalBinding
                    .OfficialModelCompatibility,
                DateTimeOffset.UnixEpoch,
                new ReachyCameraImageNormalization(
                    sourceWidth,
                    sourceHeight,
                    0,
                    0,
                    sourceWidth,
                    sourceHeight,
                    0,
                    false),
                phone,
                reachy,
                ReachyQuaternionD.Identity);
            var rotation = new ReachyCameraRelativeRotationSample(
                0x1234UL,
                9UL,
                0.125,
                2U,
                500L,
                ReachyCameraMujocoOpticalBinding.CanonicalCameraBodyId,
                ReachyMatrix3x3.Identity,
                ReachyMatrix3x3.Identity,
                reachyFromPhone);
            ReachyCameraHomographyBuildResult result =
                ReachyCameraHomographyCalculator.Build(
                    profile,
                    rotation,
                    3UL,
                    7UL,
                    500L,
                    "rear-0",
                    ReachyDeviceCameraFacing.Rear,
                    sourceWidth,
                    sourceHeight);
            Assert.That(result.Succeeded, Is.True, result.Message);
            return result.Plan!;
        }

        private static ReachyCameraCoverageSnapshot PublishCoverage(
            ReachyCameraHomographyPlan plan)
        {
            var state = new ReachyCameraCoverageStateMachine();
            ReachyCameraCoveragePublishResult result = state.Publish(
                ReachyCameraValidCoverageCalculator.Calculate(plan));
            Assert.That(result.Succeeded, Is.True, result.Message);
            return result.Snapshot;
        }

        private static bool IsShaderValid(
            ReachyCameraHomographyPlan plan,
            int outputX,
            int outputY)
        {
            ReachyVector3D projected =
                plan.ReachyToPhonePixels.Transform(
                    new ReachyVector3D(outputX, outputY, 1.0));
            if (projected.Z <=
                ReachyCameraValidCoverageCalculator
                    .ShaderDepthEpsilon)
            {
                return false;
            }

            double sourceX = projected.X / projected.Z;
            double sourceY = projected.Y / projected.Z;
            return sourceX >= 0.0 &&
                sourceX <= plan.SourceWidth - 1.0 &&
                sourceY >= 0.0 &&
                sourceY <= plan.SourceHeight - 1.0;
        }

        private static Shader RequireShader()
        {
            Assert.That(
                SystemInfo.graphicsDeviceType,
                Is.Not.EqualTo(GraphicsDeviceType.Null),
                "GPU homography tests require a real graphics device; do not use -nographics.");
            Shader shader = Shader.Find(
                ReachyCameraHomographyWarpRenderer.ShaderName);
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            return shader;
        }

        private static Texture2D CreateCenterMarkerTexture()
        {
            var source = new Texture2D(
                3,
                3,
                TextureFormat.RGBA32,
                false,
                true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[9];
            for (int index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = Color.black;
            }
            pixels[4] = Color.red;
            source.SetPixels(pixels);
            source.Apply(false, false);
            return source;
        }

        private static Texture2D ReadBack(RenderTexture source)
        {
            RenderTexture? previous = RenderTexture.active;
            var result = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                true);
            try
            {
                RenderTexture.active = source;
                result.ReadPixels(
                    new Rect(0, 0, source.width, source.height),
                    0,
                    0,
                    false);
                result.Apply(false, false);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void Destroy(Object? value)
        {
            if (value != null)
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}

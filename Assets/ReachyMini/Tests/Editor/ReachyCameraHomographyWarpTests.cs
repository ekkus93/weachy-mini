#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Rendering;
using UnityEngine;
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
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(source, plan);

                Assert.That(frame.Color.width, Is.EqualTo(3));
                Assert.That(frame.Color.height, Is.EqualTo(3));
                Assert.That(frame.Validity.width, Is.EqualTo(3));
                Assert.That(frame.Validity.height, Is.EqualTo(3));
                Assert.That(frame.Plan, Is.SameAs(plan));

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
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                ReachyCameraHomographyGpuFrame frame =
                    renderer.Warp(source, plan);

                Assert.That(frame.Color.width, Is.EqualTo(4));
                Assert.That(frame.Color.height, Is.EqualTo(3));
                Assert.That(frame.Validity.width, Is.EqualTo(4));
                Assert.That(frame.Validity.height, Is.EqualTo(3));
                Assert.That(frame.Color, Is.Not.SameAs(source));
                Assert.That(frame.Color.IsCreated(), Is.True);
                Assert.That(frame.Validity.IsCreated(), Is.True);
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
                using var renderer =
                    new ReachyCameraHomographyWarpRenderer(shader);
                renderer.Warp(source, plan);
                Assert.Throws<ArgumentException>(
                    () => renderer.Warp(wrong, plan));
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
                "Unity RMA-102 test profile",
                "sha256:unity-rma102",
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

        private static Shader RequireShader()
        {
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

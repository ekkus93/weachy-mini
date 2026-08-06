#nullable enable

using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ReachyMini.AppState;
using ReachyMini.Perception;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests.Editor
{
    public sealed class ReachyLightweightTrackingTests
    {
        [Test]
        public void StagingSizeIsBoundedAndAspectPreserving()
        {
            Assert.That(
                ReachyUnityTrackingFrameResources.CalculateStagingSize(
                    1920,
                    1080,
                    640),
                Is.EqualTo((640, 360)));
            Assert.That(
                ReachyUnityTrackingFrameResources.CalculateStagingSize(
                    320,
                    240,
                    640),
                Is.EqualTo((320, 240)));
        }

        [Test]
        public void FlipRowsProducesTopLeftOrder()
        {
            byte[] bottomLeft =
            {
                1, 2,
                3, 4,
                5, 6,
            };
            byte[] topLeft = ReachyUnityTrackingFrameResources.FlipRows(
                bottomLeft,
                width: 2,
                height: 3,
                bytesPerPixel: 1);
            CollectionAssert.AreEqual(
                new byte[]
                {
                    5, 6,
                    3, 4,
                    1, 2,
                },
                topLeft);
        }

        [UnityTest]
        public IEnumerator OwnedTrackingResourcesSurviveSourceReuseAndReadBackTopLeft()
        {
            RenderTexture colorSource = CreateSourceTexture(
                "RMA-111 source color",
                new[]
                {
                    new Color32(10, 0, 0, 255),
                    new Color32(20, 0, 0, 255),
                    new Color32(30, 0, 0, 255),
                    new Color32(40, 0, 0, 255),
                });
            RenderTexture validitySource = CreateSourceTexture(
                "RMA-111 source validity",
                new[]
                {
                    Color.white,
                    Color.white,
                    Color.white,
                    Color.white,
                });
            var resources = new ReachyUnityTrackingFrameResources(
                "rma111-unity-test",
                1UL,
                colorSource,
                validitySource);
            ReachyVisionFrame frame = CreateFrame(resources);
            Clear(colorSource, Color.blue);
            Task<ReachyTrackingFramePixels> pending =
                resources.StageAsync(
                    frame,
                    maximumDimension: 16,
                    CancellationToken.None).AsTask();
            while (!pending.IsCompleted)
            {
                yield return null;
            }
            Exception? failure = pending.IsFaulted
                ? pending.Exception?.GetBaseException() ??
                    new InvalidOperationException(
                        "RMA-111 readback failed without an exception.")
                : null;
            ReachyTrackingFramePixels? pixels =
                failure == null ? pending.Result : null;
            Task frameDispose = frame.DisposeAsync().AsTask();
            while (!frameDispose.IsCompleted)
            {
                yield return null;
            }
            Release(colorSource);
            Release(validitySource);
            if (failure != null)
            {
                throw failure;
            }
            Assert.That(pixels, Is.Not.Null);
            Assert.That(pixels!.Width, Is.EqualTo(2));
            Assert.That(pixels.Height, Is.EqualTo(2));
            byte[] rgba = pixels.CopyRgbaTopLeft();
            Assert.That(rgba[0], Is.EqualTo(30).Within(2));
            Assert.That(rgba[4], Is.EqualTo(40).Within(2));
            Assert.That(rgba[8], Is.EqualTo(10).Within(2));
            Assert.That(rgba[12], Is.EqualTo(20).Within(2));
            Assert.That(pixels.ValidityTopLeft.Span[0], Is.EqualTo(255));
            Assert.That(resources.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator DisposalWaitsForActiveGpuReadbackAndIsIdempotent()
        {
            RenderTexture colorSource = CreateSolidSource(
                "RMA-111 disposal color",
                64,
                64,
                Color.red);
            RenderTexture validitySource = CreateSolidSource(
                "RMA-111 disposal validity",
                64,
                64,
                Color.white);
            var resources = new ReachyUnityTrackingFrameResources(
                "rma111-disposal-test",
                2UL,
                colorSource,
                validitySource);
            ReachyVisionFrame frame = CreateFrame(resources);
            Task<ReachyTrackingFramePixels> readback = resources.StageAsync(
                frame,
                maximumDimension: 64,
                CancellationToken.None).AsTask();
            Task firstDispose = resources.DisposeAsync().AsTask();
            Task secondDispose = resources.DisposeAsync().AsTask();
            while (!readback.IsCompleted ||
                   !firstDispose.IsCompleted ||
                   !secondDispose.IsCompleted)
            {
                yield return null;
            }
            Assert.That(readback.IsCompletedSuccessfully, Is.True);
            Assert.That(firstDispose.IsCompletedSuccessfully, Is.True);
            Assert.That(secondDispose.IsCompletedSuccessfully, Is.True);
            Assert.That(resources.IsDisposed, Is.True);
            Task frameDispose = frame.DisposeAsync().AsTask();
            while (!frameDispose.IsCompleted)
            {
                yield return null;
            }
            Assert.That(frameDispose.IsCompletedSuccessfully, Is.True);
            Release(colorSource);
            Release(validitySource);
        }

        private static ReachyVisionFrame CreateFrame(
            ReachyUnityTrackingFrameResources resources)
        {
            var identity = new ReachyVisionFrameIdentity(
                "rma111-unity-camera",
                sourceSessionId: 1UL,
                sourceSequence: resources.Generation,
                sourceTimestampNanoseconds: checked(
                    (long)resources.Generation * 1_000_000_000L),
                authoritativeSequence: resources.Generation,
                continuityId: 1U);
            int total = checked(resources.Width * resources.Height);
            return new ReachyVisionFrame(
                VisionFrameOrigin.TransformedReachyEye,
                identity,
                new ReachyVisionCoverage(
                    VisionCoverageState.Normal,
                    total,
                    total,
                    hasValidityMask: true,
                    shouldStopVisionDrivenTurning: false,
                    "RMA-111 Unity test coverage."),
                resources);
        }

        private static RenderTexture CreateSourceTexture(
            string name,
            Color32[] bottomLeftPixels)
        {
            if (bottomLeftPixels.Length != 4)
            {
                throw new ArgumentException(
                    "The RMA-111 2x2 source requires four pixels.",
                    nameof(bottomLeftPixels));
            }
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: true)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(bottomLeftPixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            RenderTexture target = CreateSolidSource(
                name,
                2,
                2,
                Color.clear);
            Graphics.Blit(texture, target);
            Object.DestroyImmediate(texture);
            return target;
        }

        private static RenderTexture CreateSolidSource(
            string name,
            int width,
            int height,
            Color color)
        {
            var texture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
            };
            Assert.That(texture.Create(), Is.True);
            Clear(texture, color);
            return texture;
        }

        private static void Clear(RenderTexture texture, Color color)
        {
            RenderTexture? previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(clearDepth: false, clearColor: true, color);
            RenderTexture.active = previous;
        }

        private static void Release(RenderTexture texture)
        {
            if (ReferenceEquals(RenderTexture.active, texture))
            {
                RenderTexture.active = null;
            }
            if (texture.IsCreated())
            {
                texture.Release();
            }
            Object.DestroyImmediate(texture);
        }
    }
}

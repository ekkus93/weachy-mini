#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-115/195 VLM half: no IRemoteVlmImageEncoder implementation existed at all
    // before this file -- only a test fake (managed/ReachyMini.RemoteVlm.Tests'
    // FakeEncoder), confirmed by a dedicated investigation before writing any code.
    // Deliberately reuses IReachyTrackingPixelSource/ReachyTrackingFramePixels
    // (Runtime/Core/Perception/ReachyLightweightTracking.cs) rather than inventing a
    // second pixel-retrieval abstraction -- that interface already gives an
    // engine-agnostic, already-downscaled RGBA+validity-mask byte buffer from a
    // ReachyVisionFrame (the same primitive RMA-111's tracker uses), so only the
    // final "compress these bytes" step below is genuinely Unity-specific and
    // therefore lives here rather than in Runtime/Core.
    public sealed class ReachyUnityRemoteVlmImageEncoder : IRemoteVlmImageEncoder
    {
        // Matches ReachyTrackingFramePixels.IsDetectionCenterValid's own convention
        // for "is this pixel valid" (>= 128 out of 255).
        private const byte MinimumValidity = 128;

        public async ValueTask<RemoteVlmImageEncodingResult> EncodeAsync(
            RemoteVlmImageEncodingRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Cancelled,
                    "cancelled-before-staging",
                    requiresEncoderReset: false);
            }
            if (!request.Frame.Resources.TryGetResource<IReachyTrackingPixelSource>(
                    VisionResourceKind.Color,
                    out IReachyTrackingPixelSource? pixelSource) ||
                pixelSource == null)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Unsupported,
                    "no-pixel-source",
                    requiresEncoderReset: false);
            }

            int maximumDimension = Math.Max(
                request.Policy.MaximumWidth,
                request.Policy.MaximumHeight);

            ReachyTrackingFramePixels pixels;
            try
            {
                pixels = await pixelSource.StageAsync(
                        request.Frame,
                        maximumDimension,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    cancellationToken.IsCancellationRequested
                        ? RemoteVlmImageEncodingStatus.Cancelled
                        : RemoteVlmImageEncodingStatus.Failed,
                    "staging-cancelled",
                    requiresEncoderReset: !cancellationToken.IsCancellationRequested);
            }
            catch (Exception)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Failed,
                    "staging-failed",
                    requiresEncoderReset: true);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Cancelled,
                    "cancelled-after-staging",
                    requiresEncoderReset: false);
            }
            if (!pixels.Identity.Matches(request.Frame.Identity))
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Failed,
                    "staged-identity-mismatch",
                    requiresEncoderReset: true);
            }
            if (pixels.Width <= 0 || pixels.Height <= 0 ||
                pixels.Width > request.Frame.Width ||
                pixels.Height > request.Frame.Height ||
                pixels.Width > request.Policy.MaximumWidth ||
                pixels.Height > request.Policy.MaximumHeight)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Failed,
                    "staged-dimensions-out-of-bounds",
                    requiresEncoderReset: true);
            }

            byte[] compositedTopLeft = CompositeInvalidPixelsAsOpaqueBlack(
                pixels.RgbaTopLeft.Span,
                pixels.ValidityTopLeft.Span);
            // Unity's Texture2D raw-data row order runs bottom row first; the
            // staged buffer is explicitly top-row-first (see the "TopLeft" naming
            // on ReachyTrackingFramePixels), so the rows must be reversed here or
            // every image handed to the VLM would be encoded upside down --
            // wrong spatial content sent to the model, not just a cosmetic issue.
            byte[] bottomLeftForTexture = FlipRowsVertically(
                compositedTopLeft,
                pixels.Width,
                pixels.Height);

            byte[] jpegBytes;
            Texture2D? texture = null;
            try
            {
                texture = new Texture2D(
                    pixels.Width,
                    pixels.Height,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                texture.LoadRawTextureData(bottomLeftForTexture);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                jpegBytes = texture.EncodeToJPG(request.Policy.LossyQuality);
            }
            finally
            {
                DestroyTexture(texture);
                Array.Clear(bottomLeftForTexture, 0, bottomLeftForTexture.Length);
                Array.Clear(compositedTopLeft, 0, compositedTopLeft.Length);
            }

            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Failed,
                    "jpeg-encode-empty",
                    requiresEncoderReset: true);
            }
            if (jpegBytes.Length > request.Policy.MaximumEncodedBytes)
            {
                // No iterative re-encode/downscale-and-retry loop: a single
                // configured quality/dimension pair either fits the policy's
                // byte bound or this frame is reported as unencodable rather
                // than silently degrading quality further than configured.
                return RemoteVlmImageEncodingResult.Failure(
                    RemoteVlmImageEncodingStatus.Failed,
                    "encoded-bytes-exceed-policy",
                    requiresEncoderReset: false);
            }

            RemoteVlmEncodedImage image = new RemoteVlmEncodedImage(
                request.Frame.Identity,
                VisionFrameOrigin.TransformedReachyEye,
                request.Frame.Width,
                request.Frame.Height,
                pixels.Width,
                pixels.Height,
                request.Policy.PreferredFormat,
                request.Policy.InvalidPixelPolicy,
                validityMaskApplied: true,
                containsOnlyValidPixels: true,
                upscaled: false,
                jpegBytes);
            return RemoteVlmImageEncodingResult.Success(image);
        }

        private static byte[] CompositeInvalidPixelsAsOpaqueBlack(
            ReadOnlySpan<byte> rgbaTopLeft,
            ReadOnlySpan<byte> validityTopLeft)
        {
            byte[] composited = rgbaTopLeft.ToArray();
            for (int pixelIndex = 0; pixelIndex < validityTopLeft.Length; ++pixelIndex)
            {
                if (validityTopLeft[pixelIndex] >= MinimumValidity)
                {
                    continue;
                }
                int byteOffset = pixelIndex * 4;
                composited[byteOffset] = 0;
                composited[byteOffset + 1] = 0;
                composited[byteOffset + 2] = 0;
                composited[byteOffset + 3] = 255;
            }
            return composited;
        }

        // Mirrors ReachyAndroidCameraTextureBridge.Diagnostics.cs's DestroyUnityObject:
        // Destroy (deferred, safe from any thread-adjacent call site) at runtime,
        // DestroyImmediate only outside play mode (e.g. EditMode tests).
        private static void DestroyTexture(Texture2D? texture)
        {
            if (texture == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static byte[] FlipRowsVertically(byte[] topLeftRgba, int width, int height)
        {
            int rowBytes = checked(width * 4);
            byte[] flipped = new byte[topLeftRgba.Length];
            for (int row = 0; row < height; ++row)
            {
                Array.Copy(
                    topLeftRgba,
                    row * rowBytes,
                    flipped,
                    (height - 1 - row) * rowBytes,
                    rowBytes);
            }
            return flipped;
        }
    }
}

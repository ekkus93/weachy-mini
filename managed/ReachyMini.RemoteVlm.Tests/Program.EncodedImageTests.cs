#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static async Task EncodingRequestRequiresEligibleTransformedFrame()
        {
            await using ReachyVisionFrame raw = RawFrame();
            Throws<ArgumentException>(
                () => new RemoteVlmImageEncodingRequest(raw, Policy()),
                "raw encoding request");
        }

        private static void EncodedImageCopiesInputBytes()
        {
            byte[] bytes = { 1, 2, 3 };
            using RemoteVlmEncodedImage image = EncodedImage(
                Identity(),
                bytes: bytes);
            bytes[0] = 99;
            Equal((byte)1, image.EncodedBytes.Span[0], "defensive byte copy");
        }

        private static void EncodedImageRequiresTransformedOrigin()
        {
            Throws<ArgumentException>(
                () => EncodedImage(
                    Identity(),
                    sourceOrigin: VisionFrameOrigin.RawPhoneDebug),
                "raw encoded image");
        }

        private static void EncodedImageRequiresValidityApplication()
        {
            Throws<ArgumentException>(
                () => EncodedImage(
                    Identity(),
                    validityMaskApplied: false),
                "unmasked encoded image");
            Throws<ArgumentException>(
                () => EncodedImage(
                    Identity(),
                    containsOnlyValidPixels: false),
                "invalid pixel leakage");
        }

        private static void EncodedImageRejectsUpscaling()
        {
            Throws<ArgumentException>(
                () => EncodedImage(Identity(), upscaled: true),
                "upscaled payload");
            Throws<ArgumentOutOfRangeException>(
                () => EncodedImage(
                    Identity(),
                    sourceWidth: 10,
                    width: 11),
                "oversized encoded width");
        }

        private static void EncodedImageDisposalZeroesPayload()
        {
            var image = EncodedImage(Identity(), bytes: new byte[] { 7, 8, 9 });
            FieldInfo field = typeof(RemoteVlmEncodedImage).GetField(
                "encodedBytes",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("encodedBytes field not found.");
            byte[] privateBytes = (byte[])(field.GetValue(image) ??
                throw new InvalidOperationException("encodedBytes value missing."));
            image.Dispose();
            True(image.IsDisposed, "image disposed");
            True(privateBytes.All(value => value == 0), "payload zeroized");
            Throws<ObjectDisposedException>(
                () => image.EncodedBytes,
                "disposed payload access");
        }
    }
}

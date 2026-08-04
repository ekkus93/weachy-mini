#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using UnityEngine;

namespace ReachyMini.Tests
{
    public sealed class ReachyCameraTextureBridgeTests
    {
        [Test]
        public void DescriptorPreservesTimestampAndRotatedDimensions()
        {
            ReachyCameraTextureFrameDescriptor descriptor = Descriptor(
                rotationDegrees: 90,
                facing: ReachyDeviceCameraFacing.Rear,
                mirrored: false,
                width: 4,
                height: 2,
                crop: new ReachyCameraFrameCrop(1, 0, 4, 2),
                timestampNanoseconds: 987654321L);

            Assert.That(descriptor.TimestampNanoseconds, Is.EqualTo(987654321L));
            Assert.That(descriptor.OutputWidth, Is.EqualTo(2));
            Assert.That(descriptor.OutputHeight, Is.EqualTo(3));
            Assert.That(
                descriptor.Summary,
                Does.Contain("timestamp_ns=987654321"));
        }

        [Test]
        public void DescriptorRequiresFrontMirroringOnlyForFrontCamera()
        {
            Assert.Throws<ArgumentException>(() => Descriptor(
                facing: ReachyDeviceCameraFacing.Front,
                mirrored: false));
            Assert.Throws<ArgumentException>(() => Descriptor(
                facing: ReachyDeviceCameraFacing.Rear,
                mirrored: true));

            Assert.DoesNotThrow(() => Descriptor(
                facing: ReachyDeviceCameraFacing.Front,
                mirrored: true));
            Assert.DoesNotThrow(() => Descriptor(
                facing: ReachyDeviceCameraFacing.Rear,
                mirrored: false));
        }

        [TestCase(0, false, new[] { 0, 1, 2, 3 })]
        [TestCase(90, false, new[] { 2, 0, 3, 1 })]
        [TestCase(180, false, new[] { 3, 2, 1, 0 })]
        [TestCase(270, false, new[] { 1, 3, 0, 2 })]
        [TestCase(0, true, new[] { 1, 0, 3, 2 })]
        [TestCase(90, true, new[] { 0, 2, 1, 3 })]
        public void OutputMappingAppliesRotationAndFrontMirror(
            int rotationDegrees,
            bool mirrored,
            int[] expectedSourceIndices)
        {
            ReachyCameraTextureFrameDescriptor descriptor = Descriptor(
                rotationDegrees: rotationDegrees,
                facing: mirrored
                    ? ReachyDeviceCameraFacing.Front
                    : ReachyDeviceCameraFacing.Rear,
                mirrored: mirrored);

            var actual = new int[expectedSourceIndices.Length];
            for (int outputY = 0;
                 outputY < descriptor.OutputHeight;
                 ++outputY)
            {
                for (int outputX = 0;
                     outputX < descriptor.OutputWidth;
                     ++outputX)
                {
                    ReachyCameraYuvReferenceConverter.MapOutputPixelToSource(
                        descriptor,
                        outputX,
                        outputY,
                        out int sourceX,
                        out int sourceY);
                    actual[outputY * descriptor.OutputWidth + outputX] =
                        sourceY * descriptor.Width + sourceX;
                }
            }

            Assert.That(actual, Is.EqualTo(expectedSourceIndices));
        }

        [Test]
        public void OutputMappingHonorsCropBeforeRotation()
        {
            ReachyCameraTextureFrameDescriptor descriptor = Descriptor(
                rotationDegrees: 90,
                facing: ReachyDeviceCameraFacing.Rear,
                mirrored: false,
                width: 4,
                height: 2,
                crop: new ReachyCameraFrameCrop(1, 0, 4, 2));

            var actual = new int[
                descriptor.OutputWidth * descriptor.OutputHeight];
            for (int outputY = 0;
                 outputY < descriptor.OutputHeight;
                 ++outputY)
            {
                for (int outputX = 0;
                     outputX < descriptor.OutputWidth;
                     ++outputX)
                {
                    ReachyCameraYuvReferenceConverter.MapOutputPixelToSource(
                        descriptor,
                        outputX,
                        outputY,
                        out int sourceX,
                        out int sourceY);
                    actual[outputY * descriptor.OutputWidth + outputX] =
                        sourceY * descriptor.Width + sourceX;
                }
            }

            Assert.That(actual, Is.EqualTo(new[] { 5, 1, 6, 2, 7, 3 }));
        }

        [Test]
        public void Bt601LimitedReferenceConvertsBlackWhiteAndRed()
        {
            AssertColorNear(
                ReachyCameraYuvReferenceConverter.ConvertPixel(
                    16,
                    128,
                    128,
                    ReachyCameraYuvColorStandard.Bt601,
                    ReachyCameraYuvColorRange.Limited),
                new Color32(0, 0, 0, 255),
                tolerance: 1);
            AssertColorNear(
                ReachyCameraYuvReferenceConverter.ConvertPixel(
                    235,
                    128,
                    128,
                    ReachyCameraYuvColorStandard.Bt601,
                    ReachyCameraYuvColorRange.Limited),
                new Color32(255, 255, 255, 255),
                tolerance: 1);
            AssertColorNear(
                ReachyCameraYuvReferenceConverter.ConvertPixel(
                    81,
                    90,
                    240,
                    ReachyCameraYuvColorStandard.Bt601,
                    ReachyCameraYuvColorRange.Limited),
                new Color32(255, 0, 0, 255),
                tolerance: 3);
        }

        [Test]
        public void Bt709LimitedReferenceConvertsRed()
        {
            Color32 actual = ReachyCameraYuvReferenceConverter.ConvertPixel(
                63,
                102,
                240,
                ReachyCameraYuvColorStandard.Bt709,
                ReachyCameraYuvColorRange.Limited);

            AssertColorNear(
                actual,
                new Color32(255, 0, 0, 255),
                tolerance: 3);
        }

        [Test]
        public void PackedReferenceConversionRejectsPlaneLengthDrift()
        {
            ReachyCameraTextureFrameDescriptor descriptor = Descriptor();

            Assert.Throws<ArgumentException>(() =>
                ReachyCameraYuvReferenceConverter.Convert(
                    new byte[3],
                    new byte[1],
                    new byte[1],
                    descriptor));
            Assert.Throws<ArgumentException>(() =>
                ReachyCameraYuvReferenceConverter.Convert(
                    new byte[4],
                    Array.Empty<byte>(),
                    new byte[1],
                    descriptor));
        }

        [Test]
        public void PackedReferenceConversionPreservesTimestampedOrientation()
        {
            ReachyCameraTextureFrameDescriptor descriptor = Descriptor(
                rotationDegrees: 90,
                facing: ReachyDeviceCameraFacing.Rear,
                mirrored: false,
                timestampNanoseconds: 123456789L);
            byte[] yPlane = { 16, 64, 128, 235 };
            byte[] chroma = { 128 };

            Color32[] converted = ReachyCameraYuvReferenceConverter.Convert(
                yPlane,
                chroma,
                chroma,
                descriptor);

            Assert.That(converted, Has.Length.EqualTo(4));
            Assert.That(converted[0].r, Is.GreaterThan(converted[1].r));
            Assert.That(converted[2].r, Is.GreaterThan(converted[3].r));
            Assert.That(descriptor.TimestampNanoseconds, Is.EqualTo(123456789L));
        }

        private static ReachyCameraTextureFrameDescriptor Descriptor(
            int rotationDegrees = 0,
            ReachyDeviceCameraFacing facing = ReachyDeviceCameraFacing.Rear,
            bool mirrored = false,
            int width = 2,
            int height = 2,
            ReachyCameraFrameCrop? crop = null,
            long timestampNanoseconds = 123L)
        {
            ReachyCameraFrameCrop selectedCrop = crop ??
                new ReachyCameraFrameCrop(0, 0, width, height);
            return new ReachyCameraTextureFrameDescriptor(
                sessionId: 7UL,
                sequence: 11UL,
                timestampNanoseconds: timestampNanoseconds,
                cameraId: facing == ReachyDeviceCameraFacing.Front
                    ? "front-1"
                    : "rear-0",
                lensFacing: facing,
                sensorOrientationDegrees:
                    facing == ReachyDeviceCameraFacing.Front ? 270 : 90,
                rotationDegrees: rotationDegrees,
                width: width,
                height: height,
                chromaWidth: (width + 1) / 2,
                chromaHeight: (height + 1) / 2,
                crop: selectedCrop,
                mirrored: mirrored,
                colorStandard: ReachyCameraYuvColorStandard.Bt601,
                colorRange: ReachyCameraYuvColorRange.Limited);
        }

        private static void AssertColorNear(
            Color32 actual,
            Color32 expected,
            int tolerance)
        {
            Assert.That(
                Math.Abs(actual.r - expected.r),
                Is.LessThanOrEqualTo(tolerance),
                "red");
            Assert.That(
                Math.Abs(actual.g - expected.g),
                Is.LessThanOrEqualTo(tolerance),
                "green");
            Assert.That(
                Math.Abs(actual.b - expected.b),
                Is.LessThanOrEqualTo(tolerance),
                "blue");
            Assert.That(actual.a, Is.EqualTo(expected.a), "alpha");
        }
    }
}

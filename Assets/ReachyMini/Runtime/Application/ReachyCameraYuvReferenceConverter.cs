#nullable enable

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal static class ReachyCameraYuvReferenceConverter
    {
        public static Color32[] Convert(
            ReadOnlySpan<byte> yPlane,
            ReadOnlySpan<byte> uPlane,
            ReadOnlySpan<byte> vPlane,
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (yPlane.Length != descriptor.YPlaneLength)
            {
                throw new ArgumentException(
                    "The packed Y plane length does not match the texture descriptor.",
                    nameof(yPlane));
            }
            if (uPlane.Length != descriptor.ChromaPlaneLength)
            {
                throw new ArgumentException(
                    "The packed U plane length does not match the texture descriptor.",
                    nameof(uPlane));
            }
            if (vPlane.Length != descriptor.ChromaPlaneLength)
            {
                throw new ArgumentException(
                    "The packed V plane length does not match the texture descriptor.",
                    nameof(vPlane));
            }

            var output = new Color32[
                checked(descriptor.OutputWidth * descriptor.OutputHeight)];
            for (int outputY = 0;
                 outputY < descriptor.OutputHeight;
                 ++outputY)
            {
                for (int outputX = 0;
                     outputX < descriptor.OutputWidth;
                     ++outputX)
                {
                    MapOutputPixelToSource(
                        descriptor,
                        outputX,
                        outputY,
                        out int sourceX,
                        out int sourceY);
                    byte yValue = yPlane[
                        checked(sourceY * descriptor.Width + sourceX)];
                    int chromaX = Math.Min(
                        descriptor.ChromaWidth - 1,
                        sourceX / 2);
                    int chromaY = Math.Min(
                        descriptor.ChromaHeight - 1,
                        sourceY / 2);
                    int chromaIndex = checked(
                        chromaY * descriptor.ChromaWidth + chromaX);
                    output[checked(
                        outputY * descriptor.OutputWidth + outputX)] =
                        ConvertPixel(
                            yValue,
                            uPlane[chromaIndex],
                            vPlane[chromaIndex],
                            descriptor.ColorStandard,
                            descriptor.ColorRange);
                }
            }
            return output;
        }

        internal static void MapOutputPixelToSource(
            ReachyCameraTextureFrameDescriptor descriptor,
            int outputX,
            int outputY,
            out int sourceX,
            out int sourceY)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (outputX < 0 || outputX >= descriptor.OutputWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(outputX));
            }
            if (outputY < 0 || outputY >= descriptor.OutputHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(outputY));
            }

            int orientedX = descriptor.Mirrored
                ? descriptor.OutputWidth - 1 - outputX
                : outputX;
            int cropX;
            int cropY;
            switch (descriptor.RotationDegrees)
            {
                case 0:
                    cropX = orientedX;
                    cropY = outputY;
                    break;
                case 90:
                    cropX = outputY;
                    cropY = descriptor.Crop.Height - 1 - orientedX;
                    break;
                case 180:
                    cropX = descriptor.Crop.Width - 1 - orientedX;
                    cropY = descriptor.Crop.Height - 1 - outputY;
                    break;
                case 270:
                    cropX = descriptor.Crop.Width - 1 - outputY;
                    cropY = orientedX;
                    break;
                default:
                    throw new InvalidOperationException(
                        "The validated texture rotation is unsupported.");
            }

            sourceX = checked(descriptor.Crop.Left + cropX);
            sourceY = checked(descriptor.Crop.Top + cropY);
            if (sourceX < descriptor.Crop.Left ||
                sourceX >= descriptor.Crop.Right ||
                sourceY < descriptor.Crop.Top ||
                sourceY >= descriptor.Crop.Bottom)
            {
                throw new InvalidOperationException(
                    "Texture orientation mapping escaped the validated crop.");
            }
        }

        internal static Color32 ConvertPixel(
            byte yValue,
            byte uValue,
            byte vValue,
            ReachyCameraYuvColorStandard colorStandard,
            ReachyCameraYuvColorRange colorRange)
        {
            double y;
            double cb;
            double cr;
            switch (colorRange)
            {
                case ReachyCameraYuvColorRange.Limited:
                    y = (yValue - 16.0) / 219.0;
                    cb = (uValue - 128.0) / 224.0;
                    cr = (vValue - 128.0) / 224.0;
                    break;
                case ReachyCameraYuvColorRange.Full:
                    y = yValue / 255.0;
                    cb = uValue / 255.0 - 0.5;
                    cr = vValue / 255.0 - 0.5;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(colorRange),
                        colorRange,
                        "A reference conversion requires an explicit color range.");
            }

            double red;
            double green;
            double blue;
            switch (colorStandard)
            {
                case ReachyCameraYuvColorStandard.Bt601:
                    red = y + 1.402 * cr;
                    green = y - 0.344136 * cb - 0.714136 * cr;
                    blue = y + 1.772 * cb;
                    break;
                case ReachyCameraYuvColorStandard.Bt709:
                    red = y + 1.5748 * cr;
                    green = y - 0.187324 * cb - 0.468124 * cr;
                    blue = y + 1.8556 * cb;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(colorStandard),
                        colorStandard,
                        "A reference conversion requires an explicit color standard.");
            }

            return new Color32(
                ToByte(red),
                ToByte(green),
                ToByte(blue),
                byte.MaxValue);
        }

        private static byte ToByte(double value)
        {
            double clamped = Math.Max(0.0, Math.Min(1.0, value));
            return checked((byte)Math.Round(
                clamped * 255.0,
                MidpointRounding.AwayFromZero));
        }
    }
}
#endif

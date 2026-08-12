#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static void PolicyRejectsUpscaling()
        {
            Throws<ArgumentException>(
                () => Policy(allowUpscaling: true),
                "upscaling policy");
        }

        private static void PolicyRejectsInvalidDimensions()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Policy(maximumWidth: 0),
                "zero maximum width");
            Throws<ArgumentOutOfRangeException>(
                () => Policy(maximumHeight: 0),
                "zero maximum height");
        }

        private static void PolicyRejectsInvalidEncodedLimit()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Policy(maximumEncodedBytes: 0),
                "zero encoded limit");
        }

        private static void PolicyRejectsInvalidQuality()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Policy(lossyQuality: 0),
                "zero quality");
            Throws<ArgumentOutOfRangeException>(
                () => Policy(lossyQuality: 101),
                "excess quality");
        }

        private static void PolicyComputesBoundedLandscapeDimensions()
        {
            RemoteVlmImageDimensions dimensions =
                Policy(maximumWidth: 1024, maximumHeight: 1024)
                    .ComputeTargetDimensions(2048, 1024);
            Equal(1024, dimensions.Width, "landscape width");
            Equal(512, dimensions.Height, "landscape height");
        }

        private static void PolicyComputesBoundedPortraitDimensions()
        {
            RemoteVlmImageDimensions dimensions =
                Policy(maximumWidth: 1024, maximumHeight: 768)
                    .ComputeTargetDimensions(1000, 2000);
            Equal(384, dimensions.Width, "portrait width");
            Equal(768, dimensions.Height, "portrait height");
        }

        private static void PolicyDoesNotUpscaleSmallImages()
        {
            RemoteVlmImageDimensions dimensions =
                Policy(maximumWidth: 1024, maximumHeight: 1024)
                    .ComputeTargetDimensions(320, 240);
            Equal(320, dimensions.Width, "small width");
            Equal(240, dimensions.Height, "small height");
        }
    }
}

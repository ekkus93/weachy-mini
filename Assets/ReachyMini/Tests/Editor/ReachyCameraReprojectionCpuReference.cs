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
        private static CpuWarpResult WarpCpu(
            ReachyCameraHomographyPlan plan,
            Color32[] sourceTopLeftPixels)
        {
            if (sourceTopLeftPixels.Length !=
                plan.SourceWidth * plan.SourceHeight)
            {
                throw new ArgumentException(
                    "CPU reference source dimensions do not match the homography plan.",
                    nameof(sourceTopLeftPixels));
            }

            int total = checked(
                plan.OutputWidth * plan.OutputHeight);
            var colors = new Color32[total];
            var validity = new bool[total];
            long validCount = 0L;
            for (int outputY = 0;
                outputY < plan.OutputHeight;
                ++outputY)
            {
                for (int outputX = 0;
                    outputX < plan.OutputWidth;
                    ++outputX)
                {
                    int outputIndex =
                        outputY * plan.OutputWidth + outputX;
                    ReachyVector3D projected =
                        plan.ReachyToPhonePixels.Transform(
                            new ReachyVector3D(
                                outputX,
                                outputY,
                                1.0));
                    bool valid = projected.Z >
                        ReachyCameraValidCoverageCalculator
                            .ShaderDepthEpsilon;
                    double sourceX = 0.0;
                    double sourceY = 0.0;
                    if (valid)
                    {
                        sourceX = projected.X / projected.Z;
                        sourceY = projected.Y / projected.Z;
                        valid =
                            sourceX >= 0.0 &&
                            sourceX <= plan.SourceWidth - 1.0 &&
                            sourceY >= 0.0 &&
                            sourceY <= plan.SourceHeight - 1.0;
                    }

                    validity[outputIndex] = valid;
                    if (!valid)
                    {
                        colors[outputIndex] =
                            new Color32(0, 0, 0, 255);
                        continue;
                    }

                    int sourcePixelX = Clamp(
                        (int)Math.Floor(sourceX + 0.5),
                        0,
                        plan.SourceWidth - 1);
                    int sourcePixelY = Clamp(
                        (int)Math.Floor(sourceY + 0.5),
                        0,
                        plan.SourceHeight - 1);
                    colors[outputIndex] =
                        sourceTopLeftPixels[
                            sourcePixelY * plan.SourceWidth +
                            sourcePixelX];
                    ++validCount;
                }
            }

            return new CpuWarpResult(
                colors,
                validity,
                validCount,
                total);
        }

        private static void AssertReadbackMatches(
            ReachyCameraHomographyPlan plan,
            CpuWarpResult expected,
            Texture2D colorReadback,
            Texture2D validityReadback)
        {
            for (int topY = 0;
                topY < plan.OutputHeight;
                ++topY)
            {
                int readbackY =
                    plan.OutputHeight - 1 - topY;
                for (int x = 0;
                    x < plan.OutputWidth;
                    ++x)
                {
                    int index =
                        topY * plan.OutputWidth + x;
                    Color actualColor =
                        colorReadback.GetPixel(x, readbackY);
                    Color32 expectedColor =
                        expected.Colors[index];
                    Assert.That(
                        actualColor.r,
                        Is.EqualTo(expectedColor.r / 255.0f)
                            .Within(ColorTolerance),
                        $"red mismatch at ({x}, {topY})");
                    Assert.That(
                        actualColor.g,
                        Is.EqualTo(expectedColor.g / 255.0f)
                            .Within(ColorTolerance),
                        $"green mismatch at ({x}, {topY})");
                    Assert.That(
                        actualColor.b,
                        Is.EqualTo(expectedColor.b / 255.0f)
                            .Within(ColorTolerance),
                        $"blue mismatch at ({x}, {topY})");

                    float actualValidity =
                        validityReadback.GetPixel(x, readbackY).r;
                    float expectedValidity =
                        expected.Validity[index] ? 1.0f : 0.0f;
                    Assert.That(
                        actualValidity,
                        Is.EqualTo(expectedValidity)
                            .Within(ValidityTolerance),
                        $"validity mismatch at ({x}, {topY})");
                }
            }
        }

        private static int CountDifferentPixels(
            CpuWarpResult left,
            CpuWarpResult right)
        {
            Assert.That(
                right.Colors.Length,
                Is.EqualTo(left.Colors.Length));
            int count = 0;
            for (int index = 0;
                index < left.Colors.Length;
                ++index)
            {
                if (left.Validity[index] !=
                    right.Validity[index] ||
                    !left.Colors[index].Equals(
                        right.Colors[index]))
                {
                    ++count;
                }
            }
            return count;
        }

        private static Shader RequireShader()
        {
            Assert.That(
                SystemInfo.graphicsDeviceType,
                Is.Not.EqualTo(GraphicsDeviceType.Null),
                "RMA-104 GPU comparisons require a real graphics device.");
            Shader shader = Shader.Find(
                ReachyCameraHomographyWarpRenderer.ShaderName);
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            return shader;
        }

        private static Texture2D ReadBack(
            RenderTexture source)
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
                    new Rect(
                        0,
                        0,
                        source.width,
                        source.height),
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

        private static int Clamp(
            int value,
            int minimum,
            int maximum)
        {
            return Math.Max(
                minimum,
                Math.Min(maximum, value));
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

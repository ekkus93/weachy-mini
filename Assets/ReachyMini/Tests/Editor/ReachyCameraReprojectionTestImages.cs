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
        private sealed class TestImage : IDisposable
        {
            private bool disposed;

            private TestImage(
                Texture2D texture,
                Color32[] topLeftPixels)
            {
                Texture = texture;
                TopLeftPixels = topLeftPixels;
            }

            public Texture2D Texture { get; }

            public Color32[] TopLeftPixels { get; }

            public static TestImage CreatePattern(
                int width,
                int height)
            {
                var topLeft = new Color32[
                    checked(width * height)];
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        topLeft[y * width + x] =
                            new Color32(
                                (byte)((x * 37 + y * 11 + 23) %
                                    256),
                                (byte)((x * 17 + y * 43 + 59) %
                                    256),
                                (byte)((x * 71 + y * 7 + 101) %
                                    256),
                                255);
                    }
                }
                return Create(width, height, topLeft);
            }

            public static TestImage CreateSolid(
                int width,
                int height,
                Color32 color)
            {
                var topLeft = new Color32[
                    checked(width * height)];
                for (int index = 0;
                    index < topLeft.Length;
                    ++index)
                {
                    topLeft[index] = color;
                }
                return Create(width, height, topLeft);
            }

            private static TestImage Create(
                int width,
                int height,
                Color32[] topLeft)
            {
                var unityPixels = new Color32[topLeft.Length];
                for (int topY = 0;
                    topY < height;
                    ++topY)
                {
                    int unityY = height - 1 - topY;
                    for (int x = 0; x < width; ++x)
                    {
                        unityPixels[unityY * width + x] =
                            topLeft[topY * width + x];
                    }
                }

                var texture = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels32(unityPixels);
                texture.Apply(false, false);
                return new TestImage(texture, topLeft);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                Destroy(Texture);
            }
        }

        private sealed class CpuWarpResult
        {
            public CpuWarpResult(
                Color32[] colors,
                bool[] validity,
                long validPixelCount,
                int totalPixelCount)
            {
                Colors = colors;
                Validity = validity;
                ValidPixelCount = validPixelCount;
                TotalPixelCount = totalPixelCount;
            }

            public Color32[] Colors { get; }

            public bool[] Validity { get; }

            public long ValidPixelCount { get; }

            public long TotalPixelCount { get; }
        }
    }
}

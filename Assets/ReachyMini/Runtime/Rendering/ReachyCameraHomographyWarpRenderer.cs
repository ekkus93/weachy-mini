#nullable enable

using System;
using ReachyMini.AppState;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Rendering
{
    public sealed class ReachyCameraHomographyGpuFrame
    {
        internal ReachyCameraHomographyGpuFrame(
            ReachyCameraHomographyPlan plan,
            RenderTexture color,
            RenderTexture validity,
            ReachyCameraCoverageSnapshot coverage)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Color = color ?? throw new ArgumentNullException(nameof(color));
            Validity = validity ??
                throw new ArgumentNullException(nameof(validity));
            Coverage = coverage ??
                throw new ArgumentNullException(nameof(coverage));
            ReachyCameraCoverageMeasurement? measurement =
                coverage.Measurement;
            if (!coverage.HasCoverage ||
                measurement == null ||
                measurement.SourceSessionId !=
                    plan.SourceSessionId ||
                measurement.SourceSequence !=
                    plan.SourceSequence ||
                measurement.SourceTimestampNanoseconds !=
                    plan.SourceTimestampNanoseconds ||
                measurement.AuthoritativeSequence !=
                    plan.AuthoritativeSequence ||
                measurement.ContinuityId !=
                    plan.ContinuityId ||
                measurement.OutputWidth != plan.OutputWidth ||
                measurement.OutputHeight != plan.OutputHeight ||
                measurement.ReachyToPhonePixels !=
                    plan.ReachyToPhonePixels)
            {
                throw new ArgumentException(
                    "Coverage metadata does not match the GPU homography frame.",
                    nameof(coverage));
            }
        }

        public ReachyCameraHomographyPlan Plan { get; }

        public RenderTexture Color { get; }

        public RenderTexture Validity { get; }

        public ReachyCameraCoverageSnapshot Coverage { get; }
    }

    public sealed class ReachyCameraHomographyWarpRenderer : IDisposable
    {
        public const string ShaderName =
            "Hidden/ReachyMini/CameraHomographyWarp";

        private static readonly int ReachyToPhonePixelsId =
            Shader.PropertyToID("_ReachyToPhonePixels");
        private static readonly int SourceSizeId =
            Shader.PropertyToID("_SourceSize");
        private static readonly int OutputSizeId =
            Shader.PropertyToID("_OutputSize");

        private Material? material;
        private RenderTexture? colorTexture;
        private RenderTexture? validityTexture;
        private bool disposed;

        public ReachyCameraHomographyWarpRenderer(Shader? shader = null)
        {
            Shader selected = shader ?? Shader.Find(ShaderName);
            if (selected == null || !selected.isSupported)
            {
                throw new InvalidOperationException(
                    $"Required homography shader '{ShaderName}' is unavailable or unsupported.");
            }
            material = new Material(selected)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        public RenderTexture? ColorTexture => colorTexture;

        public RenderTexture? ValidityTexture => validityTexture;

        public ReachyCameraHomographyGpuFrame Warp(
            Texture normalizedPhoneTexture,
            ReachyCameraHomographyPlan plan,
            ReachyCameraCoverageSnapshot coverage)
        {
            if (normalizedPhoneTexture == null)
            {
                throw new ArgumentNullException(
                    nameof(normalizedPhoneTexture));
            }
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (coverage == null)
            {
                throw new ArgumentNullException(nameof(coverage));
            }
            ThrowIfDisposed();
            if (normalizedPhoneTexture.width != plan.SourceWidth ||
                normalizedPhoneTexture.height != plan.SourceHeight)
            {
                ResetOutputs();
                throw new ArgumentException(
                    "The GPU source texture dimensions do not match the homography plan.",
                    nameof(normalizedPhoneTexture));
            }

            EnsureOutputs(plan.OutputWidth, plan.OutputHeight);
            Material activeMaterial = material ??
                throw new ObjectDisposedException(
                    nameof(ReachyCameraHomographyWarpRenderer));
            activeMaterial.SetMatrix(
                ReachyToPhonePixelsId,
                ReachyCameraCalibrationUnityAdapter.ToUnityMatrix(
                    plan.ReachyToPhonePixels));
            activeMaterial.SetVector(
                SourceSizeId,
                new Vector4(
                    plan.SourceWidth,
                    plan.SourceHeight,
                    1.0f / plan.SourceWidth,
                    1.0f / plan.SourceHeight));
            activeMaterial.SetVector(
                OutputSizeId,
                new Vector4(
                    plan.OutputWidth,
                    plan.OutputHeight,
                    1.0f / plan.OutputWidth,
                    1.0f / plan.OutputHeight));

            Graphics.Blit(
                normalizedPhoneTexture,
                RequireTexture(colorTexture, "color"),
                activeMaterial,
                0);
            Graphics.Blit(
                normalizedPhoneTexture,
                RequireTexture(validityTexture, "validity"),
                activeMaterial,
                1);

            return new ReachyCameraHomographyGpuFrame(
                plan,
                RequireTexture(colorTexture, "color"),
                RequireTexture(validityTexture, "validity"),
                coverage);
        }

        public void ResetOutputs()
        {
            ReleaseTexture(ref colorTexture);
            ReleaseTexture(ref validityTexture);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            ResetOutputs();
            if (material != null)
            {
                DestroyObject(material);
                material = null;
            }
        }

        private void EnsureOutputs(int width, int height)
        {
            if (colorTexture == null ||
                colorTexture.width != width ||
                colorTexture.height != height)
            {
                ReleaseTexture(ref colorTexture);
                colorTexture = CreateTexture(
                    width,
                    height,
                    RenderTextureFormat.ARGB32,
                    FilterMode.Bilinear,
                    "Reachy camera homography color");
            }

            RenderTextureFormat validityFormat =
                SystemInfo.SupportsRenderTextureFormat(
                    RenderTextureFormat.R8)
                    ? RenderTextureFormat.R8
                    : RenderTextureFormat.RFloat;
            if (validityTexture == null ||
                validityTexture.width != width ||
                validityTexture.height != height ||
                validityTexture.format != validityFormat)
            {
                ReleaseTexture(ref validityTexture);
                validityTexture = CreateTexture(
                    width,
                    height,
                    validityFormat,
                    FilterMode.Point,
                    "Reachy camera homography validity");
            }
        }

        private static RenderTexture CreateTexture(
            int width,
            int height,
            RenderTextureFormat format,
            FilterMode filterMode,
            string name)
        {
            var texture = new RenderTexture(
                width,
                height,
                0,
                format,
                RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!texture.Create())
            {
                DestroyObject(texture);
                throw new InvalidOperationException(
                    $"Could not allocate {name} at {width}x{height}.");
            }
            return texture;
        }

        private static RenderTexture RequireTexture(
            RenderTexture? texture,
            string label)
        {
            return texture ?? throw new InvalidOperationException(
                $"The homography {label} render target is unavailable.");
        }

        private static void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }
            if (ReferenceEquals(RenderTexture.active, texture))
            {
                RenderTexture.active = null;
            }
            if (texture.IsCreated())
            {
                texture.Release();
            }
            DestroyObject(texture);
            texture = null;
        }

        private static void DestroyObject(Object value)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyCameraHomographyWarpRenderer));
            }
        }
    }
}

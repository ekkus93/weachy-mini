#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.Perception;
using ReachyMini.Rendering;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public static class ReachyUnityVisionFrameFactory
    {
        public static ReachyVisionFrame CreateOwnedTrackingFrame(
            ReachyCameraHomographyGpuFrame source,
            string ownerId,
            ulong generation)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            ReachyCameraCoverageMeasurement measurement =
                source.Coverage.Measurement ??
                throw new ArgumentException(
                    "Tracking frame creation requires available coverage.",
                    nameof(source));
            if (!source.Coverage.CanCreateVisualObservations)
            {
                throw new ArgumentException(
                    "Tracking frame creation requires observation-eligible coverage.",
                    nameof(source));
            }

            var identity = new ReachyVisionFrameIdentity(
                source.Plan.CameraId,
                source.Plan.SourceSessionId,
                source.Plan.SourceSequence,
                source.Plan.SourceTimestampNanoseconds,
                source.Plan.AuthoritativeSequence,
                source.Plan.ContinuityId);
            var coverage = new ReachyVisionCoverage(
                MapCoverage(source.Coverage.State),
                measurement.ValidPixelCount,
                measurement.TotalPixelCount,
                hasValidityMask: true,
                source.Coverage.ShouldStopVisionDrivenTurning,
                source.Coverage.Message);
            var resources = new ReachyUnityTrackingFrameResources(
                ownerId,
                generation,
                source.Color,
                source.Validity);
            try
            {
                return new ReachyVisionFrame(
                    VisionFrameOrigin.TransformedReachyEye,
                    identity,
                    coverage,
                    resources);
            }
            catch
            {
                resources.DisposeAfterConstructionFailure();
                throw;
            }
        }

        private static VisionCoverageState MapCoverage(
            ReachyCameraCoverageState state)
        {
            switch (state)
            {
                case ReachyCameraCoverageState.Normal:
                    return VisionCoverageState.Normal;
                case ReachyCameraCoverageState.Degraded:
                    return VisionCoverageState.Degraded;
                case ReachyCameraCoverageState.Unusable:
                    return VisionCoverageState.Unusable;
                default:
                    return VisionCoverageState.Unavailable;
            }
        }
    }

    public sealed class ReachyUnityTrackingFrameResources :
        IReachyVisionFrameResources,
        IReachyTrackingPixelSource
    {
        private readonly object sync = new object();
        private RenderTexture? color;
        private RenderTexture? validity;
        private TaskCompletionSource<bool>? disposalCompletion;
        private int activeReadbacks;
        private bool disposeRequested;
        private bool disposed;

        public ReachyUnityTrackingFrameResources(
            string ownerId,
            ulong generation,
            RenderTexture sourceColor,
            RenderTexture sourceValidity)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException(
                    "Tracking frame resource owners require an identifier.",
                    nameof(ownerId));
            }
            if (generation == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }
            if (sourceColor == null)
            {
                throw new ArgumentNullException(nameof(sourceColor));
            }
            if (sourceValidity == null)
            {
                throw new ArgumentNullException(nameof(sourceValidity));
            }
            if (!sourceColor.IsCreated() || !sourceValidity.IsCreated() ||
                sourceColor.width != sourceValidity.width ||
                sourceColor.height != sourceValidity.height)
            {
                throw new ArgumentException(
                    "Tracking color and validity textures must be created with matching dimensions.");
            }

            OwnerId = ownerId;
            Generation = generation;
            Width = sourceColor.width;
            Height = sourceColor.height;
            color = CloneTexture(
                sourceColor,
                RenderTextureFormat.ARGB32,
                FilterMode.Bilinear,
                $"{ownerId} tracking color");
            try
            {
                RenderTextureFormat validityFormat =
                    SystemInfo.SupportsRenderTextureFormat(
                        RenderTextureFormat.R8)
                        ? RenderTextureFormat.R8
                        : RenderTextureFormat.RFloat;
                validity = CloneTexture(
                    sourceValidity,
                    validityFormat,
                    FilterMode.Point,
                    $"{ownerId} tracking validity");
            }
            catch
            {
                ReleaseTexture(ref color);
                throw;
            }
        }

        public string OwnerId { get; }

        public ulong Generation { get; }

        public int Width { get; }

        public int Height { get; }

        public bool IsDisposed
        {
            get
            {
                lock (sync)
                {
                    return disposed || disposeRequested;
                }
            }
        }

        public bool HasResource(VisionResourceKind kind)
        {
            return kind == VisionResourceKind.Color ||
                kind == VisionResourceKind.ValidityMask;
        }

        public VisionPixelEncoding GetEncoding(VisionResourceKind kind)
        {
            switch (kind)
            {
                case VisionResourceKind.Color:
                    return VisionPixelEncoding.ProviderNative;
                case VisionResourceKind.ValidityMask:
                    return VisionPixelEncoding.ValidityMask8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public bool TryGetResource<TResource>(
            VisionResourceKind kind,
            out TResource? resource)
            where TResource : class
        {
            lock (sync)
            {
                if (disposed || disposeRequested)
                {
                    resource = null;
                    return false;
                }
                if (kind == VisionResourceKind.Color &&
                    this is TResource pixelSource)
                {
                    resource = pixelSource;
                    return true;
                }
                Object? value = kind == VisionResourceKind.Color
                    ? color
                    : kind == VisionResourceKind.ValidityMask
                        ? validity
                        : null;
                resource = value as TResource;
                return resource != null;
            }
        }

        public ValueTask<ReachyTrackingFramePixels> StageAsync(
            ReachyVisionFrame frame,
            int maximumDimension,
            CancellationToken cancellationToken)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }
            if (!ReferenceEquals(frame.Resources, this))
            {
                throw new ArgumentException(
                    "Tracking pixel staging must use the frame that owns this resource lease.",
                    nameof(frame));
            }
            if (maximumDimension <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDimension));
            }
            cancellationToken.ThrowIfCancellationRequested();

            RenderTexture sourceColor;
            RenderTexture sourceValidity;
            lock (sync)
            {
                ThrowIfUnavailable();
                sourceColor = color ?? throw new ObjectDisposedException(nameof(color));
                sourceValidity = validity ??
                    throw new ObjectDisposedException(nameof(validity));
                activeReadbacks = checked(activeReadbacks + 1);
            }

            return new ValueTask<ReachyTrackingFramePixels>(
                ReadbackAsync(
                    frame.Identity,
                    sourceColor,
                    sourceValidity,
                    maximumDimension,
                    cancellationToken));
        }

        public ValueTask DisposeAsync()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return default;
                }
                disposeRequested = true;
                if (activeReadbacks == 0)
                {
                    ReleaseOwnedTextures();
                    disposed = true;
                    return default;
                }
                disposalCompletion ??=
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                return new ValueTask(disposalCompletion.Task);
            }
        }

        internal void DisposeAfterConstructionFailure()
        {
            lock (sync)
            {
                if (activeReadbacks != 0)
                {
                    throw new InvalidOperationException(
                        "Construction-failure disposal cannot run with active readbacks.");
                }
                if (!disposed)
                {
                    disposeRequested = true;
                    ReleaseOwnedTextures();
                    disposed = true;
                }
            }
        }

        private async Task<ReachyTrackingFramePixels> ReadbackAsync(
            ReachyVisionFrameIdentity identity,
            RenderTexture sourceColor,
            RenderTexture sourceValidity,
            int maximumDimension,
            CancellationToken cancellationToken)
        {
            RenderTexture? stagedColor = null;
            RenderTexture? stagedValidity = null;
            try
            {
                (int width, int height) = CalculateStagingSize(
                    sourceColor.width,
                    sourceColor.height,
                    maximumDimension);
                stagedColor = CreateTexture(
                    width,
                    height,
                    RenderTextureFormat.ARGB32,
                    FilterMode.Bilinear,
                    $"{OwnerId} tracking stage color");
                RenderTextureFormat validityFormat =
                    SystemInfo.SupportsRenderTextureFormat(
                        RenderTextureFormat.R8)
                        ? RenderTextureFormat.R8
                        : RenderTextureFormat.RFloat;
                stagedValidity = CreateTexture(
                    width,
                    height,
                    validityFormat,
                    FilterMode.Point,
                    $"{OwnerId} tracking stage validity");
                Graphics.Blit(sourceColor, stagedColor);
                Graphics.Blit(sourceValidity, stagedValidity);

                Task<byte[]> colorTask = RequestBytesAsync(
                    stagedColor,
                    TextureFormat.RGBA32,
                    4,
                    cancellationToken);
                Task<byte[]> validityTask = RequestBytesAsync(
                    stagedValidity,
                    validityFormat == RenderTextureFormat.R8
                        ? TextureFormat.R8
                        : TextureFormat.RFloat,
                    validityFormat == RenderTextureFormat.R8 ? 1 : 4,
                    cancellationToken);
                await Task.WhenAll(colorTask, validityTask);
                cancellationToken.ThrowIfCancellationRequested();

                byte[] colorTopLeft = FlipRows(
                    await colorTask,
                    width,
                    height,
                    4);
                byte[] rawValidity = await validityTask;
                byte[] validityTopLeft = validityFormat ==
                    RenderTextureFormat.R8
                    ? FlipRows(rawValidity, width, height, 1)
                    : ConvertFloatValidityToBytes(
                        FlipRows(rawValidity, width, height, 4));
                return new ReachyTrackingFramePixels(
                    identity,
                    width,
                    height,
                    colorTopLeft,
                    validityTopLeft);
            }
            finally
            {
                ReleaseTexture(ref stagedColor);
                ReleaseTexture(ref stagedValidity);
                CompleteReadback();
            }
        }

        private static Task<byte[]> RequestBytesAsync(
            RenderTexture texture,
            TextureFormat format,
            int bytesPerPixel,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncGPUReadback.Request(
                texture,
                0,
                format,
                request =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }
                    if (request.hasError)
                    {
                        completion.TrySetException(
                            new InvalidOperationException(
                                "Async GPU tracking readback failed."));
                        return;
                    }
                    NativeArray<byte> data = request.GetData<byte>();
                    int expected = checked(
                        texture.width * texture.height * bytesPerPixel);
                    if (data.Length != expected)
                    {
                        completion.TrySetException(
                            new InvalidOperationException(
                                $"Async GPU tracking readback length {data.Length} did not match {expected}."));
                        return;
                    }
                    completion.TrySetResult(data.ToArray());
                });
            return completion.Task;
        }

        private void CompleteReadback()
        {
            TaskCompletionSource<bool>? completion = null;
            lock (sync)
            {
                activeReadbacks--;
                if (activeReadbacks < 0)
                {
                    throw new InvalidOperationException(
                        "Tracking readback ownership underflowed.");
                }
                if (disposeRequested && activeReadbacks == 0 && !disposed)
                {
                    ReleaseOwnedTextures();
                    disposed = true;
                    completion = disposalCompletion;
                    disposalCompletion = null;
                }
            }
            completion?.TrySetResult(true);
        }

        internal static (int Width, int Height) CalculateStagingSize(
            int width,
            int height,
            int maximumDimension)
        {
            if (width <= 0 || height <= 0 || maximumDimension <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            int largest = Math.Max(width, height);
            if (largest <= maximumDimension)
            {
                return (width, height);
            }
            double scale = (double)maximumDimension / largest;
            return (
                Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
        }

        internal static byte[] FlipRows(
            byte[] source,
            int width,
            int height,
            int bytesPerPixel)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            int rowLength = checked(width * bytesPerPixel);
            if (source.Length != checked(rowLength * height))
            {
                throw new ArgumentException(
                    "Tracking pixel buffer dimensions do not match its length.",
                    nameof(source));
            }
            var result = new byte[source.Length];
            for (int y = 0; y < height; ++y)
            {
                Buffer.BlockCopy(
                    source,
                    checked((height - 1 - y) * rowLength),
                    result,
                    checked(y * rowLength),
                    rowLength);
            }
            return result;
        }

        private static byte[] ConvertFloatValidityToBytes(byte[] source)
        {
            if (source.Length % 4 != 0)
            {
                throw new ArgumentException(
                    "Float validity data must contain four bytes per pixel.",
                    nameof(source));
            }
            var result = new byte[source.Length / 4];
            for (int index = 0; index < result.Length; ++index)
            {
                float value = BitConverter.ToSingle(source, index * 4);
                result[index] = value >= 0.5f ? (byte)255 : (byte)0;
            }
            return result;
        }

        private static RenderTexture CloneTexture(
            RenderTexture source,
            RenderTextureFormat format,
            FilterMode filterMode,
            string name)
        {
            RenderTexture destination = CreateTexture(
                source.width,
                source.height,
                format,
                filterMode,
                name);
            try
            {
                Graphics.Blit(source, destination);
                return destination;
            }
            catch
            {
                if (destination.IsCreated())
                {
                    destination.Release();
                }
                DestroyObject(destination);
                throw;
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

        private void ReleaseOwnedTextures()
        {
            ReleaseTexture(ref color);
            ReleaseTexture(ref validity);
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

        private void ThrowIfUnavailable()
        {
            if (disposed || disposeRequested)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyUnityTrackingFrameResources));
            }
        }
    }
}

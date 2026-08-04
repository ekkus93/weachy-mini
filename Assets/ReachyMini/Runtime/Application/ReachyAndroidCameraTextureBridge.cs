#nullable enable

using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.AppState
{
    public enum ReachyCameraYuvColorStandard
    {
        Unknown = 0,
        Bt601 = 1,
        Bt709 = 2,
    }

    public enum ReachyCameraYuvColorRange
    {
        Unknown = 0,
        Limited = 1,
        Full = 2,
    }

    public sealed class ReachyCameraTextureFrameDescriptor
    {
        public ReachyCameraTextureFrameDescriptor(
            ulong sessionId,
            ulong sequence,
            long timestampNanoseconds,
            string cameraId,
            ReachyDeviceCameraFacing lensFacing,
            int sensorOrientationDegrees,
            int rotationDegrees,
            int width,
            int height,
            int chromaWidth,
            int chromaHeight,
            ReachyCameraFrameCrop crop,
            bool mirrored,
            ReachyCameraYuvColorStandard colorStandard,
            ReachyCameraYuvColorRange colorRange)
        {
            if (sessionId == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionId),
                    sessionId,
                    "A texture frame requires a nonzero session identifier.");
            }
            if (sequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence),
                    sequence,
                    "A texture frame sequence starts at one.");
            }
            if (timestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampNanoseconds),
                    timestampNanoseconds,
                    "A texture frame timestamp must be positive.");
            }
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new ArgumentException(
                    "A texture frame requires a camera identifier.",
                    nameof(cameraId));
            }
            if (lensFacing != ReachyDeviceCameraFacing.Front &&
                lensFacing != ReachyDeviceCameraFacing.Rear &&
                lensFacing != ReachyDeviceCameraFacing.External)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lensFacing),
                    lensFacing,
                    "A texture frame requires an explicit lens facing.");
            }
            ValidateRightAngle(
                sensorOrientationDegrees,
                nameof(sensorOrientationDegrees));
            ValidateRightAngle(rotationDegrees, nameof(rotationDegrees));
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "Texture frame dimensions must be positive.");
            }
            int expectedChromaWidth = checked((width + 1) / 2);
            int expectedChromaHeight = checked((height + 1) / 2);
            if (chromaWidth != expectedChromaWidth ||
                chromaHeight != expectedChromaHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chromaWidth),
                    $"YUV420 chroma dimensions must be {expectedChromaWidth}x{expectedChromaHeight}.");
            }
            if (crop.Right > width || crop.Bottom > height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(crop),
                    crop,
                    "The texture crop must remain inside the source image.");
            }
            bool shouldMirror =
                lensFacing == ReachyDeviceCameraFacing.Front;
            if (mirrored != shouldMirror)
            {
                throw new ArgumentException(
                    "Front-camera texture frames must be mirrored and non-front frames must not be mirrored.",
                    nameof(mirrored));
            }
            if (colorStandard == ReachyCameraYuvColorStandard.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colorStandard),
                    colorStandard,
                    "A texture frame requires a YUV color standard.");
            }
            if (colorRange == ReachyCameraYuvColorRange.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colorRange),
                    colorRange,
                    "A texture frame requires a YUV color range.");
            }

            SessionId = sessionId;
            Sequence = sequence;
            TimestampNanoseconds = timestampNanoseconds;
            CameraId = cameraId;
            LensFacing = lensFacing;
            SensorOrientationDegrees = sensorOrientationDegrees;
            RotationDegrees = rotationDegrees;
            Width = width;
            Height = height;
            ChromaWidth = chromaWidth;
            ChromaHeight = chromaHeight;
            Crop = crop;
            Mirrored = mirrored;
            ColorStandard = colorStandard;
            ColorRange = colorRange;
        }

        public ulong SessionId { get; }

        public ulong Sequence { get; }

        public long TimestampNanoseconds { get; }

        public string CameraId { get; }

        public ReachyDeviceCameraFacing LensFacing { get; }

        public int SensorOrientationDegrees { get; }

        public int RotationDegrees { get; }

        public int Width { get; }

        public int Height { get; }

        public int ChromaWidth { get; }

        public int ChromaHeight { get; }

        public ReachyCameraFrameCrop Crop { get; }

        public bool Mirrored { get; }

        public ReachyCameraYuvColorStandard ColorStandard { get; }

        public ReachyCameraYuvColorRange ColorRange { get; }

        public int YPlaneLength => checked(Width * Height);

        public int ChromaPlaneLength => checked(ChromaWidth * ChromaHeight);

        public int OutputWidth =>
            RotationDegrees == 90 || RotationDegrees == 270
                ? Crop.Height
                : Crop.Width;

        public int OutputHeight =>
            RotationDegrees == 90 || RotationDegrees == 270
                ? Crop.Width
                : Crop.Height;

        public string Summary =>
            $"session={SessionId}; sequence={Sequence}; timestamp_ns={TimestampNanoseconds}; " +
            $"camera={CameraId}; facing={LensFacing}; source={Width}x{Height}; " +
            $"crop={Crop}; output={OutputWidth}x{OutputHeight}; rotation={RotationDegrees}; " +
            $"mirrored={Mirrored}; color={ColorStandard}/{ColorRange}";

        private static void ValidateRightAngle(int value, string name)
        {
            if (value != 0 && value != 90 && value != 180 && value != 270)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera orientation must be 0, 90, 180, or 270 degrees.");
            }
        }
    }

    public interface IReachyCameraTextureFrameLease : IDisposable
    {
        ReachyCameraTextureFrameDescriptor Descriptor { get; }

        IntPtr YBuffer { get; }

        int YLength { get; }

        IntPtr UBuffer { get; }

        int ULength { get; }

        IntPtr VBuffer { get; }

        int VLength { get; }
    }

    public enum ReachyCameraTextureBridgeState
    {
        Waiting = 0,
        Ready = 1,
        Faulted = 2,
        Unsupported = 3,
    }

    public sealed class ReachyCameraTextureBridgeSnapshot
    {
        public ReachyCameraTextureBridgeSnapshot(
            ReachyCameraTextureBridgeState state,
            string message,
            ReachyCameraTextureFrameDescriptor? frame,
            ulong uploadedFrameCount,
            ulong staleFrameCount,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Texture bridge state requires diagnostics.",
                    nameof(message));
            }
            if (state == ReachyCameraTextureBridgeState.Ready && frame == null)
            {
                throw new ArgumentNullException(
                    nameof(frame),
                    "Ready texture state requires frame metadata.");
            }
            if (state != ReachyCameraTextureBridgeState.Ready && frame != null)
            {
                throw new ArgumentException(
                    "Non-ready texture state cannot retain a sampleable frame.",
                    nameof(frame));
            }

            State = state;
            Message = message;
            Frame = frame;
            UploadedFrameCount = uploadedFrameCount;
            StaleFrameCount = staleFrameCount;
            Revision = revision;
        }

        public ReachyCameraTextureBridgeState State { get; }

        public string Message { get; }

        public ReachyCameraTextureFrameDescriptor? Frame { get; }

        public ulong UploadedFrameCount { get; }

        public ulong StaleFrameCount { get; }

        public ulong Revision { get; }

        public bool HasTexture =>
            State == ReachyCameraTextureBridgeState.Ready && Frame != null;
    }

    public sealed class ReachyCameraTextureBridgeChangedEventArgs : EventArgs
    {
        public ReachyCameraTextureBridgeChangedEventArgs(
            ReachyCameraTextureBridgeSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyCameraTextureBridgeSnapshot Snapshot { get; }
    }

    [DisallowMultipleComponent]
    public sealed class ReachyAndroidCameraTextureBridge : MonoBehaviour
    {
        public const string ShaderName =
            "Hidden/ReachyMini/CameraYuv420ToRgb";

        private static readonly int YTextureId =
            Shader.PropertyToID("_YTexture");
        private static readonly int UTextureId =
            Shader.PropertyToID("_UTexture");
        private static readonly int VTextureId =
            Shader.PropertyToID("_VTexture");
        private static readonly int CropScaleOffsetId =
            Shader.PropertyToID("_CropScaleOffset");
        private static readonly int RotationQuarterTurnsId =
            Shader.PropertyToID("_RotationQuarterTurns");
        private static readonly int MirrorXId =
            Shader.PropertyToID("_MirrorX");
        private static readonly int ColorStandardId =
            Shader.PropertyToID("_ColorStandard");
        private static readonly int ColorRangeId =
            Shader.PropertyToID("_ColorRange");

        private ReachyAndroidCameraAcquisition? acquisition;
        private Material? conversionMaterial;
        private Texture2D? yTexture;
        private Texture2D? uTexture;
        private Texture2D? vTexture;
        private RenderTexture? outputTexture;
        private ulong lastUploadedSessionId;
        private ulong lastUploadedSequence;
        private bool disposed;
        private ReachyCameraTextureBridgeSnapshot current =
            new ReachyCameraTextureBridgeSnapshot(
                ReachyCameraTextureBridgeState.Waiting,
                "Camera texture bridge is waiting for a running acquisition session.",
                null,
                0UL,
                0UL,
                0UL);

        public ReachyCameraTextureBridgeSnapshot Current => current;

        public RenderTexture? OutputTexture =>
            current.HasTexture ? outputTexture : null;

        public Texture? PreviewTexture => OutputTexture;

        public Texture? AnalysisTexture => OutputTexture;

        public event EventHandler<ReachyCameraTextureBridgeChangedEventArgs>?
            Changed;

        public void Configure(
            ReachyAndroidCameraAcquisition cameraAcquisition,
            Shader? conversionShader = null)
        {
            if (cameraAcquisition == null)
            {
                throw new ArgumentNullException(nameof(cameraAcquisition));
            }
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidCameraTextureBridge));
            }
            if (acquisition != null && acquisition != cameraAcquisition)
            {
                throw new InvalidOperationException(
                    "The texture bridge acquisition service cannot change after configuration.");
            }

            Shader shader = conversionShader ?? Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Publish(
                    ReachyCameraTextureBridgeState.Unsupported,
                    $"Required camera YUV conversion shader '{ShaderName}' is unavailable or unsupported.",
                    null,
                    current.UploadedFrameCount,
                    current.StaleFrameCount);
                throw new InvalidOperationException(current.Message);
            }

            acquisition = cameraAcquisition;
            acquisition.State.Changed -= OnAcquisitionChanged;
            acquisition.State.Changed += OnAcquisitionChanged;
            if (conversionMaterial == null)
            {
                conversionMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            PublishWaiting(
                "Camera texture bridge is configured and waiting for a frame.");
        }

        public bool PumpOnceForTests()
        {
            return PumpOnce();
        }

        private void Update()
        {
            PumpOnce();
        }

        private bool PumpOnce()
        {
            if (disposed || acquisition == null || conversionMaterial == null)
            {
                return false;
            }

            ReachyCameraAcquisitionSnapshot acquisitionSnapshot =
                acquisition.State.Current;
            if (acquisitionSnapshot.State != ReachyCameraAcquisitionState.Running ||
                acquisitionSnapshot.SessionId == 0UL)
            {
                if (current.State == ReachyCameraTextureBridgeState.Ready)
                {
                    InvalidateOutput(
                        $"Camera acquisition is {acquisitionSnapshot.State}; no texture is sampleable.");
                }
                return false;
            }

            ulong afterSequence =
                lastUploadedSessionId == acquisitionSnapshot.SessionId
                    ? lastUploadedSequence
                    : 0UL;
            IReachyCameraTextureFrameLease? lease =
                acquisition.AcquireLatestTextureFrame(afterSequence);
            if (lease == null)
            {
                return false;
            }

            using (lease)
            {
                ReachyCameraTextureFrameDescriptor descriptor =
                    lease.Descriptor;
                if (!FrameMatchesActiveSession(
                        descriptor,
                        acquisitionSnapshot,
                        afterSequence))
                {
                    Publish(
                        current.State == ReachyCameraTextureBridgeState.Ready
                            ? ReachyCameraTextureBridgeState.Ready
                            : ReachyCameraTextureBridgeState.Waiting,
                        "Rejected a stale or mismatched detached camera texture frame.",
                        current.State == ReachyCameraTextureBridgeState.Ready
                            ? current.Frame
                            : null,
                        current.UploadedFrameCount,
                        checked(current.StaleFrameCount + 1UL));
                    return false;
                }

                try
                {
                    ValidateLease(lease);
                    EnsurePlaneTextures(descriptor);
                    UploadPlane(
                        RequireTexture(yTexture, "Y"),
                        lease.YBuffer,
                        lease.YLength,
                        "Y");
                    UploadPlane(
                        RequireTexture(uTexture, "U"),
                        lease.UBuffer,
                        lease.ULength,
                        "U");
                    UploadPlane(
                        RequireTexture(vTexture, "V"),
                        lease.VBuffer,
                        lease.VLength,
                        "V");
                    EnsureOutputTexture(descriptor);
                    ConfigureMaterial(descriptor);
                    Graphics.Blit(
                        RequireTexture(yTexture, "Y"),
                        RequireOutputTexture(),
                        conversionMaterial);

                    lastUploadedSessionId = descriptor.SessionId;
                    lastUploadedSequence = descriptor.Sequence;
                    Publish(
                        ReachyCameraTextureBridgeState.Ready,
                        $"Uploaded camera texture frame {descriptor.Sequence} with timestamp {descriptor.TimestampNanoseconds} ns.",
                        descriptor,
                        checked(current.UploadedFrameCount + 1UL),
                        current.StaleFrameCount);
                    return true;
                }
                catch (Exception exception)
                {
                    InvalidateOutput(
                        $"camera_texture_upload_failed: {exception.Message}");
                    Publish(
                        ReachyCameraTextureBridgeState.Faulted,
                        $"camera_texture_upload_failed: {exception.Message}",
                        null,
                        current.UploadedFrameCount,
                        current.StaleFrameCount);
                    return false;
                }
            }
        }

        private void OnAcquisitionChanged(
            object? sender,
            ReachyCameraAcquisitionChangedEventArgs eventArgs)
        {
            ReachyCameraAcquisitionSnapshot snapshot = eventArgs.Snapshot;
            if (snapshot.State != ReachyCameraAcquisitionState.Running ||
                snapshot.SessionId == 0UL ||
                (lastUploadedSessionId != 0UL &&
                    snapshot.SessionId != lastUploadedSessionId))
            {
                InvalidateOutput(
                    $"Camera acquisition changed to {snapshot.State}; detached texture state was cleared.");
            }
        }

        private void OnDestroy()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (acquisition != null)
            {
                acquisition.State.Changed -= OnAcquisitionChanged;
            }
            acquisition = null;
            DestroyResources();
        }

        private bool FrameMatchesActiveSession(
            ReachyCameraTextureFrameDescriptor descriptor,
            ReachyCameraAcquisitionSnapshot snapshot,
            ulong afterSequence)
        {
            if (descriptor.SessionId != snapshot.SessionId ||
                !string.Equals(
                    descriptor.CameraId,
                    snapshot.CameraId,
                    StringComparison.Ordinal) ||
                descriptor.Sequence <= afterSequence)
            {
                return false;
            }
            ReachyCameraFrameMetadata? metadata = snapshot.LatestFrame;
            return metadata == null ||
                descriptor.Sequence >= metadata.Sequence;
        }

        private static void ValidateLease(
            IReachyCameraTextureFrameLease lease)
        {
            ReachyCameraTextureFrameDescriptor descriptor =
                lease.Descriptor;
            if (lease.YBuffer == IntPtr.Zero ||
                lease.UBuffer == IntPtr.Zero ||
                lease.VBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "A texture frame lease exposed a null plane pointer.");
            }
            if (lease.YLength != descriptor.YPlaneLength ||
                lease.ULength != descriptor.ChromaPlaneLength ||
                lease.VLength != descriptor.ChromaPlaneLength)
            {
                throw new InvalidOperationException(
                    "A texture frame lease exposed plane lengths that do not match its descriptor.");
            }
        }

        private void EnsurePlaneTextures(
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            {
                throw new InvalidOperationException(
                    "This device does not support the required R8 plane texture format.");
            }
            yTexture = EnsurePlaneTexture(
                yTexture,
                descriptor.Width,
                descriptor.Height,
                "ReachyCameraY");
            uTexture = EnsurePlaneTexture(
                uTexture,
                descriptor.ChromaWidth,
                descriptor.ChromaHeight,
                "ReachyCameraU");
            vTexture = EnsurePlaneTexture(
                vTexture,
                descriptor.ChromaWidth,
                descriptor.ChromaHeight,
                "ReachyCameraV");
        }

        private static Texture2D EnsurePlaneTexture(
            Texture2D? existing,
            int width,
            int height,
            string name)
        {
            if (existing != null &&
                existing.width == width && existing.height == height)
            {
                return existing;
            }
            DestroyObject(existing);
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.R8,
                mipChain: false,
                linear: true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return texture;
        }

        private void EnsureOutputTexture(
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            if (outputTexture != null &&
                outputTexture.width == descriptor.OutputWidth &&
                outputTexture.height == descriptor.OutputHeight)
            {
                return;
            }
            DestroyOutputTexture();
            outputTexture = new RenderTexture(
                descriptor.OutputWidth,
                descriptor.OutputHeight,
                depth: 0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "ReachyCameraRgb",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (!outputTexture.Create())
            {
                DestroyOutputTexture();
                throw new InvalidOperationException(
                    "Unity could not create the camera RGB render texture.");
            }
        }

        private void ConfigureMaterial(
            ReachyCameraTextureFrameDescriptor descriptor)
        {
            Material material = conversionMaterial ??
                throw new InvalidOperationException(
                    "The camera conversion material is unavailable.");
            material.SetTexture(YTextureId, yTexture);
            material.SetTexture(UTextureId, uTexture);
            material.SetTexture(VTextureId, vTexture);
            material.SetVector(
                CropScaleOffsetId,
                new Vector4(
                    (float)descriptor.Crop.Width / descriptor.Width,
                    (float)descriptor.Crop.Height / descriptor.Height,
                    (float)descriptor.Crop.Left / descriptor.Width,
                    (float)descriptor.Crop.Top / descriptor.Height));
            material.SetFloat(
                RotationQuarterTurnsId,
                descriptor.RotationDegrees / 90f);
            material.SetFloat(MirrorXId, descriptor.Mirrored ? 1f : 0f);
            material.SetFloat(
                ColorStandardId,
                descriptor.ColorStandard == ReachyCameraYuvColorStandard.Bt709
                    ? 1f
                    : 0f);
            material.SetFloat(
                ColorRangeId,
                descriptor.ColorRange == ReachyCameraYuvColorRange.Full
                    ? 1f
                    : 0f);
        }

        private static void UploadPlane(
            Texture2D texture,
            IntPtr source,
            int length,
            string planeName)
        {
            int expected = checked(texture.width * texture.height);
            if (length != expected)
            {
                throw new InvalidOperationException(
                    $"{planeName} plane length {length} does not match texture size {expected}.");
            }
            texture.LoadRawTextureData(source, length);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private void InvalidateOutput(string message)
        {
            lastUploadedSessionId = 0UL;
            lastUploadedSequence = 0UL;
            DestroyResources();
            if (current.State != ReachyCameraTextureBridgeState.Faulted &&
                current.State != ReachyCameraTextureBridgeState.Unsupported)
            {
                PublishWaiting(message);
            }
        }

        private void PublishWaiting(string message)
        {
            Publish(
                ReachyCameraTextureBridgeState.Waiting,
                message,
                null,
                current.UploadedFrameCount,
                current.StaleFrameCount);
        }

        private void Publish(
            ReachyCameraTextureBridgeState state,
            string message,
            ReachyCameraTextureFrameDescriptor? frame,
            ulong uploadedFrameCount,
            ulong staleFrameCount)
        {
            current = new ReachyCameraTextureBridgeSnapshot(
                state,
                message,
                frame,
                uploadedFrameCount,
                staleFrameCount,
                checked(current.Revision + 1UL));
            Changed?.Invoke(
                this,
                new ReachyCameraTextureBridgeChangedEventArgs(current));
        }

        private void DestroyResources()
        {
            DestroyObject(yTexture);
            DestroyObject(uTexture);
            DestroyObject(vTexture);
            yTexture = null;
            uTexture = null;
            vTexture = null;
            DestroyOutputTexture();
            DestroyObject(conversionMaterial);
            conversionMaterial = null;
        }

        private void DestroyOutputTexture()
        {
            if (outputTexture == null)
            {
                return;
            }
            if (outputTexture.IsCreated())
            {
                outputTexture.Release();
            }
            DestroyObject(outputTexture);
            outputTexture = null;
        }

        private RenderTexture RequireOutputTexture()
        {
            return outputTexture ?? throw new InvalidOperationException(
                "The camera RGB render texture is unavailable.");
        }

        private static Texture2D RequireTexture(
            Texture2D? texture,
            string planeName)
        {
            return texture ?? throw new InvalidOperationException(
                $"The camera {planeName} plane texture is unavailable.");
        }

        private static void DestroyObject(Object? value)
        {
            if (value == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(value);
            }
            else
            {
                Object.DestroyImmediate(value);
            }
        }
    }
}

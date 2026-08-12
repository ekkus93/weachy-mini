#nullable enable

using System;

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
}

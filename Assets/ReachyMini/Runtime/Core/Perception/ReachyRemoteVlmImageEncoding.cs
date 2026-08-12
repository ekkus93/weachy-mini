#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public sealed class RemoteVlmImageEncodingRequest
    {
        public RemoteVlmImageEncodingRequest(
            ReachyVisionFrame frame,
            RemoteVlmImagePolicy policy)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            if (!frame.IsObservationEligible ||
                frame.Origin != VisionFrameOrigin.TransformedReachyEye ||
                !frame.Coverage.HasValidityMask ||
                !frame.Resources.HasResource(VisionResourceKind.ValidityMask))
            {
                throw new ArgumentException(
                    "Remote VLM encoding requires an eligible transformed Reachy-eye frame with a validity mask.",
                    nameof(frame));
            }
        }

        public ReachyVisionFrame Frame { get; }

        public RemoteVlmImagePolicy Policy { get; }

        public bool RequireValidityMask { get; } = true;

        public bool ApplyValidityBeforeResize { get; } = true;
    }

    public sealed class RemoteVlmEncodedImage : IDisposable
    {
        private readonly byte[] encodedBytes;
        private int disposed;

        public RemoteVlmEncodedImage(
            ReachyVisionFrameIdentity sourceIdentity,
            VisionFrameOrigin sourceOrigin,
            int sourceWidth,
            int sourceHeight,
            int width,
            int height,
            RemoteVlmImageFormat format,
            RemoteVlmInvalidPixelPolicy invalidPixelPolicyApplied,
            bool validityMaskApplied,
            bool containsOnlyValidPixels,
            bool upscaled,
            byte[] encodedBytes)
        {
            SourceIdentity = sourceIdentity ??
                throw new ArgumentNullException(nameof(sourceIdentity));
            if (sourceOrigin != VisionFrameOrigin.TransformedReachyEye)
            {
                throw new ArgumentException(
                    "Remote VLM payloads may originate only from transformed Reachy-eye frames.",
                    nameof(sourceOrigin));
            }
            if (sourceWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            }
            if (sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));
            }
            if (width <= 0 || width > sourceWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0 || height > sourceHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            if (!Enum.IsDefined(typeof(RemoteVlmImageFormat), format))
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }
            if (!Enum.IsDefined(
                    typeof(RemoteVlmInvalidPixelPolicy),
                    invalidPixelPolicyApplied))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(invalidPixelPolicyApplied));
            }
            if (!validityMaskApplied || !containsOnlyValidPixels)
            {
                throw new ArgumentException(
                    "Encoded remote VLM images must apply the validity mask and contain only valid transformed image content.");
            }
            if (upscaled)
            {
                throw new ArgumentException(
                    "Encoded remote VLM images cannot be upscaled.",
                    nameof(upscaled));
            }
            if (encodedBytes == null)
            {
                throw new ArgumentNullException(nameof(encodedBytes));
            }
            if (encodedBytes.Length == 0)
            {
                throw new ArgumentException(
                    "Encoded remote VLM images cannot be empty.",
                    nameof(encodedBytes));
            }

            SourceOrigin = sourceOrigin;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            Width = width;
            Height = height;
            Format = format;
            InvalidPixelPolicyApplied = invalidPixelPolicyApplied;
            ValidityMaskApplied = true;
            ContainsOnlyValidPixels = true;
            Upscaled = false;
            this.encodedBytes = (byte[])encodedBytes.Clone();
        }

        public ReachyVisionFrameIdentity SourceIdentity { get; }

        public VisionFrameOrigin SourceOrigin { get; }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int Width { get; }

        public int Height { get; }

        public RemoteVlmImageFormat Format { get; }

        public RemoteVlmInvalidPixelPolicy InvalidPixelPolicyApplied { get; }

        public bool ValidityMaskApplied { get; }

        public bool ContainsOnlyValidPixels { get; }

        public bool Upscaled { get; }

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public int EncodedByteCount => encodedBytes.Length;

        public string MediaType
        {
            get
            {
                switch (Format)
                {
                    case RemoteVlmImageFormat.Jpeg:
                        return "image/jpeg";
                    case RemoteVlmImageFormat.Png:
                        return "image/png";
                    case RemoteVlmImageFormat.WebP:
                        return "image/webp";
                    default:
                        throw new InvalidOperationException(
                            "Unsupported remote VLM image format.");
                }
            }
        }

        public ReadOnlyMemory<byte> EncodedBytes
        {
            get
            {
                if (IsDisposed)
                {
                    throw new ObjectDisposedException(
                        nameof(RemoteVlmEncodedImage));
                }
                return encodedBytes;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing || Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            Array.Clear(encodedBytes, 0, encodedBytes.Length);
        }
    }

    public sealed class RemoteVlmImageEncodingResult
    {
        private RemoteVlmImageEncodingResult(
            RemoteVlmImageEncodingStatus status,
            RemoteVlmEncodedImage? image,
            string diagnosticCode,
            bool requiresEncoderReset)
        {
            Status = status;
            Image = image;
            DiagnosticCode = diagnosticCode;
            RequiresEncoderReset = requiresEncoderReset;
        }

        public RemoteVlmImageEncodingStatus Status { get; }

        public RemoteVlmEncodedImage? Image { get; }

        public string DiagnosticCode { get; }

        public bool RequiresEncoderReset { get; }

        public bool Succeeded => Status == RemoteVlmImageEncodingStatus.Succeeded;

        public static RemoteVlmImageEncodingResult Success(
            RemoteVlmEncodedImage image)
        {
            return new RemoteVlmImageEncodingResult(
                RemoteVlmImageEncodingStatus.Succeeded,
                image ?? throw new ArgumentNullException(nameof(image)),
                "encoded",
                requiresEncoderReset: false);
        }

        public static RemoteVlmImageEncodingResult Failure(
            RemoteVlmImageEncodingStatus status,
            string diagnosticCode,
            bool requiresEncoderReset)
        {
            if (status == RemoteVlmImageEncodingStatus.Succeeded ||
                !Enum.IsDefined(typeof(RemoteVlmImageEncodingStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new RemoteVlmImageEncodingResult(
                status,
                null,
                ReachyOpenAiVisionDiagnosticTokens.RequireSafeToken(
                    diagnosticCode,
                    nameof(diagnosticCode),
                    64),
                requiresEncoderReset);
        }
    }

    public interface IRemoteVlmImageEncoder
    {
        ValueTask<RemoteVlmImageEncodingResult> EncodeAsync(
            RemoteVlmImageEncodingRequest request,
            CancellationToken cancellationToken);
    }
}

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal sealed class FakeTransport : IOpenAiVisionTransport
    {
        public FakeTransport(OpenAiVisionEndpointStyle endpointStyle)
        {
            EndpointStyle = endpointStyle;
        }

        public OpenAiVisionEndpointStyle EndpointStyle { get; }

        public int CallCount { get; private set; }

        public OpenAiVisionTransportRequest? LastRequest { get; private set; }

        public Func<
            OpenAiVisionTransportRequest,
            CancellationToken,
            ValueTask<OpenAiVisionTransportResult>>? Handler { get; set; }

        public ValueTask<OpenAiVisionTransportResult> SendAsync(
            OpenAiVisionTransportRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            CallCount = checked(CallCount + 1);
            LastRequest = request;
            if (Handler != null)
            {
                return Handler(request, cancellationToken);
            }
            return new ValueTask<OpenAiVisionTransportResult>(
                OpenAiVisionTransportResult.Success(
                    "Synthetic semantic result.",
                    providerRequestId: "req_synthetic",
                    finishReason: "stop",
                    inputTokens: 10L,
                    outputTokens: 4L));
        }
    }

    internal sealed class FakeEncoder : IRemoteVlmImageEncoder
    {
        public int CallCount { get; private set; }

        public RemoteVlmEncodedImage? LastImage { get; private set; }

        public Func<
            RemoteVlmImageEncodingRequest,
            CancellationToken,
            ValueTask<RemoteVlmImageEncodingResult>>? Handler { get; set; }

        public ValueTask<RemoteVlmImageEncodingResult> EncodeAsync(
            RemoteVlmImageEncodingRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            CallCount = checked(CallCount + 1);
            ValueTask<RemoteVlmImageEncodingResult> result;
            if (Handler != null)
            {
                result = Handler(request, cancellationToken);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                result = new ValueTask<RemoteVlmImageEncodingResult>(
                    RemoteVlmImageEncodingResult.Failure(
                        RemoteVlmImageEncodingStatus.Cancelled,
                        "cancelled",
                        requiresEncoderReset: false));
            }
            else
            {
                RemoteVlmImageDimensions target =
                    request.Policy.ComputeTargetDimensions(
                        request.Frame.Width,
                        request.Frame.Height);
                LastImage = Program.EncodedImage(
                    request.Frame.Identity,
                    sourceWidth: request.Frame.Width,
                    sourceHeight: request.Frame.Height,
                    width: target.Width,
                    height: target.Height,
                    format: request.Policy.PreferredFormat,
                    invalidPixelPolicy:
                        request.Policy.InvalidPixelPolicy);
                result = new ValueTask<RemoteVlmImageEncodingResult>(
                    RemoteVlmImageEncodingResult.Success(LastImage));
            }
            return TrackImageAsync(result);
        }

        private async ValueTask<RemoteVlmImageEncodingResult> TrackImageAsync(
            ValueTask<RemoteVlmImageEncodingResult> pending)
        {
            RemoteVlmImageEncodingResult result =
                await pending.ConfigureAwait(false);
            if (result.Image != null)
            {
                LastImage = result.Image;
            }
            return result;
        }
    }

    internal sealed class FakeResources : IReachyVisionFrameResources, IDisposable
    {
        private readonly object color = new object();
        private readonly object? validityMask;
        private int disposed;
        private int ownershipTransferred;

        public FakeResources(
            int width,
            int height,
            bool includeValidityMask)
        {
            Width = width;
            Height = height;
            validityMask = includeValidityMask ? new object() : null;
        }

        public string OwnerId => "remote-vlm-tests";

        public ulong Generation => 1UL;

        public int Width { get; }

        public int Height { get; }

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public bool HasResource(VisionResourceKind kind)
        {
            return kind == VisionResourceKind.Color ||
                (kind == VisionResourceKind.ValidityMask &&
                    validityMask != null);
        }

        public VisionPixelEncoding GetEncoding(VisionResourceKind kind)
        {
            if (kind == VisionResourceKind.Color)
            {
                return VisionPixelEncoding.Rgba8;
            }
            if (kind == VisionResourceKind.ValidityMask &&
                validityMask != null)
            {
                return VisionPixelEncoding.ValidityMask8;
            }
            throw new InvalidOperationException("Resource is unavailable.");
        }

        public bool TryGetResource<TResource>(
            VisionResourceKind kind,
            out TResource? resource)
            where TResource : class
        {
            object? value = kind == VisionResourceKind.Color
                ? color
                : kind == VisionResourceKind.ValidityMask
                    ? validityMask
                    : null;
            resource = value as TResource;
            return resource != null;
        }

        public void TransferOwnershipToFrame()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (Interlocked.Exchange(ref ownershipTransferred, 1) != 0)
            {
                throw new InvalidOperationException(
                    "Fake resource ownership was already transferred.");
            }
        }

        public void Dispose()
        {
            if (Volatile.Read(ref ownershipTransferred) == 0)
            {
                _ = Interlocked.Exchange(ref disposed, 1);
            }
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            _ = Interlocked.Exchange(ref disposed, 1);
            GC.SuppressFinalize(this);
            return default;
        }
    }
}

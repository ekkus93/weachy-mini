#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static async Task EncoderInvalidFrameMapsInvalidFrame()
        {
            await AssertEncodingFailure(
                RemoteVlmImageEncodingStatus.InvalidFrame,
                VisionOperationStatus.InvalidFrame,
                "invalid_frame").ConfigureAwait(false);
        }

        private static async Task EncoderUnsupportedMapsUnavailable()
        {
            await AssertEncodingFailure(
                RemoteVlmImageEncodingStatus.Unsupported,
                VisionOperationStatus.Unavailable,
                "unsupported_format").ConfigureAwait(false);
        }

        private static async Task EncoderCancellationMapsCancelled()
        {
            await AssertEncodingFailure(
                RemoteVlmImageEncodingStatus.Cancelled,
                VisionOperationStatus.Cancelled,
                "cancelled").ConfigureAwait(false);
        }

        private static async Task EncoderFailurePreservesSafeCode()
        {
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Failure(
                            RemoteVlmImageEncodingStatus.Failed,
                            "gpu_readback_failed",
                            requiresEncoderReset: true)),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "encoder failure status");
            Contains("gpu_readback_failed", result.Diagnostic, "encoder code");
            True(result.RequiresProviderReset, "encoder reset");
            Equal(0, transport.CallCount, "encoder failure transport calls");
        }

        private static async Task EncodedImageIdentityMismatchRejectedBeforeTransport()
        {
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Success(
                            EncodedImage(Identity(sourceSequence: 99)))),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "identity mismatch status");
            Equal(0, transport.CallCount, "identity mismatch transport calls");
            True(encoder.LastImage?.IsDisposed == true, "mismatched image disposed");
        }

        private static async Task EncodedImagePolicyOverflowRejectedBeforeTransport()
        {
            RemoteVlmImagePolicy policy = Policy(maximumEncodedBytes: 4);
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Success(
                            EncodedImage(
                                request.Frame.Identity,
                                sourceWidth: request.Frame.Width,
                                sourceHeight: request.Frame.Height,
                                width: request.Frame.Width,
                                height: request.Frame.Height,
                                bytes: new byte[] { 1, 2, 3, 4, 5 }))),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder,
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    policy: policy));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "payload overflow status");
            Equal(0, transport.CallCount, "payload overflow transport calls");
        }

        private static async Task EncodedImagePolicyMismatchRejectedBeforeTransport()
        {
            RemoteVlmImagePolicy policy = Policy(
                preferredFormat: RemoteVlmImageFormat.Png);
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Success(
                            EncodedImage(
                                request.Frame.Identity,
                                sourceWidth: request.Frame.Width,
                                sourceHeight: request.Frame.Height,
                                width: request.Frame.Width,
                                height: request.Frame.Height,
                                format: RemoteVlmImageFormat.Jpeg))),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder,
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    policy: policy));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "policy mismatch status");
            Equal(0, transport.CallCount, "policy mismatch transport calls");
        }
    }
}

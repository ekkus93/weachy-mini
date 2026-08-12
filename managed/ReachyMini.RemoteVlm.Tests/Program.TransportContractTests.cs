#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static async Task TransportCancellationMapsCancelled()
        {
            await AssertTransportFailure(
                OpenAiVisionTransportStatus.Cancelled,
                VisionOperationStatus.Cancelled,
                Error(OpenAiVisionProviderErrorCategory.Transport, "cancelled"))
                .ConfigureAwait(false);
        }

        private static async Task TransportTimeoutMapsTimedOut()
        {
            await AssertTransportFailure(
                OpenAiVisionTransportStatus.TimedOut,
                VisionOperationStatus.TimedOut,
                Error(OpenAiVisionProviderErrorCategory.Transport, "timeout"))
                .ConfigureAwait(false);
        }

        private static async Task TransportUnavailableMapsUnavailable()
        {
            await AssertTransportFailure(
                OpenAiVisionTransportStatus.Unavailable,
                VisionOperationStatus.Unavailable,
                Error(
                    OpenAiVisionProviderErrorCategory.UnsupportedCapability,
                    "vision_not_supported"))
                .ConfigureAwait(false);
        }

        private static async Task TransportFailurePreservesSafeDetail()
        {
            var error = new OpenAiVisionProviderError(
                OpenAiVisionProviderErrorCategory.RateLimit,
                "rate_limit_exceeded",
                429,
                "req_123",
                "Rate limit reached for this model.");
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Failure(
                            OpenAiVisionTransportStatus.RateLimited,
                            error,
                            requiresTransportReset: false)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "rate-limit status");
            Contains("rate_limit_exceeded", result.Diagnostic, "error code");
            Contains("http_status=429", result.Diagnostic, "http status");
            Contains("req_123", result.Diagnostic, "request id");
            Contains("Rate limit reached", result.Diagnostic, "safe detail");
        }

        private static void TransportSecretDetailIsRedacted()
        {
            var error = new OpenAiVisionProviderError(
                OpenAiVisionProviderErrorCategory.Authentication,
                "authentication_failed",
                401,
                "req_safe",
                "Authorization: Bearer sk-super-secret");
            True(error.DetailRedacted, "secret detail redacted");
            False(error.Detail.Contains("sk-super-secret", StringComparison.Ordinal), "secret absent");
            Contains("redacted", error.Detail, "redaction marker");
        }

        private static void TransportLongOpaqueDetailIsRedacted()
        {
            var error = new OpenAiVisionProviderError(
                OpenAiVisionProviderErrorCategory.Protocol,
                "payload_rejected",
                400,
                null,
                new string('A', 100));
            True(error.DetailRedacted, "opaque detail redacted");
        }

        private static async Task TransportSuccessReturnsValidatedText()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Success(
                            "A person is visible.",
                            "req_ok",
                            "stop",
                            100,
                            8)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            True(result.Succeeded, "transport success");
            Equal("A person is visible.", result.Text ?? string.Empty, "result text");
        }

        private static async Task OversizedTransportTextRejected()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Success(
                            "12345",
                            null,
                            null,
                            null,
                            null)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder(),
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    maximumResponseCharacters: 4));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "oversized result status");
            True(result.RequiresProviderReset, "oversized result reset");
        }

        private static void StructuredResultRejectsSuccessWithoutText()
        {
            Throws<ArgumentException>(
                () => OpenAiVisionTransportResult.Success(
                    " ",
                    null,
                    null,
                    null,
                    null),
                "blank transport success");
        }

        private static void StructuredFailureRequiresError()
        {
            Throws<ArgumentNullException>(
                () => OpenAiVisionTransportResult.Failure(
                    OpenAiVisionTransportStatus.ServerFailure,
                    null!,
                    requiresTransportReset: true),
                "failure without error");
        }
    }
}

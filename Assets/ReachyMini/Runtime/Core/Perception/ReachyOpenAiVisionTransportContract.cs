#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public sealed class OpenAiVisionTransportRequest
    {
        public OpenAiVisionTransportRequest(
            OpenAiVisionEndpointStyle endpointStyle,
            string requestId,
            string modelId,
            string systemContext,
            string userPrompt,
            RemoteVlmEncodedImage image,
            RemoteVlmImageDetail detail,
            int maximumOutputTokens)
        {
            if (!Enum.IsDefined(
                    typeof(OpenAiVisionEndpointStyle),
                    endpointStyle))
            {
                throw new ArgumentOutOfRangeException(nameof(endpointStyle));
            }
            if (!Enum.IsDefined(typeof(RemoteVlmImageDetail), detail))
            {
                throw new ArgumentOutOfRangeException(nameof(detail));
            }
            if (maximumOutputTokens <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutputTokens));
            }

            EndpointStyle = endpointStyle;
            RequestId = ProviderDescriptor.RequireText(
                requestId,
                nameof(requestId));
            ModelId = ProviderDescriptor.RequireText(modelId, nameof(modelId));
            SystemContext = ProviderDescriptor.RequireText(
                systemContext,
                nameof(systemContext));
            UserPrompt = ProviderDescriptor.RequireText(
                userPrompt,
                nameof(userPrompt));
            Image = image ?? throw new ArgumentNullException(nameof(image));
            if (image.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(image));
            }
            Detail = detail;
            MaximumOutputTokens = maximumOutputTokens;
        }

        public OpenAiVisionEndpointStyle EndpointStyle { get; }

        public string RequestId { get; }

        public string ModelId { get; }

        public string SystemContext { get; }

        public string UserPrompt { get; }

        public RemoteVlmEncodedImage Image { get; }

        public RemoteVlmImageDetail Detail { get; }

        public int MaximumOutputTokens { get; }

        public bool StoreResponse { get; }

        public bool Stream { get; }
    }

    public sealed class OpenAiVisionTransportResult
    {
        private OpenAiVisionTransportResult(
            OpenAiVisionTransportStatus status,
            string? text,
            string? providerRequestId,
            string? finishReason,
            long? inputTokens,
            long? outputTokens,
            OpenAiVisionProviderError? error,
            bool requiresTransportReset)
        {
            Status = status;
            Text = text;
            ProviderRequestId = providerRequestId;
            FinishReason = finishReason;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            Error = error;
            RequiresTransportReset = requiresTransportReset;
        }

        public OpenAiVisionTransportStatus Status { get; }

        public string? Text { get; }

        public string? ProviderRequestId { get; }

        public string? FinishReason { get; }

        public long? InputTokens { get; }

        public long? OutputTokens { get; }

        public OpenAiVisionProviderError? Error { get; }

        public bool RequiresTransportReset { get; }

        public bool Succeeded => Status == OpenAiVisionTransportStatus.Succeeded;

        public static OpenAiVisionTransportResult Success(
            string text,
            string? providerRequestId,
            string? finishReason,
            long? inputTokens,
            long? outputTokens)
        {
            string resultText = ProviderDescriptor.RequireText(
                text,
                nameof(text));
            string? safeRequestId = string.IsNullOrWhiteSpace(providerRequestId)
                ? null
                : ReachyOpenAiVisionDiagnosticTokens.RequireSafeToken(
                    providerRequestId,
                    nameof(providerRequestId),
                    128);
            string? safeFinishReason = string.IsNullOrWhiteSpace(finishReason)
                ? null
                : ReachyOpenAiVisionDiagnosticTokens.RequireSafeToken(
                    finishReason,
                    nameof(finishReason),
                    64);
            RequireNonNegative(inputTokens, nameof(inputTokens));
            RequireNonNegative(outputTokens, nameof(outputTokens));

            return new OpenAiVisionTransportResult(
                OpenAiVisionTransportStatus.Succeeded,
                resultText,
                safeRequestId,
                safeFinishReason,
                inputTokens,
                outputTokens,
                null,
                requiresTransportReset: false);
        }

        public static OpenAiVisionTransportResult Failure(
            OpenAiVisionTransportStatus status,
            OpenAiVisionProviderError error,
            bool requiresTransportReset)
        {
            if (status == OpenAiVisionTransportStatus.Succeeded ||
                !Enum.IsDefined(typeof(OpenAiVisionTransportStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new OpenAiVisionTransportResult(
                status,
                null,
                null,
                null,
                null,
                null,
                error ?? throw new ArgumentNullException(nameof(error)),
                requiresTransportReset);
        }

        private static void RequireNonNegative(long? value, string name)
        {
            if (value.HasValue && value.Value < 0L)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public interface IOpenAiVisionTransport
    {
        OpenAiVisionEndpointStyle EndpointStyle { get; }

        ValueTask<OpenAiVisionTransportResult> SendAsync(
            OpenAiVisionTransportRequest request,
            CancellationToken cancellationToken);
    }
}

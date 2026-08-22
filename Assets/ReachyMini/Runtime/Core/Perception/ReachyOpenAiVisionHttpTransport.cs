#nullable enable

using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Providers;

namespace ReachyMini.Perception
{
    // RMA-115/195 VLM half: no cloud VLM transport implementation existed at all
    // before this file -- IOpenAiVisionTransport/IRemoteVlmImageEncoder were
    // built-and-tested in isolation only (managed/ReachyMini.RemoteVlm.Tests'
    // FakeTransport/FakeEncoder), with zero production implementation, confirmed by
    // a dedicated investigation before writing any code. This mirrors the RMA-142/143
    // LLM adapter's real-transport pattern: one ReachySharedHttpTransport-backed
    // class, reusing ReachyBearerCredentialTransportBinding for auth. It also reuses
    // ReachyLlmResponseProtocolParser (internal, same assembly) for extracting
    // assistant text -- the OpenAI Chat Completions / Responses response SHAPE for
    // "where is the assistant's text" is identical whether or not the request
    // included image content, so a second parser would just be a needless
    // duplicate of the exact same recursive-descent scanner.
    public sealed class ReachyOpenAiVisionHttpTransport : IOpenAiVisionTransport, IDisposable
    {
        private const string InternalBearerSecretReference = "reachy.internal.vlm.bearer";
        private const int MaximumJsonResponseDepth = 64;

        private readonly ReachySharedHttpTransport transport;
        private readonly bool ownsTransport;
        private readonly string relativeCompletionPath;
        private readonly int maximumResponseBytes;
        private readonly int maximumDiagnosticCharacters;
        private int disposed;

        public ReachyOpenAiVisionHttpTransport(
            OpenAiVisionEndpointStyle endpointStyle,
            ReachyProviderProfile profile,
            IReachyProviderSecretStore secretStore,
            string relativeCompletionPath,
            int maximumResponseBytes,
            int maximumDiagnosticCharacters)
            : this(
                endpointStyle,
                profile,
                secretStore,
                relativeCompletionPath,
                maximumResponseBytes,
                maximumDiagnosticCharacters,
                transportOverride: null)
        {
        }

        internal ReachyOpenAiVisionHttpTransport(
            OpenAiVisionEndpointStyle endpointStyle,
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            string relativeCompletionPath,
            int maximumResponseBytes,
            int maximumDiagnosticCharacters,
            ReachySharedHttpTransport? transportOverride)
        {
            if (!Enum.IsDefined(typeof(OpenAiVisionEndpointStyle), endpointStyle))
            {
                throw new ArgumentOutOfRangeException(nameof(endpointStyle));
            }
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            ValidateProfile(profile, endpointStyle);
            EndpointStyle = endpointStyle;
            if (string.IsNullOrWhiteSpace(relativeCompletionPath))
            {
                throw new ArgumentException(
                    "The VLM transport requires a completion path.",
                    nameof(relativeCompletionPath));
            }
            this.relativeCompletionPath = relativeCompletionPath;
            if (maximumResponseBytes < 1024 ||
                maximumResponseBytes > ReachyHttpTransportPolicy.MaximumResponseBytesLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
            }
            this.maximumResponseBytes = maximumResponseBytes;
            if (maximumDiagnosticCharacters < 64 || maximumDiagnosticCharacters > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDiagnosticCharacters));
            }
            this.maximumDiagnosticCharacters = maximumDiagnosticCharacters;

            if (transportOverride != null)
            {
                transport = transportOverride;
                ownsTransport = false;
            }
            else
            {
                ReachyBearerCredentialTransportBinding bearer =
                    ReachyBearerCredentialTransportBinding.Create(
                        profile,
                        secretStore ?? throw new ArgumentNullException(nameof(secretStore)),
                        InternalBearerSecretReference);
                transport = new ReachySharedHttpTransport(
                    bearer.Profile,
                    bearer.SecretStore,
                    ReachyHttpTransportPolicy.FromProfile(bearer.Profile));
                ownsTransport = true;
            }
        }

        public OpenAiVisionEndpointStyle EndpointStyle { get; }

        public async ValueTask<OpenAiVisionTransportResult> SendAsync(
            OpenAiVisionTransportRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
            if (request.EndpointStyle != EndpointStyle)
            {
                throw new ArgumentException(
                    "Vision transport request endpoint style does not match this transport instance.",
                    nameof(request));
            }

            byte[]? requestBody = null;
            byte[]? borrowedResponseBody = null;
            try
            {
                requestBody = BuildRequestBody(request);

                using ReachyHttpTransportRequest transportRequest = new ReachyHttpTransportRequest(
                    HttpMethod.Post,
                    relativeCompletionPath,
                    requestBody,
                    "application/json",
                    "application/json",
                    ReachyHttpResponseMode.Buffered,
                    maximumResponseBytes,
                    maximumSseEventCharacters: 1024,
                    explicitlyAuthorizeNonIdempotentRetry: false,
                    idempotencyKey: null);

                ReachyHttpTransportResult result = await transport.SendAsync(
                        transportRequest,
                        eventSink: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return FailureFromTransport(result);
                }

                borrowedResponseBody = result.BorrowResponseBodyForTransport() ??
                    throw new InvalidOperationException(
                        "Successful VLM HTTP response omitted its response body.");

                string responseText;
                string? assistantText;
                try
                {
                    responseText = DecodeStrictUtf8(borrowedResponseBody);
                    ReachyLlmResponseProtocolParser parser = new ReachyLlmResponseProtocolParser(
                        responseText,
                        MaximumJsonResponseDepth,
                        maximumDiagnosticCharacters);
                    assistantText = EndpointStyle == OpenAiVisionEndpointStyle.Responses
                        ? parser.ParseResponsesAssistantText()
                        : parser.ParseChatCompletionsAssistantText();
                }
                catch (FormatException exception)
                {
                    return OpenAiVisionTransportResult.Failure(
                        OpenAiVisionTransportStatus.ProtocolFailure,
                        new OpenAiVisionProviderError(
                            OpenAiVisionProviderErrorCategory.Protocol,
                            "protocol-failure",
                            result.HttpStatusCode,
                            null,
                            "VLM provider response is not valid strict-UTF8 JSON: " +
                                exception.Message),
                        requiresTransportReset: true);
                }

                if (assistantText == null)
                {
                    return OpenAiVisionTransportResult.Failure(
                        OpenAiVisionTransportStatus.ProtocolFailure,
                        new OpenAiVisionProviderError(
                            OpenAiVisionProviderErrorCategory.Protocol,
                            "missing-assistant-text",
                            result.HttpStatusCode,
                            null,
                            "VLM provider response did not contain a recognizable assistant message."),
                        requiresTransportReset: true);
                }

                return OpenAiVisionTransportResult.Success(
                    assistantText,
                    SanitizeProviderRequestId(result.ProviderRequestId),
                    finishReason: null,
                    inputTokens: null,
                    outputTokens: null);
            }
            catch (OperationCanceledException)
            {
                return OpenAiVisionTransportResult.Failure(
                    OpenAiVisionTransportStatus.Cancelled,
                    new OpenAiVisionProviderError(
                        OpenAiVisionProviderErrorCategory.Transport,
                        "cancelled",
                        null,
                        null,
                        "VLM transport request was cancelled."),
                    requiresTransportReset: false);
            }
            finally
            {
                if (requestBody != null)
                {
                    Array.Clear(requestBody, 0, requestBody.Length);
                }
                if (borrowedResponseBody != null)
                {
                    Array.Clear(borrowedResponseBody, 0, borrowedResponseBody.Length);
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (ownsTransport)
            {
                transport.Dispose();
            }
        }

        private static void ValidateProfile(
            ReachyProviderProfile profile,
            OpenAiVisionEndpointStyle endpointStyle)
        {
            ReachyProviderEndpointStyle expected = endpointStyle == OpenAiVisionEndpointStyle.Responses
                ? ReachyProviderEndpointStyle.Responses
                : ReachyProviderEndpointStyle.ChatCompletions;
            if (profile.EndpointStyle != expected)
            {
                throw new ArgumentException(
                    "The VLM transport requires a profile whose endpoint style matches the adapter class.",
                    nameof(profile));
            }
            _ = profile.GetModelId(ReachyProviderModelRole.Vision);
            if (profile.StreamingEnabled)
            {
                throw new ArgumentException(
                    "Cloud VLM profiles must disable streaming explicitly.",
                    nameof(profile));
            }
        }

        private byte[] BuildRequestBody(OpenAiVisionTransportRequest request)
        {
            string base64Image = Convert.ToBase64String(request.Image.EncodedBytes.Span);
            string dataUri = "data:" + request.Image.MediaType + ";base64," + base64Image;
            string detail = DetailToken(request.Detail);

            StringBuilder builder = new StringBuilder(base64Image.Length + 1024);
            builder.Append('{');
            AppendProperty(builder, "model", request.ModelId, first: true);
            builder.Append(',');
            if (EndpointStyle == OpenAiVisionEndpointStyle.Responses)
            {
                AppendJsonString(builder, "input");
                builder.Append(':');
                builder.Append('[');
                AppendResponsesMessage(
                    builder,
                    "system",
                    request.SystemContext,
                    imageDataUri: null,
                    detail: null,
                    first: true);
                AppendResponsesMessage(
                    builder,
                    "user",
                    request.UserPrompt,
                    dataUri,
                    detail,
                    first: false);
                builder.Append(']');
                builder.Append(',');
                AppendJsonString(builder, "max_output_tokens");
            }
            else
            {
                AppendJsonString(builder, "messages");
                builder.Append(':');
                builder.Append('[');
                AppendChatMessage(
                    builder,
                    "system",
                    request.SystemContext,
                    imageDataUri: null,
                    detail: null,
                    first: true);
                AppendChatMessage(
                    builder,
                    "user",
                    request.UserPrompt,
                    dataUri,
                    detail,
                    first: false);
                builder.Append(']');
                builder.Append(',');
                AppendJsonString(builder, "max_tokens");
            }
            builder.Append(':');
            builder.Append(
                request.MaximumOutputTokens.ToString(CultureInfo.InvariantCulture));
            builder.Append('}');
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
        }

        private static string DetailToken(RemoteVlmImageDetail detail)
        {
            switch (detail)
            {
                case RemoteVlmImageDetail.Low: return "low";
                case RemoteVlmImageDetail.High: return "high";
                default: return "auto";
            }
        }

        private static void AppendChatMessage(
            StringBuilder builder,
            string role,
            string text,
            string? imageDataUri,
            string? detail,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            builder.Append('{');
            AppendProperty(builder, "role", role, first: true);
            builder.Append(',');
            AppendJsonString(builder, "content");
            builder.Append(':');
            if (imageDataUri == null)
            {
                AppendJsonString(builder, text);
            }
            else
            {
                builder.Append('[');
                builder.Append('{');
                AppendProperty(builder, "type", "text", first: true);
                builder.Append(',');
                AppendProperty(builder, "text", text, first: true);
                builder.Append('}');
                builder.Append(',');
                builder.Append('{');
                AppendProperty(builder, "type", "image_url", first: true);
                builder.Append(',');
                AppendJsonString(builder, "image_url");
                builder.Append(':');
                builder.Append('{');
                AppendProperty(builder, "url", imageDataUri, first: true);
                builder.Append(',');
                AppendProperty(builder, "detail", detail!, first: true);
                builder.Append('}');
                builder.Append('}');
                builder.Append(']');
            }
            builder.Append('}');
        }

        private static void AppendResponsesMessage(
            StringBuilder builder,
            string role,
            string text,
            string? imageDataUri,
            string? detail,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            builder.Append('{');
            AppendProperty(builder, "role", role, first: true);
            builder.Append(',');
            AppendJsonString(builder, "content");
            builder.Append(':');
            builder.Append('[');
            builder.Append('{');
            AppendProperty(builder, "type", "input_text", first: true);
            builder.Append(',');
            AppendProperty(builder, "text", text, first: true);
            builder.Append('}');
            if (imageDataUri != null)
            {
                builder.Append(',');
                builder.Append('{');
                AppendProperty(builder, "type", "input_image", first: true);
                builder.Append(',');
                AppendProperty(builder, "image_url", imageDataUri, first: true);
                builder.Append(',');
                AppendProperty(builder, "detail", detail!, first: true);
                builder.Append('}');
            }
            builder.Append(']');
            builder.Append('}');
        }

        private static void AppendProperty(
            StringBuilder builder,
            string name,
            string value,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            AppendJsonString(builder, name);
            builder.Append(':');
            AppendJsonString(builder, value);
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            builder.Append('"');
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(
                                ((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else if (char.IsHighSurrogate(character))
                        {
                            if (index + 1 >= value.Length ||
                                !char.IsLowSurrogate(value[index + 1]))
                            {
                                throw new ArgumentException(
                                    "VLM request JSON text contains an unpaired high surrogate.",
                                    nameof(value));
                            }
                            builder.Append(character);
                            builder.Append(value[++index]);
                        }
                        else if (char.IsLowSurrogate(character))
                        {
                            throw new ArgumentException(
                                "VLM request JSON text contains an unpaired low surrogate.",
                                nameof(value));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private static string DecodeStrictUtf8(ReadOnlyMemory<byte> bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            }
            catch (DecoderFallbackException exception)
            {
                throw new FormatException("VLM provider response is not strict UTF-8.", exception);
            }
        }

        private static string? SanitizeProviderRequestId(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }
            try
            {
                return ReachyOpenAiVisionDiagnosticTokens.RequireSafeToken(
                    candidate,
                    "providerRequestId",
                    128);
            }
            catch (ArgumentException)
            {
                // The HTTP layer's own request-id validation is looser (bounded
                // visible text) than the diagnostic-token charset this field
                // requires -- an unusual but legitimate provider request id must
                // not fail the whole response just because it can't be echoed
                // back as a diagnostic.
                return null;
            }
        }

        private static OpenAiVisionTransportResult FailureFromTransport(
            ReachyHttpTransportResult result)
        {
            ReachyHttpTransportError error = result.Error ??
                throw new InvalidOperationException(
                    "Failed VLM HTTP result omitted its typed error.");
            (OpenAiVisionTransportStatus status,
                OpenAiVisionProviderErrorCategory category,
                bool requiresReset) = MapCategory(error.Category);
            OpenAiVisionProviderError providerError = new OpenAiVisionProviderError(
                category,
                error.Category.ToString(),
                error.HttpStatusCode,
                null,
                error.Detail);
            return OpenAiVisionTransportResult.Failure(status, providerError, requiresReset);
        }

        private static (OpenAiVisionTransportStatus, OpenAiVisionProviderErrorCategory, bool)
            MapCategory(ReachyHttpErrorCategory category)
        {
            switch (category)
            {
                case ReachyHttpErrorCategory.Cancelled:
                    return (
                        OpenAiVisionTransportStatus.Cancelled,
                        OpenAiVisionProviderErrorCategory.Transport,
                        false);
                case ReachyHttpErrorCategory.Timeout:
                    return (
                        OpenAiVisionTransportStatus.TimedOut,
                        OpenAiVisionProviderErrorCategory.Transport,
                        false);
                case ReachyHttpErrorCategory.Authentication:
                    return (
                        OpenAiVisionTransportStatus.Unauthorized,
                        OpenAiVisionProviderErrorCategory.Authentication,
                        false);
                case ReachyHttpErrorCategory.Permission:
                    return (
                        OpenAiVisionTransportStatus.Unauthorized,
                        OpenAiVisionProviderErrorCategory.Authorization,
                        false);
                case ReachyHttpErrorCategory.QuotaOrRateLimited:
                    return (
                        OpenAiVisionTransportStatus.RateLimited,
                        OpenAiVisionProviderErrorCategory.RateLimit,
                        false);
                case ReachyHttpErrorCategory.Tls:
                    return (
                        OpenAiVisionTransportStatus.Unavailable,
                        OpenAiVisionProviderErrorCategory.Transport,
                        true);
                case ReachyHttpErrorCategory.MalformedResponse:
                    return (
                        OpenAiVisionTransportStatus.ProtocolFailure,
                        OpenAiVisionProviderErrorCategory.Protocol,
                        true);
                case ReachyHttpErrorCategory.Server:
                    return (
                        OpenAiVisionTransportStatus.ServerFailure,
                        OpenAiVisionProviderErrorCategory.Server,
                        false);
                case ReachyHttpErrorCategory.Client:
                    return (
                        OpenAiVisionTransportStatus.InvalidRequest,
                        OpenAiVisionProviderErrorCategory.InvalidRequest,
                        false);
                case ReachyHttpErrorCategory.Network:
                    return (
                        OpenAiVisionTransportStatus.Unavailable,
                        OpenAiVisionProviderErrorCategory.Transport,
                        true);
                case ReachyHttpErrorCategory.ResponseTooLarge:
                    return (
                        OpenAiVisionTransportStatus.ProtocolFailure,
                        OpenAiVisionProviderErrorCategory.Protocol,
                        true);
                case ReachyHttpErrorCategory.RedirectRejected:
                    return (
                        OpenAiVisionTransportStatus.Unavailable,
                        OpenAiVisionProviderErrorCategory.Transport,
                        true);
                case ReachyHttpErrorCategory.Configuration:
                    return (
                        OpenAiVisionTransportStatus.InvalidRequest,
                        OpenAiVisionProviderErrorCategory.InvalidRequest,
                        false);
                case ReachyHttpErrorCategory.Consumer:
                    return (
                        OpenAiVisionTransportStatus.InvalidRequest,
                        OpenAiVisionProviderErrorCategory.InvalidRequest,
                        false);
                default:
                    return (
                        OpenAiVisionTransportStatus.Unavailable,
                        OpenAiVisionProviderErrorCategory.Unknown,
                        true);
            }
        }
    }
}

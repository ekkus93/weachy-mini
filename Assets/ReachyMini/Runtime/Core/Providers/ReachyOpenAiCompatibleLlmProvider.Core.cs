#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;

namespace ReachyMini.Providers
{
    // RMA-142/143: no cloud LLM provider class existed at all before this file --
    // following the RMA-144/145 OpenAI-compatible ASR/TTS adapter pattern (one real
    // ReachySharedHttpTransport-backed class per endpoint style, mirroring the
    // RMA-115 VLM base-class/two-thin-subclasses shape). Scope: bounded, non-streaming
    // text-in/JSON-behavior-intent-out generation only. Image input ("transformed-image
    // input where enabled") is deliberately NOT implemented here -- RMA-115's VLM
    // providers already cover image-grounded scene description/questions separately,
    // and RMA-195 Phase D's own framing gates conversational behavior intents on text
    // landing first; ReachyLlmCapabilities.SupportsImages stays false on every
    // provider instance produced by this file, so a caller can detect the mismatch
    // rather than have it silently ignored.
    public abstract partial class ReachyOpenAiCompatibleLlmProviderBase : ICloudLlmProviderCapability, IAsyncDisposable
    {
        private const string InternalBearerSecretReference = "reachy.internal.llm.bearer";
        private const int MaximumJsonResponseDepth = 64;

        private readonly ReachyProviderProfile sourceProfile;
        private readonly IReachyProviderSecretStore? sourceSecretStore;
        private readonly ReachyOpenAiCompatibleLlmOptions options;
        private readonly ReachySharedHttpTransport transport;
        private readonly bool ownsTransport;
        private int disposed;

        private protected ReachyOpenAiCompatibleLlmProviderBase(
            ReachyProviderEndpointStyle requiredEndpointStyle,
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyOpenAiCompatibleLlmOptions options,
            ReachyLlmCapabilities capabilities,
            ReachySharedHttpTransport? transportOverride)
        {
            sourceProfile = profile ?? throw new ArgumentNullException(nameof(profile));
            sourceSecretStore = secretStore;
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            ValidateProfile(profile, requiredEndpointStyle, options.AuthenticationMode);
            EndpointStyle = requiredEndpointStyle;

            if (transportOverride != null)
            {
                transport = transportOverride;
                ownsTransport = false;
            }
            else
            {
                (ReachyProviderProfile transportProfile,
                    IReachyProviderSecretStore? transportSecretStore) =
                    CreateTransportBinding(profile, secretStore, options.AuthenticationMode);
                transport = new ReachySharedHttpTransport(
                    transportProfile,
                    transportSecretStore,
                    ReachyHttpTransportPolicy.FromProfile(transportProfile));
                ownsTransport = true;
            }
        }

        public ReachyProviderEndpointStyle EndpointStyle { get; }

        public ReachyLlmCapabilities Capabilities { get; }

        public async Task<ReachyLlmGenerationResult> GenerateAsync(
            ReachyLlmGenerationRequest request,
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
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(sourceProfile.TimeoutMilliseconds));

            byte[]? requestBody = null;
            byte[]? borrowedResponseBody = null;
            try
            {
                requestBody = BuildRequestBody(
                    EndpointStyle,
                    sourceProfile.GetModelId(ReachyProviderModelRole.Text),
                    options.SystemPrompt,
                    request.Messages,
                    options.MaximumOutputTokens);

                using var transportRequest = new ReachyHttpTransportRequest(
                    HttpMethod.Post,
                    options.RelativeCompletionPath,
                    requestBody,
                    "application/json",
                    "application/json",
                    ReachyHttpResponseMode.Buffered,
                    options.MaximumResponseBytes,
                    maximumSseEventCharacters: 1024,
                    explicitlyAuthorizeNonIdempotentRetry: false,
                    idempotencyKey: null);

                ReachyHttpTransportResult result = await transport.SendAsync(
                        transportRequest,
                        eventSink: null,
                        timeout.Token)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return FailureFromTransport(
                        result,
                        cancellationToken.IsCancellationRequested,
                        timeout.IsCancellationRequested);
                }
                borrowedResponseBody = result.BorrowResponseBodyForTransport() ??
                    throw new InvalidOperationException(
                        "Successful LLM HTTP response omitted its response body.");

                string responseText;
                string? assistantText;
                try
                {
                    responseText = DecodeStrictUtf8(borrowedResponseBody);
                    var parser = new ReachyLlmResponseProtocolParser(
                        responseText,
                        MaximumJsonResponseDepth,
                        ReachyLlmProviderPolicy.MaximumDiagnosticCharacters * 8);
                    assistantText = EndpointStyle == ReachyProviderEndpointStyle.Responses
                        ? parser.ParseResponsesAssistantText()
                        : parser.ParseChatCompletionsAssistantText();
                }
                catch (FormatException exception)
                {
                    return new ReachyLlmGenerationResult(
                        ReachyLlmGenerationStatus.HttpFailure,
                        null,
                        string.Empty,
                        "LLM provider response is not valid strict-UTF8 JSON: " + exception.Message);
                }

                if (assistantText == null)
                {
                    return new ReachyLlmGenerationResult(
                        ReachyLlmGenerationStatus.HttpFailure,
                        null,
                        responseText,
                        "LLM provider response did not contain a recognizable assistant message.");
                }

                ReachyBehaviorIntentValidationResult validation =
                    ReachyBehaviorIntentJsonParser.Validate(assistantText);
                return new ReachyLlmGenerationResult(
                    validation.Succeeded
                        ? ReachyLlmGenerationStatus.BehaviorIntentValid
                        : ReachyLlmGenerationStatus.BehaviorIntentInvalid,
                    validation,
                    assistantText,
                    validation.Detail);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Cancelled();
                }
                return new ReachyLlmGenerationResult(
                    ReachyLlmGenerationStatus.HttpFailure,
                    null,
                    string.Empty,
                    "LLM generation exceeded its bounded timeout.");
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is KeyNotFoundException)
            {
                return new ReachyLlmGenerationResult(
                    ReachyLlmGenerationStatus.HttpFailure,
                    null,
                    string.Empty,
                    "LLM request could not be completed because its bounded provider contract was not satisfied: " +
                        exception.Message);
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

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (ownsTransport)
            {
                transport.Dispose();
            }
            GC.SuppressFinalize(this);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static ReachyLlmGenerationResult Cancelled()
        {
            return new ReachyLlmGenerationResult(
                ReachyLlmGenerationStatus.Cancelled,
                null,
                string.Empty,
                "LLM generation was cancelled.");
        }
    }

    public sealed class OpenAiResponsesLlmProvider : ReachyOpenAiCompatibleLlmProviderBase
    {
        public OpenAiResponsesLlmProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyOpenAiCompatibleLlmOptions options,
            ReachyLlmCapabilities capabilities)
            : base(
                ReachyProviderEndpointStyle.Responses,
                profile,
                secretStore,
                options,
                capabilities,
                transportOverride: null)
        {
        }

        internal OpenAiResponsesLlmProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyOpenAiCompatibleLlmOptions options,
            ReachyLlmCapabilities capabilities,
            ReachySharedHttpTransport transportOverride)
            : base(
                ReachyProviderEndpointStyle.Responses,
                profile,
                secretStore,
                options,
                capabilities,
                transportOverride)
        {
        }
    }

    public sealed class OpenAiChatCompletionsLlmProvider : ReachyOpenAiCompatibleLlmProviderBase
    {
        public OpenAiChatCompletionsLlmProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyOpenAiCompatibleLlmOptions options,
            ReachyLlmCapabilities capabilities)
            : base(
                ReachyProviderEndpointStyle.ChatCompletions,
                profile,
                secretStore,
                options,
                capabilities,
                transportOverride: null)
        {
        }

        internal OpenAiChatCompletionsLlmProvider(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyOpenAiCompatibleLlmOptions options,
            ReachyLlmCapabilities capabilities,
            ReachySharedHttpTransport transportOverride)
            : base(
                ReachyProviderEndpointStyle.ChatCompletions,
                profile,
                secretStore,
                options,
                capabilities,
                transportOverride)
        {
        }
    }
}

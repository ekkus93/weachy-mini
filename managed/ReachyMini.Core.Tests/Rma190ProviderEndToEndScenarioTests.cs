#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    internal static class Rma190ProviderEndToEndScenarioTests
    {
        internal static void RunAll()
        {
            IndependentCloudProviderAdaptersRemainIsolatedAsync().GetAwaiter().GetResult();
            NetworkLossFailsClosedAsync().GetAwaiter().GetResult();
            RateLimitFailsClosedAsync().GetAwaiter().GetResult();
            MalformedResponseFailsClosedAsync().GetAwaiter().GetResult();
            CancellationFailsClosedBeforeNetworkAsync().GetAwaiter().GetResult();
        }

        private static async Task IndependentCloudProviderAdaptersRemainIsolatedAsync()
        {
            string responsesIntent = "{\"schema_version\":1,\"speech\":\"responses-provider\"}";
            string chatIntent = "{\"schema_version\":1,\"gesture\":\"nod\"}";
            using var responsesHandler = new ScenarioHandler((_, _) => JsonResponse(
                HttpStatusCode.OK,
                "{\"output_text\":" + EncodeJsonString(responsesIntent) + "}"));
            using var chatHandler = new ScenarioHandler((_, _) => JsonResponse(
                HttpStatusCode.OK,
                "{\"choices\":[{\"message\":{\"content\":" +
                    EncodeJsonString(chatIntent) + "}}]}"));

            await using ProviderHandle<OpenAiResponsesLlmProvider> responses =
                CreateResponsesProvider(
                    responsesHandler,
                    "rma190-responses-provider",
                    "responses-model",
                    "responses-secret-value");
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> chat =
                CreateChatProvider(
                    chatHandler,
                    "rma190-chat-provider",
                    "chat-model",
                    "chat-secret-value");

            ReachyLlmGenerationResult responsesResult =
                await responses.Provider.GenerateAsync(
                    Request("responses-request"),
                    CancellationToken.None);
            ReachyLlmGenerationResult chatResult =
                await chat.Provider.GenerateAsync(
                    Request("chat-request"),
                    CancellationToken.None);

            Equal(
                ReachyLlmGenerationStatus.BehaviorIntentValid,
                responsesResult.Status,
                "Responses provider result");
            Equal(
                ReachyLlmGenerationStatus.BehaviorIntentValid,
                chatResult.Status,
                "Chat provider result");
            Equal(
                "responses-provider",
                responsesResult.Validation?.Intent?.Speech,
                "Responses provider behavior payload");
            Equal(
                ReachyBehaviorGesture.Nod,
                chatResult.Validation?.Intent?.Gesture,
                "Chat provider behavior payload");

            Equal(1, responsesHandler.CallCount, "Responses provider call count");
            Equal(1, chatHandler.CallCount, "Chat provider call count");
            Contains(
                responsesHandler.LastRequest?.RequestUri?.AbsolutePath ?? string.Empty,
                "/v1/responses",
                "Responses endpoint");
            Contains(
                chatHandler.LastRequest?.RequestUri?.AbsolutePath ?? string.Empty,
                "/v1/chat/completions",
                "Chat endpoint");
            Contains(
                responsesHandler.LastRequestBody ?? string.Empty,
                "\"model\":\"responses-model\"",
                "Responses model binding");
            Excludes(
                responsesHandler.LastRequestBody ?? string.Empty,
                "chat-model",
                "Responses request excludes chat model");
            Contains(
                chatHandler.LastRequestBody ?? string.Empty,
                "\"model\":\"chat-model\"",
                "Chat model binding");
            Excludes(
                chatHandler.LastRequestBody ?? string.Empty,
                "responses-model",
                "Chat request excludes Responses model");
            Equal(
                "Bearer responses-secret-value",
                ReadAuthorization(responsesHandler.LastRequest),
                "Responses credential binding");
            Equal(
                "Bearer chat-secret-value",
                ReadAuthorization(chatHandler.LastRequest),
                "Chat credential binding");
        }

        private static async Task NetworkLossFailsClosedAsync()
        {
            using var handler = new ScenarioHandler((_, _) =>
                throw new HttpRequestException("RMA-190 simulated network loss."));
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatProvider(
                    handler,
                    "rma190-network-loss",
                    "chat-model",
                    "network-secret");

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("network-loss"),
                CancellationToken.None);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "network-loss status");
            Equal(false, result.Succeeded, "network-loss success flag");
            Equal(1, handler.CallCount, "network-loss request count");
        }

        private static async Task RateLimitFailsClosedAsync()
        {
            using var handler = new ScenarioHandler((_, _) => new HttpResponseMessage(
                (HttpStatusCode)429)
            {
                Content = new StringContent(
                    "{\"error\":\"rate limited\"}",
                    Encoding.UTF8,
                    "application/json"),
            });
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatProvider(
                    handler,
                    "rma190-rate-limit",
                    "chat-model",
                    "rate-secret");

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("rate-limit"),
                CancellationToken.None);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "rate-limit status");
            Equal(false, result.Succeeded, "rate-limit success flag");
            Equal(1, handler.CallCount, "rate-limit request count");
        }

        private static async Task MalformedResponseFailsClosedAsync()
        {
            using var handler = new ScenarioHandler((_, _) => JsonResponse(
                HttpStatusCode.OK,
                "{not-valid-json"));
            await using ProviderHandle<OpenAiResponsesLlmProvider> handle =
                CreateResponsesProvider(
                    handler,
                    "rma190-malformed-response",
                    "responses-model",
                    "malformed-secret");

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("malformed-response"),
                CancellationToken.None);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "malformed-response status");
            Equal(false, result.Succeeded, "malformed-response success flag");
            Equal(1, handler.CallCount, "malformed-response request count");
        }

        private static async Task CancellationFailsClosedBeforeNetworkAsync()
        {
            using var handler = new ScenarioHandler((_, _) => JsonResponse(
                HttpStatusCode.OK,
                "{\"output_text\":\"{\\\"schema_version\\\":1}\"}"));
            await using ProviderHandle<OpenAiResponsesLlmProvider> handle =
                CreateResponsesProvider(
                    handler,
                    "rma190-cancelled",
                    "responses-model",
                    "cancel-secret");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("cancelled"),
                cancellation.Token);

            Equal(
                ReachyLlmGenerationStatus.Cancelled,
                result.Status,
                "cancellation status");
            Equal(false, result.Succeeded, "cancellation success flag");
            Equal(0, handler.CallCount, "cancelled request never reaches network");
        }

        private static ReachyLlmGenerationRequest Request(string requestId)
        {
            return new ReachyLlmGenerationRequest(
                requestId,
                new[]
                {
                    new ReachyLlmChatMessage(
                        ReachyLlmChatRole.User,
                        "Produce one validated behavior intent."),
                });
        }

        private static ProviderHandle<OpenAiResponsesLlmProvider> CreateResponsesProvider(
            ScenarioHandler handler,
            string providerId,
            string modelId,
            string secret)
        {
            ReachyProviderProfile profile = BuildProfile(
                providerId,
                ReachyProviderEndpointStyle.Responses,
                new Uri("https://responses.example.test/"),
                modelId);
            FakeSecretStore sourceSecrets = BuildSecrets(secret);
            (ReachyProviderProfile transportProfile, IReachyProviderSecretStore transportSecrets) =
                ApplyBearerBinding(profile, sourceSecrets, providerId);
            var transport = new ReachySharedHttpTransport(
                transportProfile,
                transportSecrets,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiResponsesLlmProvider(
                profile,
                sourceSecrets,
                ReachyOpenAiCompatibleLlmOptions.OpenAiResponsesDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle<OpenAiResponsesLlmProvider>(provider, transport);
        }

        private static ProviderHandle<OpenAiChatCompletionsLlmProvider> CreateChatProvider(
            ScenarioHandler handler,
            string providerId,
            string modelId,
            string secret)
        {
            ReachyProviderProfile profile = BuildProfile(
                providerId,
                ReachyProviderEndpointStyle.ChatCompletions,
                new Uri("https://chat.example.test/"),
                modelId);
            FakeSecretStore sourceSecrets = BuildSecrets(secret);
            (ReachyProviderProfile transportProfile, IReachyProviderSecretStore transportSecrets) =
                ApplyBearerBinding(profile, sourceSecrets, providerId);
            var transport = new ReachySharedHttpTransport(
                transportProfile,
                transportSecrets,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                sourceSecrets,
                ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle<OpenAiChatCompletionsLlmProvider>(provider, transport);
        }

        private static ReachyProviderProfile BuildProfile(
            string providerId,
            ReachyProviderEndpointStyle endpointStyle,
            Uri baseUri,
            string modelId)
        {
            return new ReachyProviderProfile(
                providerId,
                providerId,
                baseUri,
                endpointStyle,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        modelId),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                timeoutMilliseconds: 30000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "llm-cred");
        }

        private static FakeSecretStore BuildSecrets(string secret)
        {
            var store = new FakeSecretStore();
            store.Put("llm-cred", Encoding.ASCII.GetBytes(secret));
            return store;
        }

        private static (
            ReachyProviderProfile profile,
            IReachyProviderSecretStore secretStore) ApplyBearerBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore sourceSecrets,
            string providerId)
        {
            ReachyBearerCredentialTransportBinding binding =
                ReachyBearerCredentialTransportBinding.Create(
                    profile,
                    sourceSecrets,
                    "reachy.internal.rma190." + providerId);
            return (binding.Profile, binding.SecretStore);
        }

        private static string ReadAuthorization(HttpRequestMessage? request)
        {
            if (request == null ||
                !request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values))
            {
                return string.Empty;
            }
            return string.Join(",", values);
        }

        private static string EncodeJsonString(string value)
        {
            var builder = new StringBuilder();
            builder.Append('"');
            foreach (char character in value)
            {
                if (character == '"' || character == '\\')
                {
                    builder.Append('\\');
                }
                builder.Append(character);
            }
            builder.Append('"');
            return builder.ToString();
        }

        private static HttpResponseMessage JsonResponse(
            HttpStatusCode statusCode,
            string body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider end-to-end failed for {label}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }

        private static void Contains(string value, string expected, string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider end-to-end failed for {label}: " +
                    $"missing '{expected}'.");
            }
        }

        private static void Excludes(string value, string unexpected, string label)
        {
            if (value.Contains(unexpected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider end-to-end failed for {label}: " +
                    $"unexpected '{unexpected}'.");
            }
        }

        private readonly struct ProviderHandle<T> : IAsyncDisposable
            where T : IAsyncDisposable
        {
            private readonly ReachySharedHttpTransport transport;

            public ProviderHandle(T provider, ReachySharedHttpTransport transport)
            {
                Provider = provider;
                this.transport = transport;
            }

            public T Provider { get; }

            public async ValueTask DisposeAsync()
            {
                await Provider.DisposeAsync();
                transport.Dispose();
            }
        }

        private sealed class ScenarioHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond;

            public ScenarioHandler(
                Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public int CallCount { get; private set; }

            public HttpRequestMessage? LastRequest { get; private set; }

            public string? LastRequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                LastRequest = request;
                LastRequestBody = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                HttpResponseMessage response = respond(request, cancellationToken);
                response.RequestMessage = request;
                return response;
            }
        }

        private sealed class ImmediateBackoffDelay : IReachyHttpBackoffDelay
        {
            public ValueTask DelayAsync(
                TimeSpan delay,
                CancellationToken cancellationToken)
            {
                return default;
            }
        }

        private sealed class FakeSecretStore : IReachyProviderSecretStore
        {
            private readonly Dictionary<string, byte[]> secrets =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public void Put(string reference, byte[] secretUtf8)
            {
                secrets[reference] = (byte[])secretUtf8.Clone();
            }

            public byte[] GetSecret(string reference)
            {
                return (byte[])secrets[reference].Clone();
            }

            public bool Contains(string reference)
            {
                return secrets.ContainsKey(reference);
            }

            public bool Delete(string reference)
            {
                return secrets.Remove(reference);
            }
        }
    }
}

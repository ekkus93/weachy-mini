#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    // RMA-142/143: exercises ReachyOpenAiCompatibleLlmProviderBase against a fake
    // HttpMessageHandler through ReachySharedHttpTransport's real request-building,
    // auth-header, and response-parsing pipeline -- a genuine mock-server contract
    // test, not an interface-level fake, per the ticket's explicit ask ("Add
    // mock-server contract tests"). Requires the InternalsVisibleTo declared in
    // Assets/ReachyMini/Runtime/Core/ReachyMiniCoreAssemblyInfo.cs.
    internal static class Rma142Rma143OpenAiLlmAdapterContractTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            ChatCompletionsAdapterProducesValidBehaviorIntent().GetAwaiter().GetResult();
            ResponsesAdapterExtractsFlattenedOutputText().GetAwaiter().GetResult();
            ResponsesAdapterWalksNestedOutputContentArray().GetAwaiter().GetResult();
            MalformedAssistantJsonSurfacesAsInvalidNotThrown().GetAwaiter().GetResult();
            AuthorizationHeaderCarriesBearerCredential().GetAwaiter().GetResult();
            HttpAuthenticationFailureSurfacesAsHttpFailure().GetAwaiter().GetResult();
            CancellationBeforeSendSurfacesAsCancelled().GetAwaiter().GetResult();
            CapabilitiesDefaultToTextOnly();
        }

        private static async Task ChatCompletionsAdapterProducesValidBehaviorIntent()
        {
            string assistantJson = "{\"schema_version\":1,\"speech\":\"Hello there.\"}";
            string body = "{\"choices\":[{\"message\":{\"content\":" +
                EncodeJsonString(assistantJson) + "}}]}";
            using var handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, body));

            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);
            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("greet the person"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.BehaviorIntentValid, result.Status, "chat-completions status");
            True(result.Succeeded, "chat-completions succeeded");
            Equal("Hello there.", result.Validation?.Intent?.Speech, "chat-completions speech");
            Equal(1, handler.CallCount, "chat-completions call count");
            True(
                handler.LastRequest != null &&
                    handler.LastRequest.RequestUri!.AbsolutePath.EndsWith(
                        "/v1/chat/completions",
                        StringComparison.Ordinal),
                "chat-completions request path");
        }

        private static async Task ResponsesAdapterExtractsFlattenedOutputText()
        {
            string assistantJson = "{\"schema_version\":1,\"gesture\":\"nod\"}";
            string body = "{\"output_text\":" + EncodeJsonString(assistantJson) + "}";
            using var handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, body));

            await using ProviderHandle<OpenAiResponsesLlmProvider> handle = CreateResponsesProvider(handler);
            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("nod if you understand"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.BehaviorIntentValid, result.Status, "responses flattened status");
            Equal(ReachyBehaviorGesture.Nod, result.Validation?.Intent?.Gesture, "responses flattened gesture");
            True(
                handler.LastRequest != null &&
                    handler.LastRequest.RequestUri!.AbsolutePath.EndsWith(
                        "/v1/responses",
                        StringComparison.Ordinal),
                "responses request path");
        }

        private static async Task ResponsesAdapterWalksNestedOutputContentArray()
        {
            string assistantJson = "{\"schema_version\":1,\"urgency\":\"high\"}";
            string body = "{\"output\":[{\"type\":\"message\",\"content\":[" +
                "{\"type\":\"reasoning\",\"text\":\"ignored\"}," +
                "{\"type\":\"output_text\",\"text\":" + EncodeJsonString(assistantJson) + "}" +
                "]}]}";
            using var handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, body));

            await using ProviderHandle<OpenAiResponsesLlmProvider> handle = CreateResponsesProvider(handler);
            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("urgent?"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.BehaviorIntentValid, result.Status, "responses nested status");
            Equal(ReachyBehaviorUrgency.High, result.Validation?.Intent?.Urgency, "responses nested urgency");
        }

        private static async Task MalformedAssistantJsonSurfacesAsInvalidNotThrown()
        {
            string body = "{\"choices\":[{\"message\":{\"content\":\"not json at all\"}}]}";
            using var handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, body));

            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);
            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("say something"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.BehaviorIntentInvalid, result.Status, "malformed assistant status");
            False(result.Succeeded, "malformed assistant not succeeded");
            True(result.Validation != null, "malformed assistant validation present");
        }

        private static async Task AuthorizationHeaderCarriesBearerCredential()
        {
            using var handler = new FakeHandler((_, _) => JsonResponse(
                HttpStatusCode.OK,
                "{\"choices\":[{\"message\":{\"content\":\"{\\\"schema_version\\\":1}\"}}]}"));
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);

            _ = await handle.Provider.GenerateAsync(SingleUserMessageRequest("hi"), CancellationToken.None);

            IEnumerable<string>? values = null;
            bool hasHeader = handler.LastRequest?.Headers.TryGetValues("Authorization", out values) == true;
            True(hasHeader, "authorization header present");
            string authorizationValue = new List<string>(values!)[0];
            Equal("Bearer test-secret-value", authorizationValue, "authorization header value");
        }

        private static async Task HttpAuthenticationFailureSurfacesAsHttpFailure()
        {
            using var handler = new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("denied", Encoding.UTF8, "text/plain"),
                });
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("hi"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.HttpFailure, result.Status, "auth failure status");
            False(result.Succeeded, "auth failure not succeeded");
        }

        private static async Task CancellationBeforeSendSurfacesAsCancelled()
        {
            using var handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, "{}"));
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("hi"),
                cancelled.Token);

            Equal(ReachyLlmGenerationStatus.Cancelled, result.Status, "pre-cancelled status");
            Equal(0, handler.CallCount, "pre-cancelled call count");
        }

        private static void CapabilitiesDefaultToTextOnly()
        {
            ReachyLlmCapabilities capabilities = ReachyLlmCapabilities.TextOnly();
            False(capabilities.SupportsImages, "text-only images");
            False(capabilities.SupportsToolCalls, "text-only tool calls");
            False(capabilities.SupportsJsonSchema, "text-only json schema");
            False(capabilities.SupportsStreaming, "text-only streaming");
        }

        private static ProviderHandle<OpenAiChatCompletionsLlmProvider> CreateChatCompletionsProvider(
            FakeHandler handler)
        {
            var profile = BuildProfile(ReachyProviderEndpointStyle.ChatCompletions);
            var secretStore = new FakeSecretStore();
            secretStore.Put("llm-cred", Encoding.ASCII.GetBytes("test-secret-value"));
            // The provider's own constructor applies this bearer-credential
            // transformation internally when it builds its own transport; supplying
            // a transportOverride bypasses that constructor path entirely, so the
            // test must reproduce it here to exercise the real Authorization header
            // this provider is configured to send.
            (ReachyProviderProfile transportProfile, IReachyProviderSecretStore transportSecretStore) =
                ApplyBearerBinding(profile, secretStore);
            var transport = new ReachySharedHttpTransport(
                transportProfile,
                transportSecretStore,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle<OpenAiChatCompletionsLlmProvider>(provider, transport);
        }

        private static ProviderHandle<OpenAiResponsesLlmProvider> CreateResponsesProvider(FakeHandler handler)
        {
            var profile = BuildProfile(ReachyProviderEndpointStyle.Responses);
            var secretStore = new FakeSecretStore();
            secretStore.Put("llm-cred", Encoding.ASCII.GetBytes("test-secret-value"));
            (ReachyProviderProfile transportProfile, IReachyProviderSecretStore transportSecretStore) =
                ApplyBearerBinding(profile, secretStore);
            var transport = new ReachySharedHttpTransport(
                transportProfile,
                transportSecretStore,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiResponsesLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions.OpenAiResponsesDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle<OpenAiResponsesLlmProvider>(provider, transport);
        }

        private static (ReachyProviderProfile profile, IReachyProviderSecretStore secretStore) ApplyBearerBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore secretStore)
        {
            ReachyBearerCredentialTransportBinding bearer = ReachyBearerCredentialTransportBinding.Create(
                profile,
                secretStore,
                "reachy.internal.llm.bearer.test");
            return (bearer.Profile, bearer.SecretStore);
        }

        private static ReachyProviderProfile BuildProfile(ReachyProviderEndpointStyle endpointStyle)
        {
            return new ReachyProviderProfile(
                "rma142-test-provider",
                "RMA-142 Test Provider",
                new Uri("https://llm.example.test/"),
                endpointStyle,
                new[] { new ReachyProviderModelBinding(ReachyProviderModelRole.Text, "gpt-test") },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "llm-cred");
        }

        private static ReachyLlmGenerationRequest SingleUserMessageRequest(string userText)
        {
            return new ReachyLlmGenerationRequest(
                "req-1",
                new[] { new ReachyLlmChatMessage(ReachyLlmChatRole.User, userText) });
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

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-142/143 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-142/143 contract failed for {description}: expected true.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-142/143 contract failed for {description}: expected false.");
            }
        }

        // The provider's internal transportOverride constructor always sets
        // ownsTransport=false (the override is assumed borrowed, not owned) --
        // production callers that build their own transport separately from a
        // provider are expected to own and dispose it themselves, exactly as this
        // test handle does.
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

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond;

            public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public int CallCount { get; private set; }

            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                LastRequest = request;
                HttpResponseMessage response = respond(request, cancellationToken);
                // The real HttpClient pipeline stamps this automatically; a raw
                // handler standing in for the network must do it itself, or
                // ReachySharedHttpTransport's final-URI redirect check (which reads
                // response.RequestMessage.RequestUri) treats every response as an
                // untrusted redirect target.
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }

        private sealed class ImmediateBackoffDelay : IReachyHttpBackoffDelay
        {
            public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                return default;
            }
        }

        private sealed class FakeSecretStore : IReachyProviderSecretStore
        {
            private readonly Dictionary<string, byte[]> secrets = new Dictionary<string, byte[]>(StringComparer.Ordinal);

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

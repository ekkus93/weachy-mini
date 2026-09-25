#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    internal static class Rma190ProviderFailureMatrixEndToEndTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            NetworkLossRetriesBoundedlyAndFailsClosed().GetAwaiter().GetResult();
            RateLimitRetriesBoundedlyAndFailsClosed().GetAwaiter().GetResult();
            MalformedProviderPayloadCannotBecomeBehaviorSuccess().GetAwaiter().GetResult();
            CancellationStopsBeforeNetwork().GetAwaiter().GetResult();
        }

        private static async Task NetworkLossRetriesBoundedlyAndFailsClosed()
        {
            using var handler = new FakeHandler((_, _) =>
                throw new HttpRequestException("simulated network loss"));
            var delay = new RecordingBackoffDelay();
            using ReachySharedHttpTransport transport = CreateTransport(
                handler,
                delay,
                maximumAttempts: 2);
            using ReachyHttpTransportRequest request = GetRequest();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                eventSink: null,
                CancellationToken.None);

            False(result.Succeeded, "network loss succeeded");
            Equal<ReachyHttpErrorCategory?>(ReachyHttpErrorCategory.Network, result.Error?.Category, "network loss category");
            Equal(2, result.Attempts, "network loss bounded attempt count");
            Equal(2, handler.CallCount, "network loss handler call count");
            Equal(1, delay.CallCount, "network loss retry backoff count");
        }

        private static async Task RateLimitRetriesBoundedlyAndFailsClosed()
        {
            using var handler = new FakeHandler((request, _) =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
                    RequestMessage = request,
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.Zero);
                return response;
            });
            var delay = new RecordingBackoffDelay();
            using ReachySharedHttpTransport transport = CreateTransport(
                handler,
                delay,
                maximumAttempts: 3);
            using ReachyHttpTransportRequest request = GetRequest();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                eventSink: null,
                CancellationToken.None);

            False(result.Succeeded, "rate limit succeeded");
            Equal<ReachyHttpErrorCategory?>(
                ReachyHttpErrorCategory.QuotaOrRateLimited,
                result.Error?.Category,
                "rate limit category");
            Equal<int?>(429, result.Error?.HttpStatusCode, "rate limit HTTP status");
            Equal(3, result.Attempts, "rate limit bounded attempt count");
            Equal(3, handler.CallCount, "rate limit handler call count");
            Equal(2, delay.CallCount, "rate limit retry backoff count");
        }

        private static async Task MalformedProviderPayloadCannotBecomeBehaviorSuccess()
        {
            string body = "{\"choices\":[{\"message\":{\"content\":\"not behavior json\"}}]}";
            using var handler = new FakeHandler((request, _) =>
                JsonResponse(request, HttpStatusCode.OK, body));
            var profile = BuildProfile();
            var secretStore = new FakeSecretStore();
            secretStore.Put("llm-cred", Encoding.ASCII.GetBytes("test-secret-value"));
            ReachyBearerCredentialTransportBinding bearer =
                ReachyBearerCredentialTransportBinding.Create(
                    profile,
                    secretStore,
                    "reachy.internal.rma190.llm.bearer.test");
            using var transport = new ReachySharedHttpTransport(
                bearer.Profile,
                bearer.SecretStore,
                policy: new ReachyHttpTransportPolicy(1000, 1000, 1, 0, 0),
                backoffDelay: new RecordingBackoffDelay(),
                handler: handler);
            await using var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);

            ReachyLlmGenerationResult result = await provider.GenerateAsync(
                new ReachyLlmGenerationRequest(
                    "rma190-malformed",
                    new[]
                    {
                        new ReachyLlmChatMessage(
                            ReachyLlmChatRole.User,
                            "Return a behavior intent."),
                    }),
                CancellationToken.None);

            False(result.Succeeded, "malformed provider payload succeeded");
            Equal(
                ReachyLlmGenerationStatus.BehaviorIntentInvalid,
                result.Status,
                "malformed provider payload status");
            True(result.Validation != null, "malformed provider validation evidence");
            Equal(1, handler.CallCount, "malformed provider handler call count");
        }

        private static async Task CancellationStopsBeforeNetwork()
        {
            using var handler = new FakeHandler((request, _) =>
                JsonResponse(request, HttpStatusCode.OK, "{}"));
            using ReachySharedHttpTransport transport = CreateTransport(
                handler,
                new RecordingBackoffDelay(),
                maximumAttempts: 3);
            using ReachyHttpTransportRequest request = GetRequest();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                eventSink: null,
                cancellation.Token);

            False(result.Succeeded, "cancelled request succeeded");
            Equal<ReachyHttpErrorCategory?>(ReachyHttpErrorCategory.Cancelled, result.Error?.Category, "cancellation category");
            Equal(1, result.Attempts, "pre-send cancellation attempt accounting");
            Equal(0, handler.CallCount, "pre-send cancellation network calls");
        }

        private static ReachySharedHttpTransport CreateTransport(
            HttpMessageHandler handler,
            IReachyHttpBackoffDelay delay,
            int maximumAttempts)
        {
            return new ReachySharedHttpTransport(
                BuildProfile(),
                secretStore: null,
                policy: new ReachyHttpTransportPolicy(
                    connectionTimeoutMilliseconds: 1000,
                    readTimeoutMilliseconds: 1000,
                    maximumAttempts: maximumAttempts,
                    baseBackoffMilliseconds: 0,
                    maximumBackoffMilliseconds: 0),
                backoffDelay: delay,
                handler: handler);
        }

        private static ReachyProviderProfile BuildProfile()
        {
            return new ReachyProviderProfile(
                "rma190-provider-failure-test",
                "RMA-190 Provider Failure Test",
                new Uri("https://llm.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        "gpt-test"),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                1000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "llm-cred");
        }

        private static ReachyHttpTransportRequest GetRequest()
        {
            return new ReachyHttpTransportRequest(
                HttpMethod.Get,
                "v1/models",
                body: null,
                contentType: null,
                accept: "application/json",
                ReachyHttpResponseMode.Buffered,
                maximumResponseBytes: 4096,
                maximumSseEventCharacters: 4096,
                explicitlyAuthorizeNonIdempotentRetry: false,
                idempotencyKey: null);
        }

        private static HttpResponseMessage JsonResponse(
            HttpRequestMessage request,
            HttpStatusCode statusCode,
            string body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider-failure matrix acceptance failed for {description}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider-failure matrix acceptance failed for {description}: expected true.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider-failure matrix acceptance failed for {description}: expected false.");
            }
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond;

            internal FakeHandler(
                Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            internal int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                HttpResponseMessage response = respond(request, cancellationToken);
                response.RequestMessage ??= request;
                return Task.FromResult(response);
            }
        }

        private sealed class RecordingBackoffDelay : IReachyHttpBackoffDelay
        {
            internal int CallCount { get; private set; }

            public ValueTask DelayAsync(
                TimeSpan delay,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount = checked(CallCount + 1);
                return default;
            }
        }

        private sealed class FakeSecretStore : IReachyProviderSecretStore
        {
            private readonly Dictionary<string, byte[]> secrets =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);

            internal void Put(string reference, byte[] secretUtf8)
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

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
    internal static class Rma190ProviderFailureEndToEndTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            NetworkLossFailsClosedWithoutRetry().GetAwaiter().GetResult();
            RateLimitFailsClosedWithoutRetry().GetAwaiter().GetResult();
            MalformedProviderPayloadCannotBecomeSuccess().GetAwaiter().GetResult();
            InFlightCallerCancellationStopsTransport().GetAwaiter().GetResult();
            Console.WriteLine("RMA-190 provider failure end-to-end tests passed.");
        }

        private static async Task NetworkLossFailsClosedWithoutRetry()
        {
            using var handler = new AsyncFakeHandler(
                (_, _) => Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("deterministic network loss")));
            await using ProviderHandle handle = CreateProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("network-loss"),
                CancellationToken.None).ConfigureAwait(false);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "network-loss status");
            False(result.Succeeded, "network loss cannot succeed");
            Equal(1, handler.CallCount, "network-loss attempt count");
        }

        private static async Task RateLimitFailsClosedWithoutRetry()
        {
            using var handler = new AsyncFakeHandler(
                (_, _) => Task.FromResult(
                    TextResponse(
                        (HttpStatusCode)429,
                        "rate limited")));
            await using ProviderHandle handle = CreateProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("rate-limit"),
                CancellationToken.None).ConfigureAwait(false);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "rate-limit status");
            False(result.Succeeded, "rate limit cannot succeed");
            Equal(1, handler.CallCount, "rate-limit attempt count");
        }

        private static async Task MalformedProviderPayloadCannotBecomeSuccess()
        {
            using var handler = new AsyncFakeHandler(
                (_, _) => Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        "{\"choices\":[]}")));
            await using ProviderHandle handle = CreateProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                Request("malformed-provider-response"),
                CancellationToken.None).ConfigureAwait(false);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "malformed provider payload status");
            False(result.Succeeded, "malformed provider payload cannot succeed");
            Equal(1, handler.CallCount, "malformed provider attempt count");
        }

        private static async Task InFlightCallerCancellationStopsTransport()
        {
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new AsyncFakeHandler(
                async (_, cancellationToken) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        "Cancelled transport unexpectedly resumed.");
                });
            await using ProviderHandle handle = CreateProvider(handler);
            using var cancellation = new CancellationTokenSource();

            Task<ReachyLlmGenerationResult> generation =
                handle.Provider.GenerateAsync(
                    Request("in-flight-cancel"),
                    cancellation.Token);
            await started.Task.ConfigureAwait(false);
            cancellation.Cancel();
            ReachyLlmGenerationResult result =
                await generation.ConfigureAwait(false);

            Equal(
                ReachyLlmGenerationStatus.Cancelled,
                result.Status,
                "in-flight cancellation status");
            False(result.Succeeded, "cancelled generation cannot succeed");
            Equal(1, handler.CallCount, "cancelled transport attempt count");
        }

        private static ProviderHandle CreateProvider(HttpMessageHandler handler)
        {
            ReachyProviderProfile profile = new ReachyProviderProfile(
                "rma190-provider-failure-test",
                "RMA-190 provider failure test",
                new Uri("https://llm.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        "gpt-test"),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30_000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "rma190-test-key");

            var secretStore = new FakeSecretStore();
            secretStore.Put(
                "rma190-test-key",
                Encoding.ASCII.GetBytes("test-secret-value"));
            ReachyBearerCredentialTransportBinding bearer =
                ReachyBearerCredentialTransportBinding.Create(
                    profile,
                    secretStore,
                    "reachy.internal.rma190.bearer.test");
            var transport = new ReachySharedHttpTransport(
                bearer.Profile,
                bearer.SecretStore,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions
                    .OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle(provider, transport);
        }

        private static ReachyLlmGenerationRequest Request(string requestId)
        {
            return new ReachyLlmGenerationRequest(
                requestId,
                new[]
                {
                    new ReachyLlmChatMessage(
                        ReachyLlmChatRole.User,
                        "Return a bounded behavior intent."),
                });
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

        private static HttpResponseMessage TextResponse(
            HttpStatusCode statusCode,
            string body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "text/plain"),
            };
        }

        private static void Equal<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "RMA-190 provider failure test failed for " +
                    description + ": expected=" + expected +
                    "; actual=" + actual + ".");
            }
        }

        private static void False(bool value, string description)
        {
            if (value)
            {
                throw new InvalidOperationException(
                    "RMA-190 provider failure test failed for " +
                    description + ": expected false.");
            }
        }

        private sealed class AsyncFakeHandler : HttpMessageHandler
        {
            private readonly Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> respond;

            internal AsyncFakeHandler(
                Func<
                    HttpRequestMessage,
                    CancellationToken,
                    Task<HttpResponseMessage>> respond)
            {
                this.respond = respond;
            }

            internal int CallCount { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                HttpResponseMessage response =
                    await respond(request, cancellationToken)
                        .ConfigureAwait(false);
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
                cancellationToken.ThrowIfCancellationRequested();
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

        private readonly struct ProviderHandle : IAsyncDisposable
        {
            private readonly ReachySharedHttpTransport transport;

            internal ProviderHandle(
                OpenAiChatCompletionsLlmProvider provider,
                ReachySharedHttpTransport transport)
            {
                Provider = provider;
                this.transport = transport;
            }

            internal OpenAiChatCompletionsLlmProvider Provider { get; }

            public async ValueTask DisposeAsync()
            {
                await Provider.DisposeAsync().ConfigureAwait(false);
                transport.Dispose();
            }
        }
    }
}

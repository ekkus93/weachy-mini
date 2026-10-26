#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static async Task TestRma190ProviderNetworkFailureScenarioAsync()
        {
            using (var handler = new Rma190HttpHandler((_, _) =>
                throw new HttpRequestException("synthetic network loss")))
            {
                await using Rma190ProviderHandle handle = CreateRma190Provider(handler);
                ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                    Rma190LlmRequest("network loss"),
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(
                    ReachyLlmGenerationStatus.HttpFailure,
                    result.Status,
                    "RMA-190 network loss is fail-closed");
                AssertEqual(false, result.Succeeded, "RMA-190 network loss does not fabricate success");
                if (!result.Detail.Contains("HTTP connection failed", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "RMA-190 network loss did not preserve the typed transport diagnostic.");
                }
            }

            using (var handler = new Rma190HttpHandler((request, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
                    RequestMessage = request,
                };
                return response;
            }))
            {
                await using Rma190ProviderHandle handle = CreateRma190Provider(handler);
                ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                    Rma190LlmRequest("rate limit"),
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(
                    ReachyLlmGenerationStatus.HttpFailure,
                    result.Status,
                    "RMA-190 rate limit is fail-closed");
                if (!result.Detail.Contains("quota or rate-limit", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "RMA-190 rate limit did not preserve the typed transport diagnostic.");
                }
            }

            using (var handler = new Rma190HttpHandler((request, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
                    RequestMessage = request,
                };
                return response;
            }))
            {
                await using Rma190ProviderHandle handle = CreateRma190Provider(handler);
                ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                    Rma190LlmRequest("malformed response"),
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(
                    ReachyLlmGenerationStatus.HttpFailure,
                    result.Status,
                    "RMA-190 malformed response is fail-closed");
                AssertEqual(false, result.Succeeded, "RMA-190 malformed response does not fabricate success");
            }

            using (var handler = new Rma190HttpHandler((request, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                    RequestMessage = request,
                };
                return response;
            }))
            {
                await using Rma190ProviderHandle handle = CreateRma190Provider(handler);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                    Rma190LlmRequest("cancel"),
                    cancelled.Token).ConfigureAwait(false);
                AssertEqual(
                    ReachyLlmGenerationStatus.Cancelled,
                    result.Status,
                    "RMA-190 cancellation status");
                AssertEqual(0, handler.CallCount, "RMA-190 cancelled request never reaches network");
            }
        }

        private static Rma190ProviderHandle CreateRma190Provider(Rma190HttpHandler handler)
        {
            var profile = new ReachyProviderProfile(
                "rma190-cloud-failure-provider",
                "RMA-190 Cloud Failure Provider",
                new Uri("https://llm.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[] { new ReachyProviderModelBinding(ReachyProviderModelRole.Text, "gpt-test") },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "rma190-llm-cred");
            var secretStore = new Rma190SecretStore();
            secretStore.Put("rma190-llm-cred", Encoding.ASCII.GetBytes("test-secret-value"));
            ReachyBearerCredentialTransportBinding bearer =
                ReachyBearerCredentialTransportBinding.Create(
                    profile,
                    secretStore,
                    "reachy.internal.rma190.llm.bearer");
            var transport = new ReachySharedHttpTransport(
                bearer.Profile,
                bearer.SecretStore,
                policy: null,
                backoffDelay: new Rma190ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new Rma190ProviderHandle(provider, transport);
        }

        private static ReachyLlmGenerationRequest Rma190LlmRequest(string text)
        {
            return new ReachyLlmGenerationRequest(
                "rma190-failure-request",
                new[] { new ReachyLlmChatMessage(ReachyLlmChatRole.User, text) });
        }

        private readonly struct Rma190ProviderHandle : IAsyncDisposable
        {
            private readonly ReachySharedHttpTransport transport;

            internal Rma190ProviderHandle(
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

        private sealed class Rma190HttpHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond;

            internal Rma190HttpHandler(
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

        private sealed class Rma190ImmediateBackoffDelay : IReachyHttpBackoffDelay
        {
            public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class Rma190SecretStore : IReachyProviderSecretStore
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

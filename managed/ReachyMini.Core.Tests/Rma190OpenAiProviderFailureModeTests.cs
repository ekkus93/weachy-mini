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
    // RMA-190: provider failure scenarios must fail closed through the same
    // shipping OpenAI-compatible LLM transport path used by RMA-142/143. These
    // contracts cover explicit network-loss and rate-limit surfaces without
    // allowing a fabricated behavior intent, provider fallback, or hidden retry.
    internal static class Rma190OpenAiProviderFailureModeTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            NetworkLossSurfacesAsHttpFailure().GetAwaiter().GetResult();
            RateLimitSurfacesAsHttpFailure().GetAwaiter().GetResult();
        }

        private static async Task NetworkLossSurfacesAsHttpFailure()
        {
            using var handler = new FakeHandler((_, _) =>
                throw new HttpRequestException("synthetic network loss"));
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("produce a safe behavior intent"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.HttpFailure, result.Status, "network loss status");
            False(result.Succeeded, "network loss cannot fabricate success");
            Equal(1, handler.CallCount, "network loss call count");
        }

        private static async Task RateLimitSurfacesAsHttpFailure()
        {
            using var handler = new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
                });
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("produce a safe behavior intent"),
                CancellationToken.None);

            Equal(ReachyLlmGenerationStatus.HttpFailure, result.Status, "rate-limit status");
            False(result.Succeeded, "rate-limit cannot fabricate success");
            Equal(1, handler.CallCount, "rate-limit call count");
        }

        private static ProviderHandle<OpenAiChatCompletionsLlmProvider> CreateChatCompletionsProvider(
            FakeHandler handler)
        {
            ReachyProviderProfile profile = BuildProfile();
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
            var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle<OpenAiChatCompletionsLlmProvider>(provider, transport);
        }

        private static (ReachyProviderProfile profile, IReachyProviderSecretStore secretStore) ApplyBearerBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore secretStore)
        {
            ReachyBearerCredentialTransportBinding bearer = ReachyBearerCredentialTransportBinding.Create(
                profile,
                secretStore,
                "reachy.internal.rma190.llm.bearer.test");
            return (bearer.Profile, bearer.SecretStore);
        }

        private static ReachyProviderProfile BuildProfile()
        {
            return new ReachyProviderProfile(
                "rma190-test-provider",
                "RMA-190 Test Provider",
                new Uri("https://llm.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
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
                "rma190-network-failure-request",
                new[] { new ReachyLlmChatMessage(ReachyLlmChatRole.User, userText) });
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider failure contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider failure contract failed for {description}: expected false.");
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

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond;

            public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                HttpResponseMessage response = respond(request, cancellationToken);
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

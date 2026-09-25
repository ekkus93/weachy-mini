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
    internal static class Rma190OpenAiLlmFailureModeContractTests
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
                throw new HttpRequestException("synthetic RMA-190 network loss"));
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("hi"),
                CancellationToken.None);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "RMA-190 LLM network loss status");
            False(result.Succeeded, "RMA-190 LLM network loss not succeeded");
            True(handler.CallCount >= 1, "RMA-190 LLM network loss attempted request");
        }

        private static async Task RateLimitSurfacesAsHttpFailure()
        {
            using var handler = new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        "rate limited",
                        Encoding.UTF8,
                        "text/plain"),
                });
            await using ProviderHandle<OpenAiChatCompletionsLlmProvider> handle =
                CreateChatCompletionsProvider(handler);

            ReachyLlmGenerationResult result = await handle.Provider.GenerateAsync(
                SingleUserMessageRequest("hi"),
                CancellationToken.None);

            Equal(
                ReachyLlmGenerationStatus.HttpFailure,
                result.Status,
                "RMA-190 LLM rate-limit status");
            False(result.Succeeded, "RMA-190 LLM rate-limit not succeeded");
            True(handler.CallCount >= 1, "RMA-190 LLM rate-limit attempted request");
        }

        private static ProviderHandle<OpenAiChatCompletionsLlmProvider>
            CreateChatCompletionsProvider(FakeHandler handler)
        {
            var profile = new ReachyProviderProfile(
                "rma190-llm-failure-provider",
                "RMA-190 LLM Failure Provider",
                new Uri("https://llm.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Text,
                        "gpt-test"),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "llm-cred");
            var secretStore = new FakeSecretStore();
            secretStore.Put(
                "llm-cred",
                Encoding.ASCII.GetBytes("test-secret-value"));
            ReachyBearerCredentialTransportBinding bearer =
                ReachyBearerCredentialTransportBinding.Create(
                    profile,
                    secretStore,
                    "reachy.internal.llm.rma190.bearer.test");
            var transport = new ReachySharedHttpTransport(
                bearer.Profile,
                bearer.SecretStore,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            var provider = new OpenAiChatCompletionsLlmProvider(
                profile,
                secretStore,
                ReachyOpenAiCompatibleLlmOptions.OpenAiChatCompletionsDefault(),
                ReachyLlmCapabilities.TextOnly(),
                transport);
            return new ProviderHandle<OpenAiChatCompletionsLlmProvider>(
                provider,
                transport);
        }

        private static ReachyLlmGenerationRequest SingleUserMessageRequest(
            string userText)
        {
            return new ReachyLlmGenerationRequest(
                "rma190-llm-failure-request",
                new[] { new ReachyLlmChatMessage(ReachyLlmChatRole.User, userText) });
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 LLM failure-mode contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 LLM failure-mode contract failed for {description}: expected true.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 LLM failure-mode contract failed for {description}: expected false.");
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
            private readonly Func<
                HttpRequestMessage,
                CancellationToken,
                HttpResponseMessage> respond;

            public FakeHandler(
                Func<
                    HttpRequestMessage,
                    CancellationToken,
                    HttpResponseMessage> respond)
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
    }
}

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
    internal static class Rma190NetworkProviderFailureScenarioTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            NetworkLossSurfacesAsRetryableNetworkFailure().GetAwaiter().GetResult();
            RateLimitSurfacesAsExplicitQuotaFailure().GetAwaiter().GetResult();
            MalformedStreamingResponseFailsClosed().GetAwaiter().GetResult();
            CancellationBeforeTransportStartDoesNotReachProvider().GetAwaiter().GetResult();
            FailureModesDoNotAuthorizeSilentProviderSwitch();
        }

        private static async Task NetworkLossSurfacesAsRetryableNetworkFailure()
        {
            using var handler = new FakeHandler((_, _) =>
                throw new HttpRequestException("synthetic RMA-190 network loss"));
            using ReachySharedHttpTransport transport = CreateTransport(handler);
            using ReachyHttpTransportRequest request = BufferedRequest();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                eventSink: null,
                CancellationToken.None);

            False(result.Succeeded, "network loss did not succeed");
            Equal(ReachyHttpErrorCategory.Network, RequiredError(result).Category, "network loss category");
            Equal(ReachyHttpTimeoutPhase.ConnectionOrHeaders, RequiredError(result).TimeoutPhase, "network loss phase");
            True(RequiredError(result).Retryable, "network loss retryable diagnostic");
            Equal(1, result.Attempts, "network loss attempt count");
            Equal(1, handler.CallCount, "network loss provider call count");
        }

        private static async Task RateLimitSurfacesAsExplicitQuotaFailure()
        {
            using var handler = new FakeHandler((_, _) =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
                };
                response.Headers.TryAddWithoutValidation("retry-after", "1");
                response.Headers.TryAddWithoutValidation("x-request-id", "rma190-rate-limit");
                return response;
            });
            using ReachySharedHttpTransport transport = CreateTransport(handler);
            using ReachyHttpTransportRequest request = BufferedRequest();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                eventSink: null,
                CancellationToken.None);

            False(result.Succeeded, "rate limit did not succeed");
            ReachyHttpTransportError error = RequiredError(result);
            Equal(ReachyHttpErrorCategory.QuotaOrRateLimited, error.Category, "rate limit category");
            Equal(429, error.HttpStatusCode, "rate limit status code");
            Equal("rma190-rate-limit", error.ProviderRequestId, "rate limit request id");
            True(error.Retryable, "rate limit retryable diagnostic");
            Equal(1, result.Attempts, "rate limit attempt count");
        }

        private static async Task MalformedStreamingResponseFailsClosed()
        {
            using var handler = new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            using ReachySharedHttpTransport transport = CreateTransport(handler);
            using ReachyHttpTransportRequest request = StreamingRequest();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                new NoopEventSink(),
                CancellationToken.None);

            False(result.Succeeded, "malformed streaming response did not succeed");
            ReachyHttpTransportError error = RequiredError(result);
            Equal(ReachyHttpErrorCategory.MalformedResponse, error.Category, "malformed response category");
            Equal(200, error.HttpStatusCode, "malformed response status code");
            False(error.Retryable, "malformed response retryable diagnostic");
        }

        private static async Task CancellationBeforeTransportStartDoesNotReachProvider()
        {
            using var handler = new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            using ReachySharedHttpTransport transport = CreateTransport(handler);
            using ReachyHttpTransportRequest request = BufferedRequest();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            ReachyHttpTransportResult result = await transport.SendAsync(
                request,
                eventSink: null,
                cancelled.Token);

            False(result.Succeeded, "pre-cancelled request did not succeed");
            Equal(ReachyHttpErrorCategory.Cancelled, RequiredError(result).Category, "cancelled category");
            Equal(ReachyHttpTimeoutPhase.None, RequiredError(result).TimeoutPhase, "cancelled phase");
            False(RequiredError(result).Retryable, "cancelled retryable diagnostic");
            Equal(0, handler.CallCount, "pre-cancelled provider call count");
        }

        private static void FailureModesDoNotAuthorizeSilentProviderSwitch()
        {
            var engine = new ReachyProviderFallbackPolicyEngine();
            ReachyProviderEndpointIdentity source = Endpoint("llm-cloud-primary");
            ReachyProviderEndpointIdentity target = Endpoint("llm-cloud-backup");
            foreach (string reason in new[]
            {
                "network-loss",
                "rate-limit",
                "malformed-response",
                "cancellation",
            })
            {
                var request = new ReachyProviderSwitchRequest(source, target, reason);
                ReachyFallbackDecision decision = engine.EvaluateProviderSwitch(request);
                Equal(ReachyFallbackDecisionStatus.Denied, decision.Status, $"{reason} fallback decision");
                Equal(null, decision.Authorization, $"{reason} fallback authorization");
            }
        }

        private static ReachySharedHttpTransport CreateTransport(FakeHandler handler)
        {
            return new ReachySharedHttpTransport(
                Profile(),
                secretStore: null,
                new ReachyHttpTransportPolicy(
                    connectionTimeoutMilliseconds: 1000,
                    readTimeoutMilliseconds: 1000,
                    maximumAttempts: 1,
                    baseBackoffMilliseconds: 0,
                    maximumBackoffMilliseconds: 0),
                new ImmediateBackoffDelay(),
                handler);
        }

        private static ReachyProviderProfile Profile()
        {
            return new ReachyProviderProfile(
                "rma190-network-failure",
                "RMA-190 Network Failure Test Provider",
                new Uri("https://provider.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[] { new ReachyProviderModelBinding(ReachyProviderModelRole.Text, "gpt-test") },
                Array.Empty<ReachyProviderHeaderBinding>(),
                1000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: null);
        }

        private static ReachyHttpTransportRequest BufferedRequest()
        {
            return Request(ReachyHttpResponseMode.Buffered, accept: "application/json");
        }

        private static ReachyHttpTransportRequest StreamingRequest()
        {
            return Request(ReachyHttpResponseMode.ServerSentEvents, accept: "text/event-stream");
        }

        private static ReachyHttpTransportRequest Request(
            ReachyHttpResponseMode mode,
            string accept)
        {
            return new ReachyHttpTransportRequest(
                HttpMethod.Post,
                "v1/chat/completions",
                Encoding.UTF8.GetBytes("{}"),
                "application/json",
                accept,
                mode,
                maximumResponseBytes: 1024,
                maximumSseEventCharacters: 1024,
                explicitlyAuthorizeNonIdempotentRetry: false,
                idempotencyKey: null);
        }

        private static ReachyProviderEndpointIdentity Endpoint(string providerId)
        {
            return new ReachyProviderEndpointIdentity(
                ReachyProviderWorkloadKind.Llm,
                providerId,
                ReachyProviderPrivacyBoundary.Cloud);
        }

        private static ReachyHttpTransportError RequiredError(ReachyHttpTransportResult result)
        {
            return result.Error ??
                throw new InvalidOperationException("RMA-190 network failure scenario omitted structured error details.");
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 network failure scenario failed for {label}: expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string label)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 network failure scenario failed for {label}: expected true.");
            }
        }

        private static void False(bool condition, string label)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 network failure scenario failed for {label}: expected false.");
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

        private sealed class NoopEventSink : IReachyServerSentEventSink
        {
            public ValueTask OnEventAsync(
                ReachyServerSentEvent serverSentEvent,
                CancellationToken cancellationToken)
            {
                return default;
            }
        }

        private sealed class ImmediateBackoffDelay : IReachyHttpBackoffDelay
        {
            public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                return default;
            }
        }
    }
}

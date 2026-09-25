#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;
using ReachyMini.Providers;

namespace ReachyMini.Core.Tests
{
    internal static class Rma190NetworkFailureEndToEndTests
    {
        internal static void RunAll()
        {
            NetworkLossFailsClosedAsUnavailable().GetAwaiter().GetResult();
            RateLimitFailsClosedAsRateLimited().GetAwaiter().GetResult();
            MalformedResponseFailsClosedAsProtocolFailure().GetAwaiter().GetResult();
            CancellationFailsClosedAsCancelled().GetAwaiter().GetResult();
        }

        private static async Task NetworkLossFailsClosedAsUnavailable()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                throw new HttpRequestException("Synthetic RMA-190 network loss."));
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                CancellationToken.None);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.Unavailable,
                OpenAiVisionProviderErrorCategory.Transport,
                requiresTransportReset: true,
                "network loss");
        }

        private static async Task RateLimitFailsClosedAsRateLimited()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent(
                        "{\"error\":\"rate limited\"}",
                        Encoding.UTF8,
                        "application/json"),
                });
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                CancellationToken.None);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.RateLimited,
                OpenAiVisionProviderErrorCategory.RateLimit,
                requiresTransportReset: false,
                "rate limit");
        }

        private static async Task MalformedResponseFailsClosedAsProtocolFailure()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                JsonResponse(HttpStatusCode.OK, "not-json"));
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                CancellationToken.None);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.ProtocolFailure,
                OpenAiVisionProviderErrorCategory.Protocol,
                requiresTransportReset: true,
                "malformed response");
        }

        private static async Task CancellationFailsClosedAsCancelled()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                JsonResponse(HttpStatusCode.OK, "{\"choices\":[]}"));
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                cancellation.Token);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.Cancelled,
                OpenAiVisionProviderErrorCategory.Transport,
                requiresTransportReset: false,
                "cancellation");
            Equal(0, handler.CallCount, "pre-cancelled request does not reach external network");
        }

        private static void AssertFailure(
            OpenAiVisionTransportResult result,
            OpenAiVisionTransportStatus expectedStatus,
            OpenAiVisionProviderErrorCategory expectedCategory,
            bool requiresTransportReset,
            string description)
        {
            False(result.Succeeded, description + " must not succeed");
            Equal(expectedStatus, result.Status, description + " status");
            Equal(null, result.Text, description + " must not fabricate response text");
            Equal(
                requiresTransportReset,
                result.RequiresTransportReset,
                description + " transport-reset contract");
            OpenAiVisionProviderError error = result.Error ??
                throw new InvalidOperationException(
                    "RMA-190 " + description + " did not surface a structured error.");
            Equal(expectedCategory, error.Category, description + " error category");
        }

        private static TransportHandle CreateTransport(FakeHandler handler)
        {
            ReachyProviderProfile profile = new ReachyProviderProfile(
                "rma190-network-test-provider",
                "RMA-190 Network Failure Provider",
                new Uri("https://vlm.example.test/"),
                ReachyProviderEndpointStyle.ChatCompletions,
                new[]
                {
                    new ReachyProviderModelBinding(
                        ReachyProviderModelRole.Vision,
                        "gpt-vision-test"),
                },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "vlm-cred");
            var secretStore = new FakeSecretStore();
            secretStore.Put(
                "vlm-cred",
                Encoding.ASCII.GetBytes("test-secret-value"));
            ReachyBearerCredentialTransportBinding bearer =
                ReachyBearerCredentialTransportBinding.Create(
                    profile,
                    secretStore,
                    "reachy.internal.rma190.vlm.bearer");
            ReachySharedHttpTransport sharedTransport =
                new ReachySharedHttpTransport(
                    bearer.Profile,
                    bearer.SecretStore,
                    policy: null,
                    backoffDelay: new ImmediateBackoffDelay(),
                    handler: handler);
            var transport = new ReachyOpenAiVisionHttpTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                profile,
                secretStore,
                "v1/chat/completions",
                1024 * 1024,
                4096,
                sharedTransport);
            return new TransportHandle(transport, sharedTransport);
        }

        private static OpenAiVisionTransportRequest BuildRequest(
            RemoteVlmEncodedImage image)
        {
            return new OpenAiVisionTransportRequest(
                OpenAiVisionEndpointStyle.ChatCompletions,
                "rma190-network-request",
                "gpt-vision-test",
                "Analyze only the supplied transformed Reachy-eye image.",
                "What is visible?",
                image,
                RemoteVlmImageDetail.Auto,
                128);
        }

        private static RemoteVlmEncodedImage BuildEncodedImage()
        {
            var identity = new ReachyVisionFrameIdentity(
                "camera-rma190",
                sourceSessionId: 1UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: 1L,
                authoritativeSequence: 1UL,
                continuityId: 1U);
            byte[] fixtureBytes = { 0xFF, 0xD8, 0xFF, 0xD9 };
            return new RemoteVlmEncodedImage(
                identity,
                VisionFrameOrigin.TransformedReachyEye,
                sourceWidth: 64,
                sourceHeight: 64,
                width: 64,
                height: 64,
                RemoteVlmImageFormat.Jpeg,
                RemoteVlmInvalidPixelPolicy.ReplaceWithOpaqueBlack,
                validityMaskApplied: true,
                containsOnlyValidPixels: true,
                upscaled: false,
                fixtureBytes);
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

        private static void Equal<T>(
            T expected,
            T actual,
            string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    "RMA-190 network-failure acceptance failed for " +
                    description + ": expected=" + expected +
                    "; actual=" + actual + ".");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    "RMA-190 network-failure acceptance failed for " +
                    description + ": expected false.");
            }
        }

        private readonly struct TransportHandle : IAsyncDisposable
        {
            private readonly ReachySharedHttpTransport sharedTransport;

            public TransportHandle(
                ReachyOpenAiVisionHttpTransport transport,
                ReachySharedHttpTransport sharedTransport)
            {
                Transport = transport;
                this.sharedTransport = sharedTransport;
            }

            public ReachyOpenAiVisionHttpTransport Transport { get; }

            public ValueTask DisposeAsync()
            {
                Transport.Dispose();
                sharedTransport.Dispose();
                return default;
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
                this.respond = respond ??
                    throw new ArgumentNullException(nameof(respond));
            }

            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                CallCount = checked(CallCount + 1);
                HttpResponseMessage response = respond(
                    request,
                    cancellationToken);
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

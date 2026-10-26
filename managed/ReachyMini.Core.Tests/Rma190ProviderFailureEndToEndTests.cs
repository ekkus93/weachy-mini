#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;
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
            MalformedResponseFailsClosedWithoutRetry().GetAwaiter().GetResult();
            InFlightCancellationFailsClosedWithoutRetry().GetAwaiter().GetResult();
        }

        private static async Task NetworkLossFailsClosedWithoutRetry()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                throw new HttpRequestException("simulated network loss"));
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                CancellationToken.None);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.Unavailable,
                OpenAiVisionProviderErrorCategory.Transport,
                expectedHttpStatusCode: null,
                requiresTransportReset: true,
                "network loss");
            Equal(1, handler.CallCount, "network loss attempt count");
        }

        private static async Task RateLimitFailsClosedWithoutRetry()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                TextResponse(HttpStatusCode.TooManyRequests, "rate limited"));
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                CancellationToken.None);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.RateLimited,
                OpenAiVisionProviderErrorCategory.RateLimit,
                (int)HttpStatusCode.TooManyRequests,
                requiresTransportReset: false,
                "rate limit");
            Equal(1, handler.CallCount, "rate-limit attempt count");
        }

        private static async Task MalformedResponseFailsClosedWithoutRetry()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                JsonResponse(HttpStatusCode.OK, "not json"));
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                CancellationToken.None);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.ProtocolFailure,
                OpenAiVisionProviderErrorCategory.Protocol,
                (int)HttpStatusCode.OK,
                requiresTransportReset: true,
                "malformed response");
            Equal(1, handler.CallCount, "malformed-response attempt count");
        }

        private static async Task InFlightCancellationFailsClosedWithoutRetry()
        {
            using var callerCancellation = new CancellationTokenSource();
            using FakeHandler handler = new FakeHandler((_, effectiveCancellation) =>
            {
                callerCancellation.Cancel();
                effectiveCancellation.ThrowIfCancellationRequested();
                return JsonResponse(HttpStatusCode.OK, "{}");
            });
            await using TransportHandle handle = CreateTransport(handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(image),
                callerCancellation.Token);

            AssertFailure(
                result,
                OpenAiVisionTransportStatus.Cancelled,
                OpenAiVisionProviderErrorCategory.Transport,
                expectedHttpStatusCode: null,
                requiresTransportReset: false,
                "in-flight cancellation");
            Equal(1, handler.CallCount, "cancellation attempt count");
        }

        private static void AssertFailure(
            OpenAiVisionTransportResult result,
            OpenAiVisionTransportStatus expectedStatus,
            OpenAiVisionProviderErrorCategory expectedCategory,
            int? expectedHttpStatusCode,
            bool requiresTransportReset,
            string description)
        {
            False(result.Succeeded, $"{description} must not succeed");
            Equal(expectedStatus, result.Status, $"{description} status");
            True(result.Error != null, $"{description} structured error");
            Equal(
                expectedCategory,
                result.Error!.Category,
                $"{description} error category");
            Equal(
                expectedHttpStatusCode,
                result.Error.HttpStatusCode,
                $"{description} HTTP status");
            Equal(
                requiresTransportReset,
                result.RequiresTransportReset,
                $"{description} transport-reset requirement");
            Equal<string?>(null, result.Text, $"{description} fabricated response text");
        }

        private static TransportHandle CreateTransport(FakeHandler handler)
        {
            var profile = new ReachyProviderProfile(
                "rma190-vlm-failure-provider",
                "RMA-190 VLM Failure Provider",
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
                    "reachy.internal.vlm.bearer.rma190");
            var sharedTransport = new ReachySharedHttpTransport(
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
                "rma190-provider-failure-request",
                "gpt-vision-test",
                "Analyze only the supplied image.",
                "what is in this scene?",
                image,
                RemoteVlmImageDetail.Auto,
                256);
        }

        private static RemoteVlmEncodedImage BuildEncodedImage()
        {
            var identity = new ReachyVisionFrameIdentity(
                "camera-0",
                sourceSessionId: 1UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: 1L,
                authoritativeSequence: 1UL,
                continuityId: 1U);
            byte[] fakeJpegBytes = { 0xFF, 0xD8, 0xFF, 0xD9 };
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
                fakeJpegBytes);
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
                Content = new StringContent(body, Encoding.UTF8, "text/plain"),
            };
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider-failure acceptance failed for {description}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider-failure acceptance failed for {description}: " +
                    "expected true.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 provider-failure acceptance failed for {description}: " +
                    "expected false.");
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
                Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public int CallCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                ++CallCount;
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

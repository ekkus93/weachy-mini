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
    // RMA-195 VLM half: exercises ReachyOpenAiVisionHttpTransport (the first real
    // production IOpenAiVisionTransport implementation -- previously only test
    // fakes existed anywhere in the repo, confirmed by a dedicated investigation
    // before writing any code) against a fake HttpMessageHandler through
    // ReachySharedHttpTransport's real request-building/auth/response-parsing
    // pipeline, mirroring Rma142Rma143OpenAiLlmAdapterContractTests' mock-server
    // contract style exactly.
    internal static class Rma195VlmTransportContractTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            ChatCompletionsTransportExtractsAssistantText().GetAwaiter().GetResult();
            ResponsesTransportExtractsFlattenedOutputText().GetAwaiter().GetResult();
            RequestBodyCarriesBase64ImageAndPrompt().GetAwaiter().GetResult();
            AuthorizationHeaderCarriesBearerCredential().GetAwaiter().GetResult();
            HttpUnauthorizedSurfacesAsUnauthorizedStatus().GetAwaiter().GetResult();
            MalformedJsonSurfacesAsProtocolFailureNotThrown().GetAwaiter().GetResult();
            CancellationBeforeSendSurfacesAsCancelled().GetAwaiter().GetResult();
        }

        private static async Task ChatCompletionsTransportExtractsAssistantText()
        {
            string body = "{\"choices\":[{\"message\":{\"content\":\"A red mug on a table.\"}}]}";
            using FakeHandler handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, body));

            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();
            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.ChatCompletions, image),
                CancellationToken.None);

            True(result.Succeeded, "chat-completions succeeded");
            Equal("A red mug on a table.", result.Text, "chat-completions assistant text");
            True(
                handler.LastRequest != null &&
                    handler.LastRequest.RequestUri!.AbsolutePath.EndsWith(
                        "/v1/chat/completions",
                        StringComparison.Ordinal),
                "chat-completions request path");
        }

        private static async Task ResponsesTransportExtractsFlattenedOutputText()
        {
            string body = "{\"output_text\":\"A red mug on a table.\"}";
            using FakeHandler handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, body));

            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.Responses,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();
            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.Responses, image),
                CancellationToken.None);

            True(result.Succeeded, "responses succeeded");
            Equal("A red mug on a table.", result.Text, "responses assistant text");
            True(
                handler.LastRequest != null &&
                    handler.LastRequest.RequestUri!.AbsolutePath.EndsWith(
                        "/v1/responses",
                        StringComparison.Ordinal),
                "responses request path");
        }

        private static async Task RequestBodyCarriesBase64ImageAndPrompt()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                JsonResponse(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();
            string expectedBase64 = Convert.ToBase64String(image.EncodedBytes.Span);

            _ = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.ChatCompletions, image),
                CancellationToken.None);

            string sentBody = handler.LastRequestBody ??
                throw new InvalidOperationException("Fake handler did not capture a request body.");
            True(
                sentBody.Contains("data:image/jpeg;base64," + expectedBase64, StringComparison.Ordinal),
                "request body carries base64 image data URI");
            True(
                sentBody.Contains("what is in this scene", StringComparison.Ordinal),
                "request body carries the user prompt");
        }

        private static async Task AuthorizationHeaderCarriesBearerCredential()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                JsonResponse(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            _ = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.ChatCompletions, image),
                CancellationToken.None);

            IEnumerable<string>? values = null;
            bool hasHeader = handler.LastRequest?.Headers.TryGetValues("Authorization", out values) == true;
            True(hasHeader, "authorization header present");
            string authorizationValue = new List<string>(values!)[0];
            Equal("Bearer test-secret-value", authorizationValue, "authorization header value");
        }

        private static async Task HttpUnauthorizedSurfacesAsUnauthorizedStatus()
        {
            using FakeHandler handler = new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("denied", Encoding.UTF8, "text/plain"),
                });
            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.ChatCompletions, image),
                CancellationToken.None);

            False(result.Succeeded, "unauthorized not succeeded");
            Equal(OpenAiVisionTransportStatus.Unauthorized, result.Status, "unauthorized status");
            True(result.Error != null, "unauthorized carries a structured error");
        }

        private static async Task MalformedJsonSurfacesAsProtocolFailureNotThrown()
        {
            using FakeHandler handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, "not json"));
            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.ChatCompletions, image),
                CancellationToken.None);

            False(result.Succeeded, "malformed json not succeeded");
            Equal(OpenAiVisionTransportStatus.ProtocolFailure, result.Status, "malformed json status");
        }

        private static async Task CancellationBeforeSendSurfacesAsCancelled()
        {
            using FakeHandler handler = new FakeHandler((_, _) => JsonResponse(HttpStatusCode.OK, "{}"));
            await using TransportHandle handle = CreateTransport(
                OpenAiVisionEndpointStyle.ChatCompletions,
                handler);
            using RemoteVlmEncodedImage image = BuildEncodedImage();
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            OpenAiVisionTransportResult result = await handle.Transport.SendAsync(
                BuildRequest(OpenAiVisionEndpointStyle.ChatCompletions, image),
                cancelled.Token);

            Equal(OpenAiVisionTransportStatus.Cancelled, result.Status, "pre-cancelled status");
        }

        private static TransportHandle CreateTransport(
            OpenAiVisionEndpointStyle endpointStyle,
            FakeHandler handler)
        {
            ReachyProviderEndpointStyle profileStyle = endpointStyle == OpenAiVisionEndpointStyle.Responses
                ? ReachyProviderEndpointStyle.Responses
                : ReachyProviderEndpointStyle.ChatCompletions;
            ReachyProviderProfile profile = new ReachyProviderProfile(
                "rma195-vlm-test-provider",
                "RMA-195 VLM Test Provider",
                new Uri("https://vlm.example.test/"),
                profileStyle,
                new[] { new ReachyProviderModelBinding(ReachyProviderModelRole.Vision, "gpt-vision-test") },
                Array.Empty<ReachyProviderHeaderBinding>(),
                30000,
                streamingEnabled: false,
                ReachyProviderTlsMode.RequireHttps,
                credentialReference: "vlm-cred");
            FakeSecretStore secretStore = new FakeSecretStore();
            secretStore.Put("vlm-cred", Encoding.ASCII.GetBytes("test-secret-value"));
            // The transport's own production constructor applies this bearer-credential
            // transformation internally when it builds its own ReachySharedHttpTransport;
            // supplying a transportOverride bypasses that constructor path entirely, so
            // the test must reproduce it here to exercise the real Authorization header.
            ReachyBearerCredentialTransportBinding bearer = ReachyBearerCredentialTransportBinding.Create(
                profile,
                secretStore,
                "reachy.internal.vlm.bearer.test");
            ReachySharedHttpTransport sharedTransport = new ReachySharedHttpTransport(
                bearer.Profile,
                bearer.SecretStore,
                policy: null,
                backoffDelay: new ImmediateBackoffDelay(),
                handler: handler);
            string relativePath = endpointStyle == OpenAiVisionEndpointStyle.Responses
                ? "v1/responses"
                : "v1/chat/completions";
            ReachyOpenAiVisionHttpTransport transport = new ReachyOpenAiVisionHttpTransport(
                endpointStyle,
                profile,
                secretStore,
                relativePath,
                1024 * 1024,
                4096,
                sharedTransport);
            return new TransportHandle(transport, sharedTransport);
        }

        private static OpenAiVisionTransportRequest BuildRequest(
            OpenAiVisionEndpointStyle endpointStyle,
            RemoteVlmEncodedImage image)
        {
            return new OpenAiVisionTransportRequest(
                endpointStyle,
                "req-1",
                "gpt-vision-test",
                "Analyze only the supplied image.",
                "what is in this scene?",
                image,
                RemoteVlmImageDetail.Auto,
                256);
        }

        private static RemoteVlmEncodedImage BuildEncodedImage()
        {
            ReachyVisionFrameIdentity identity = new ReachyVisionFrameIdentity(
                "camera-0",
                sourceSessionId: 1UL,
                sourceSequence: 1UL,
                sourceTimestampNanoseconds: 1L,
                authoritativeSequence: 1UL,
                continuityId: 1U);
            // Not a real JPEG -- the transport treats the encoded bytes as an opaque
            // payload it base64-encodes, so any non-empty byte content exercises the
            // transport's own contract; only a real encoder needs real JPEG bytes.
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
                    $"RMA-195 VLM transport contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-195 VLM transport contract failed for {description}: expected true.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-195 VLM transport contract failed for {description}: expected false.");
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
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond;

            public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public HttpRequestMessage? LastRequest { get; private set; }

            public string? LastRequestBody { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastRequestBody = request.Content == null
                    ? null
                    : request.Content.ReadAsStringAsync(CancellationToken.None)
                        .GetAwaiter().GetResult();
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

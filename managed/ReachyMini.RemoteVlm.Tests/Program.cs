#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private const int ExpectedCaseCount = 60;
        private static int caseCount;

        private static async Task<int> Main()
        {
            Run(PolicyRejectsUpscaling);
            Run(PolicyRejectsInvalidDimensions);
            Run(PolicyRejectsInvalidEncodedLimit);
            Run(PolicyRejectsInvalidQuality);
            Run(PolicyComputesBoundedLandscapeDimensions);
            Run(PolicyComputesBoundedPortraitDimensions);
            Run(PolicyDoesNotUpscaleSmallImages);
            Run(ConfigurationRequiresNetworkLocation);
            Run(ConfigurationRequiresSemanticCapability);
            Run(ConfigurationKeepsModelIdConfigurable);
            Run(ConfigurationPublishesExactCapabilities);
            Run(ResponsesProviderRequiresResponsesConfiguration);
            Run(ResponsesProviderRequiresResponsesTransport);
            Run(ChatProviderRequiresChatConfiguration);
            Run(ChatProviderRequiresChatTransport);
            await RunAsync(EncodingRequestRequiresEligibleTransformedFrame).ConfigureAwait(false);
            Run(EncodedImageCopiesInputBytes);
            Run(EncodedImageRequiresTransformedOrigin);
            Run(EncodedImageRequiresValidityApplication);
            Run(EncodedImageRejectsUpscaling);
            Run(EncodedImageDisposalZeroesPayload);
            await RunAsync(ProviderRejectsRawFrameBeforeEncoder).ConfigureAwait(false);
            await RunAsync(ProviderRejectsUnusableCoverageBeforeEncoder).ConfigureAwait(false);
            await RunAsync(ProviderRequiresNetworkDisclosure).ConfigureAwait(false);
            await RunAsync(ProviderHonorsPreCancellation).ConfigureAwait(false);
            await RunAsync(ProviderRejectsOverlongPrompt).ConfigureAwait(false);
            await RunAsync(ResponsesProviderSendsResponsesStyle).ConfigureAwait(false);
            await RunAsync(ChatProviderSendsChatStyle).ConfigureAwait(false);
            await RunAsync(RequestDisablesStorageAndStreaming).ConfigureAwait(false);
            await RunAsync(RequestUsesConfiguredModelAndOutputLimit).ConfigureAwait(false);
            await RunAsync(RequestContainsOnlyEncodedTransformedImage).ConfigureAwait(false);
            await RunAsync(DegradedCoverageContextStatesValidFraction).ConfigureAwait(false);
            await RunAsync(NormalCoverageContextDoesNotClaimDegradation).ConfigureAwait(false);
            await RunAsync(ContextExcludesWorldModelHistory).ConfigureAwait(false);
            await RunAsync(EncoderInvalidFrameMapsInvalidFrame).ConfigureAwait(false);
            await RunAsync(EncoderUnsupportedMapsUnavailable).ConfigureAwait(false);
            await RunAsync(EncoderCancellationMapsCancelled).ConfigureAwait(false);
            await RunAsync(EncoderFailurePreservesSafeCode).ConfigureAwait(false);
            await RunAsync(EncodedImageIdentityMismatchRejectedBeforeTransport).ConfigureAwait(false);
            await RunAsync(EncodedImagePolicyOverflowRejectedBeforeTransport).ConfigureAwait(false);
            await RunAsync(EncodedImagePolicyMismatchRejectedBeforeTransport).ConfigureAwait(false);
            await RunAsync(TransportCancellationMapsCancelled).ConfigureAwait(false);
            await RunAsync(TransportTimeoutMapsTimedOut).ConfigureAwait(false);
            await RunAsync(TransportUnavailableMapsUnavailable).ConfigureAwait(false);
            await RunAsync(TransportFailurePreservesSafeDetail).ConfigureAwait(false);
            Run(TransportSecretDetailIsRedacted);
            Run(TransportLongOpaqueDetailIsRedacted);
            await RunAsync(TransportSuccessReturnsValidatedText).ConfigureAwait(false);
            await RunAsync(OversizedTransportTextRejected).ConfigureAwait(false);
            Run(StructuredResultRejectsSuccessWithoutText);
            Run(StructuredFailureRequiresError);
            await RunAsync(ProviderDoesNotRetryOrFallback).ConfigureAwait(false);
            await RunAsync(ProviderConcurrencyLimitIsVisible).ConfigureAwait(false);
            await RunAsync(ProviderDisposalIsIdempotent).ConfigureAwait(false);
            await RunAsync(DisposedProviderRejectsInvocation).ConfigureAwait(false);
            await RunAsync(ExecutorCancellationRemainsCancellable).ConfigureAwait(false);
            await RunAsync(ProviderDoesNotDisposeInputFrame).ConfigureAwait(false);
            await RunAsync(ProviderDisposesEncodedPayloadAfterSuccess).ConfigureAwait(false);
            await RunAsync(ProviderExceptionDoesNotExposeMessage).ConfigureAwait(false);
            Run(SourceAndDocumentationDeclareFailClosedBoundary);

            Equal(ExpectedCaseCount, caseCount, "contract case count");
            Console.WriteLine(
                "RMA-115 OpenAI-compatible VLM adapter contracts passed: " +
                caseCount + ".");
            return 0;
        }

        private static void ConfigurationRequiresNetworkLocation()
        {
            Throws<ArgumentException>(
                () => Configuration(location: VisionProviderLocation.OnDevice),
                "on-device remote configuration");
        }

        private static void ConfigurationRequiresSemanticCapability()
        {
            Throws<ArgumentException>(
                () => Configuration(
                    supportsVisualQuestions: false,
                    supportsSceneDescription: false),
                "missing semantic capability");
        }

        private static void ConfigurationKeepsModelIdConfigurable()
        {
            OpenAiVisionProviderConfiguration configuration =
                Configuration(modelId: "user-selected-model");
            Equal("user-selected-model", configuration.ModelId, "model id");
        }

        private static void ConfigurationPublishesExactCapabilities()
        {
            OpenAiVisionProviderConfiguration configuration = Configuration(
                maximumConcurrentOperations: 2,
                maximumPromptCharacters: 1234);
            ProviderDescriptor descriptor = configuration.CreateDescriptor();
            VisionLanguageCapabilities capabilities =
                configuration.CreateCapabilities();
            Equal(
                VisionProviderKind.SemanticVisionLanguage,
                descriptor.Kind,
                "descriptor kind");
            Equal(VisionProviderLocation.Cloud, descriptor.Location, "location");
            True(descriptor.RequiresNetworkDisclosure, "network disclosure");
            True(capabilities.SupportsVisualQuestions, "visual questions");
            True(capabilities.SupportsSceneDescription, "scene description");
            True(capabilities.SupportsCancellation, "cancellation");
            Equal(2, capabilities.MaximumConcurrentOperations, "concurrency");
            Equal(1234, capabilities.MaximumPromptCharacters, "prompt bound");
        }

        private static void ResponsesProviderRequiresResponsesConfiguration()
        {
            Throws<ArgumentException>(
                () => new OpenAiResponsesVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                    new FakeEncoder()),
                "responses configuration mismatch");
        }

        private static void ResponsesProviderRequiresResponsesTransport()
        {
            Throws<ArgumentException>(
                () => new OpenAiResponsesVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.Responses),
                    new FakeTransport(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeEncoder()),
                "responses transport mismatch");
        }

        private static void ChatProviderRequiresChatConfiguration()
        {
            Throws<ArgumentException>(
                () => new OpenAiChatCompletionsVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.Responses),
                    new FakeTransport(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeEncoder()),
                "chat configuration mismatch");
        }

        private static void ChatProviderRequiresChatTransport()
        {
            Throws<ArgumentException>(
                () => new OpenAiChatCompletionsVisionLanguageProvider(
                    Configuration(OpenAiVisionEndpointStyle.ChatCompletions),
                    new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                    new FakeEncoder()),
                "chat transport mismatch");
        }

        private static async Task EncodingRequestRequiresEligibleTransformedFrame()
        {
            await using ReachyVisionFrame raw = RawFrame();
            Throws<ArgumentException>(
                () => new RemoteVlmImageEncodingRequest(raw, Policy()),
                "raw encoding request");
        }

        private static void EncodedImageCopiesInputBytes()
        {
            byte[] bytes = { 1, 2, 3 };
            using RemoteVlmEncodedImage image = EncodedImage(
                Identity(),
                bytes: bytes);
            bytes[0] = 99;
            Equal((byte)1, image.EncodedBytes.Span[0], "defensive byte copy");
        }

        private static void EncodedImageRequiresTransformedOrigin()
        {
            Throws<ArgumentException>(
                () => EncodedImage(
                    Identity(),
                    sourceOrigin: VisionFrameOrigin.RawPhoneDebug),
                "raw encoded image");
        }

        private static void EncodedImageRequiresValidityApplication()
        {
            Throws<ArgumentException>(
                () => EncodedImage(
                    Identity(),
                    validityMaskApplied: false),
                "unmasked encoded image");
            Throws<ArgumentException>(
                () => EncodedImage(
                    Identity(),
                    containsOnlyValidPixels: false),
                "invalid pixel leakage");
        }

        private static void EncodedImageRejectsUpscaling()
        {
            Throws<ArgumentException>(
                () => EncodedImage(Identity(), upscaled: true),
                "upscaled payload");
            Throws<ArgumentOutOfRangeException>(
                () => EncodedImage(
                    Identity(),
                    sourceWidth: 10,
                    width: 11),
                "oversized encoded width");
        }

        private static void EncodedImageDisposalZeroesPayload()
        {
            var image = EncodedImage(Identity(), bytes: new byte[] { 7, 8, 9 });
            FieldInfo field = typeof(RemoteVlmEncodedImage).GetField(
                "encodedBytes",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("encodedBytes field not found.");
            byte[] privateBytes = (byte[])(field.GetValue(image) ??
                throw new InvalidOperationException("encodedBytes value missing."));
            image.Dispose();
            True(image.IsDisposed, "image disposed");
            True(privateBytes.All(value => value == 0), "payload zeroized");
            Throws<ObjectDisposedException>(
                () => image.EncodedBytes,
                "disposed payload access");
        }

        private static async Task ProviderRejectsRawFrameBeforeEncoder()
        {
            var encoder = new FakeEncoder();
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = RawFrame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(VisionOperationStatus.InvalidFrame, result.Status, "raw status");
            Equal(0, encoder.CallCount, "raw encoder calls");
            Equal(0, transport.CallCount, "raw transport calls");
        }

        private static async Task ProviderRejectsUnusableCoverageBeforeEncoder()
        {
            var encoder = new FakeEncoder();
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame(
                VisionCoverageState.Unusable,
                validPixelCount: 0,
                totalPixelCount: 100,
                shouldStopVisionDrivenTurning: true);
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.InvalidFrame,
                result.Status,
                "unusable coverage status");
            Equal(0, encoder.CallCount, "unusable encoder calls");
        }

        private static async Task ProviderRequiresNetworkDisclosure()
        {
            var encoder = new FakeEncoder();
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame, networkAcknowledged: false),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.Unavailable,
                result.Status,
                "disclosure status");
            Equal(0, encoder.CallCount, "disclosure encoder calls");
        }

        private static async Task ProviderHonorsPreCancellation()
        {
            var encoder = new FakeEncoder();
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                cancellation.Token).ConfigureAwait(false);
            Equal(VisionOperationStatus.Cancelled, result.Status, "cancel status");
            Equal(0, encoder.CallCount, "cancel encoder calls");
        }

        private static async Task ProviderRejectsOverlongPrompt()
        {
            var encoder = new FakeEncoder();
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder,
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    maximumPromptCharacters: 4));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame, prompt: "12345"),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "overlong prompt status");
            Equal(0, encoder.CallCount, "overlong encoder calls");
        }

        private static async Task ResponsesProviderSendsResponsesStyle()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.Responses,
                transport).ConfigureAwait(false);
            Equal(
                OpenAiVisionEndpointStyle.Responses,
                transport.LastRequest?.EndpointStyle ??
                    throw new InvalidOperationException("missing request"),
                "responses request style");
        }

        private static async Task ChatProviderSendsChatStyle()
        {
            var transport = new FakeTransport(
                OpenAiVisionEndpointStyle.ChatCompletions);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.ChatCompletions,
                transport).ConfigureAwait(false);
            Equal(
                OpenAiVisionEndpointStyle.ChatCompletions,
                transport.LastRequest?.EndpointStyle ??
                    throw new InvalidOperationException("missing request"),
                "chat request style");
        }

        private static async Task RequestDisablesStorageAndStreaming()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.Responses,
                transport).ConfigureAwait(false);
            OpenAiVisionTransportRequest request = transport.LastRequest ??
                throw new InvalidOperationException("missing request");
            False(request.StoreResponse, "response storage");
            False(request.Stream, "streaming");
            False(RemoteVlmReleasePolicy.ResponseStorageEnabled, "storage policy");
            False(RemoteVlmReleasePolicy.StreamingEnabled, "stream policy");
        }

        private static async Task RequestUsesConfiguredModelAndOutputLimit()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            OpenAiVisionProviderConfiguration configuration = Configuration(
                OpenAiVisionEndpointStyle.Responses,
                modelId: "configured-vision-model",
                maximumOutputTokens: 321);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                configuration).ConfigureAwait(false);
            OpenAiVisionTransportRequest request = transport.LastRequest ??
                throw new InvalidOperationException("missing request");
            Equal("configured-vision-model", request.ModelId, "transport model");
            Equal(321, request.MaximumOutputTokens, "transport output tokens");
        }

        private static async Task RequestContainsOnlyEncodedTransformedImage()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.Responses,
                transport).ConfigureAwait(false);
            OpenAiVisionTransportRequest request = transport.LastRequest ??
                throw new InvalidOperationException("missing request");
            Equal(
                VisionFrameOrigin.TransformedReachyEye,
                request.Image.SourceOrigin,
                "encoded source origin");
            True(request.Image.ValidityMaskApplied, "validity applied");
            True(request.Image.ContainsOnlyValidPixels, "valid-only payload");
            Equal("image/jpeg", request.Image.MediaType, "media type");
        }

        private static async Task DegradedCoverageContextStatesValidFraction()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame(
                VisionCoverageState.Degraded,
                validPixelCount: 75,
                totalPixelCount: 100);
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            True(result.Succeeded, "degraded request success");
            string context = transport.LastRequest?.SystemContext ??
                throw new InvalidOperationException("missing context");
            Contains("Coverage is degraded", context, "degraded label");
            Contains("75.0%", context, "valid fraction");
            Contains("do not infer", context, "invalid region warning");
        }

        private static async Task NormalCoverageContextDoesNotClaimDegradation()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.Responses,
                transport).ConfigureAwait(false);
            string context = transport.LastRequest?.SystemContext ??
                throw new InvalidOperationException("missing context");
            Contains("Coverage is normal", context, "normal label");
            False(
                context.Contains("Coverage is degraded", StringComparison.Ordinal),
                "false degradation");
        }

        private static async Task ContextExcludesWorldModelHistory()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await InvokeSuccessfulProvider(
                OpenAiVisionEndpointStyle.Responses,
                transport).ConfigureAwait(false);
            OpenAiVisionTransportRequest request = transport.LastRequest ??
                throw new InvalidOperationException("missing request");
            Contains(
                "No world-model entity history",
                request.SystemContext,
                "history exclusion context");
            string[] forbiddenPropertyFragments =
            {
                "Entity",
                "History",
                "Recent",
                "WorldModel",
            };
            string[] propertyNames = typeof(OpenAiVisionTransportRequest)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray();
            for (int index = 0; index < forbiddenPropertyFragments.Length; ++index)
            {
                False(
                    propertyNames.Any(name => name.Contains(
                        forbiddenPropertyFragments[index],
                        StringComparison.OrdinalIgnoreCase)),
                    "stale context property " + forbiddenPropertyFragments[index]);
            }
        }

        private static async Task EncoderInvalidFrameMapsInvalidFrame()
        {
            await AssertEncodingFailure(
                RemoteVlmImageEncodingStatus.InvalidFrame,
                VisionOperationStatus.InvalidFrame,
                "invalid_frame").ConfigureAwait(false);
        }

        private static async Task EncoderUnsupportedMapsUnavailable()
        {
            await AssertEncodingFailure(
                RemoteVlmImageEncodingStatus.Unsupported,
                VisionOperationStatus.Unavailable,
                "unsupported_format").ConfigureAwait(false);
        }

        private static async Task EncoderCancellationMapsCancelled()
        {
            await AssertEncodingFailure(
                RemoteVlmImageEncodingStatus.Cancelled,
                VisionOperationStatus.Cancelled,
                "cancelled").ConfigureAwait(false);
        }

        private static async Task EncoderFailurePreservesSafeCode()
        {
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Failure(
                            RemoteVlmImageEncodingStatus.Failed,
                            "gpu_readback_failed",
                            requiresEncoderReset: true)),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "encoder failure status");
            Contains("gpu_readback_failed", result.Diagnostic, "encoder code");
            True(result.RequiresProviderReset, "encoder reset");
            Equal(0, transport.CallCount, "encoder failure transport calls");
        }

        private static async Task EncodedImageIdentityMismatchRejectedBeforeTransport()
        {
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Success(
                            EncodedImage(Identity(sourceSequence: 99)))),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "identity mismatch status");
            Equal(0, transport.CallCount, "identity mismatch transport calls");
            True(encoder.LastImage?.IsDisposed == true, "mismatched image disposed");
        }

        private static async Task EncodedImagePolicyOverflowRejectedBeforeTransport()
        {
            RemoteVlmImagePolicy policy = Policy(maximumEncodedBytes: 4);
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Success(
                            EncodedImage(
                                request.Frame.Identity,
                                sourceWidth: request.Frame.Width,
                                sourceHeight: request.Frame.Height,
                                width: request.Frame.Width,
                                height: request.Frame.Height,
                                bytes: new byte[] { 1, 2, 3, 4, 5 }))),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder,
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    policy: policy));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "payload overflow status");
            Equal(0, transport.CallCount, "payload overflow transport calls");
        }

        private static async Task EncodedImagePolicyMismatchRejectedBeforeTransport()
        {
            RemoteVlmImagePolicy policy = Policy(
                preferredFormat: RemoteVlmImageFormat.Png);
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Success(
                            EncodedImage(
                                request.Frame.Identity,
                                sourceWidth: request.Frame.Width,
                                sourceHeight: request.Frame.Height,
                                width: request.Frame.Width,
                                height: request.Frame.Height,
                                format: RemoteVlmImageFormat.Jpeg))),
            };
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                encoder,
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    policy: policy));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "policy mismatch status");
            Equal(0, transport.CallCount, "policy mismatch transport calls");
        }

        private static async Task TransportCancellationMapsCancelled()
        {
            await AssertTransportFailure(
                OpenAiVisionTransportStatus.Cancelled,
                VisionOperationStatus.Cancelled,
                Error(OpenAiVisionProviderErrorCategory.Transport, "cancelled"))
                .ConfigureAwait(false);
        }

        private static async Task TransportTimeoutMapsTimedOut()
        {
            await AssertTransportFailure(
                OpenAiVisionTransportStatus.TimedOut,
                VisionOperationStatus.TimedOut,
                Error(OpenAiVisionProviderErrorCategory.Transport, "timeout"))
                .ConfigureAwait(false);
        }

        private static async Task TransportUnavailableMapsUnavailable()
        {
            await AssertTransportFailure(
                OpenAiVisionTransportStatus.Unavailable,
                VisionOperationStatus.Unavailable,
                Error(
                    OpenAiVisionProviderErrorCategory.UnsupportedCapability,
                    "vision_not_supported"))
                .ConfigureAwait(false);
        }

        private static async Task TransportFailurePreservesSafeDetail()
        {
            var error = new OpenAiVisionProviderError(
                OpenAiVisionProviderErrorCategory.RateLimit,
                "rate_limit_exceeded",
                429,
                "req_123",
                "Rate limit reached for this model.");
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Failure(
                            OpenAiVisionTransportStatus.RateLimited,
                            error,
                            requiresTransportReset: false)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "rate-limit status");
            Contains("rate_limit_exceeded", result.Diagnostic, "error code");
            Contains("http_status=429", result.Diagnostic, "http status");
            Contains("req_123", result.Diagnostic, "request id");
            Contains("Rate limit reached", result.Diagnostic, "safe detail");
        }

        private static void TransportSecretDetailIsRedacted()
        {
            var error = new OpenAiVisionProviderError(
                OpenAiVisionProviderErrorCategory.Authentication,
                "authentication_failed",
                401,
                "req_safe",
                "Authorization: Bearer sk-super-secret");
            True(error.DetailRedacted, "secret detail redacted");
            False(error.Detail.Contains("sk-super-secret", StringComparison.Ordinal), "secret absent");
            Contains("redacted", error.Detail, "redaction marker");
        }

        private static void TransportLongOpaqueDetailIsRedacted()
        {
            var error = new OpenAiVisionProviderError(
                OpenAiVisionProviderErrorCategory.Protocol,
                "payload_rejected",
                400,
                null,
                new string('A', 100));
            True(error.DetailRedacted, "opaque detail redacted");
        }

        private static async Task TransportSuccessReturnsValidatedText()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Success(
                            "A person is visible.",
                            "req_ok",
                            "stop",
                            100,
                            8)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            True(result.Succeeded, "transport success");
            Equal("A person is visible.", result.Text ?? string.Empty, "result text");
        }

        private static async Task OversizedTransportTextRejected()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Success(
                            "12345",
                            null,
                            null,
                            null,
                            null)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder(),
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    maximumResponseCharacters: 4));
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ContractViolation,
                result.Status,
                "oversized result status");
            True(result.RequiresProviderReset, "oversized result reset");
        }

        private static void StructuredResultRejectsSuccessWithoutText()
        {
            Throws<ArgumentException>(
                () => OpenAiVisionTransportResult.Success(
                    " ",
                    null,
                    null,
                    null,
                    null),
                "blank transport success");
        }

        private static void StructuredFailureRequiresError()
        {
            Throws<ArgumentNullException>(
                () => OpenAiVisionTransportResult.Failure(
                    OpenAiVisionTransportStatus.ServerFailure,
                    null!,
                    requiresTransportReset: true),
                "failure without error");
        }

        private static async Task ProviderDoesNotRetryOrFallback()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Failure(
                            OpenAiVisionTransportStatus.ServerFailure,
                            Error(
                                OpenAiVisionProviderErrorCategory.Server,
                                "server_failure"),
                            requiresTransportReset: true)),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "server failure status");
            Equal(1, transport.CallCount, "single transport call");
            False(RemoteVlmReleasePolicy.AutomaticRetryEnabled, "retry policy");
            False(
                RemoteVlmReleasePolicy.AutomaticProviderFallbackEnabled,
                "fallback policy");
        }

        private static async Task ProviderConcurrencyLimitIsVisible()
        {
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = async (request, token) =>
                {
                    _ = entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return OpenAiVisionTransportResult.Success(
                        "done",
                        null,
                        null,
                        null,
                        null);
                },
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder(),
                Configuration(
                    OpenAiVisionEndpointStyle.Responses,
                    maximumConcurrentOperations: 1));
            await using ReachyVisionFrame firstFrame = Frame(sourceSequence: 1);
            await using ReachyVisionFrame secondFrame = Frame(sourceSequence: 2);
            Task<VisionLanguageResult> first = provider.AnalyzeAsync(
                Request(provider, firstFrame, requestId: "first"),
                CancellationToken.None).AsTask();
            await entered.Task.ConfigureAwait(false);
            VisionLanguageResult second = await provider.AnalyzeAsync(
                Request(provider, secondFrame, requestId: "second"),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.Unavailable,
                second.Status,
                "concurrency status");
            Contains("not queued or rerouted", second.Diagnostic, "concurrency diagnostic");
            Equal(1, transport.CallCount, "concurrency transport calls");
            _ = release.TrySetResult(true);
            True((await first.ConfigureAwait(false)).Succeeded, "first completion");
        }

        private static async Task ProviderDisposalIsIdempotent()
        {
            IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                new FakeEncoder());
            await provider.DisposeAsync().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task DisposedProviderRejectsInvocation()
        {
            IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                new FakeEncoder());
            await provider.DisposeAsync().ConfigureAwait(false);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.Unavailable,
                result.Status,
                "disposed status");
        }

        private static async Task ExecutorCancellationRemainsCancellable()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            var selection = new VisionProviderSelection(provider.Descriptor);
            VisionLanguageRequest request = Request(
                provider,
                frame,
                selection: selection);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            VisionLanguageResult result =
                await VisionProviderExecutor.AnalyzeVisionLanguageAsync(
                    provider,
                    request,
                    selection,
                    cancellation.Token).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.Cancelled,
                result.Status,
                "executor cancellation");
            Equal(0, transport.CallCount, "executor cancelled transport calls");
        }

        private static async Task ProviderDoesNotDisposeInputFrame()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses);
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            ReachyVisionFrame frame = Frame();
            try
            {
                VisionLanguageResult result = await provider.AnalyzeAsync(
                    Request(provider, frame),
                    CancellationToken.None).ConfigureAwait(false);
                True(result.Succeeded, "frame ownership success");
                False(frame.IsDisposed, "input frame remains caller-owned");
            }
            finally
            {
                await frame.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task ProviderDisposesEncodedPayloadAfterSuccess()
        {
            var encoder = new FakeEncoder();
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                new FakeTransport(OpenAiVisionEndpointStyle.Responses),
                encoder);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            True(result.Succeeded, "payload disposal success");
            True(encoder.LastImage?.IsDisposed == true, "encoded payload disposed");
        }

        private static async Task ProviderExceptionDoesNotExposeMessage()
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    throw new InvalidOperationException(
                        "Authorization: Bearer sk-do-not-leak"),
            };
            await using IVisionLanguageProvider provider = Provider(
                OpenAiVisionEndpointStyle.Responses,
                transport,
                new FakeEncoder());
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            Equal(
                VisionOperationStatus.ProviderFailure,
                result.Status,
                "transport exception status");
            Contains(
                nameof(InvalidOperationException),
                result.Diagnostic,
                "exception type retained");
            False(
                result.Diagnostic.Contains(
                    "sk-do-not-leak",
                    StringComparison.Ordinal),
                "exception secret absent");
            False(
                result.Diagnostic.Contains(
                    "Authorization:",
                    StringComparison.Ordinal),
                "exception header absent");
        }

    }
}

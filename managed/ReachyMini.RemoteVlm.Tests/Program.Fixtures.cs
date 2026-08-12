#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static RemoteVlmImagePolicy Policy(
            int maximumWidth = 1024,
            int maximumHeight = 1024,
            int maximumEncodedBytes = 4 * 1024 * 1024,
            RemoteVlmImageFormat preferredFormat = RemoteVlmImageFormat.Jpeg,
            int lossyQuality = 85,
            RemoteVlmImageDetail detail = RemoteVlmImageDetail.Auto,
            RemoteVlmInvalidPixelPolicy invalidPixelPolicy =
                RemoteVlmInvalidPixelPolicy.ReplaceWithOpaqueBlack,
            bool allowUpscaling = false)
        {
            return new RemoteVlmImagePolicy(
                maximumWidth,
                maximumHeight,
                maximumEncodedBytes,
                preferredFormat,
                lossyQuality,
                detail,
                invalidPixelPolicy,
                allowUpscaling);
        }

        private static OpenAiVisionProviderConfiguration Configuration(
            OpenAiVisionEndpointStyle endpointStyle =
                OpenAiVisionEndpointStyle.Responses,
            string modelId = "configured-model",
            VisionProviderLocation location = VisionProviderLocation.Cloud,
            bool supportsVisualQuestions = true,
            bool supportsSceneDescription = true,
            int maximumConcurrentOperations = 1,
            int maximumPromptCharacters = 4096,
            int maximumOutputTokens = 256,
            int maximumResponseCharacters = 4096,
            RemoteVlmImagePolicy? policy = null)
        {
            return new OpenAiVisionProviderConfiguration(
                endpointStyle,
                providerId: "openai-compatible-vlm",
                providerInstanceId: "remote-vlm-instance",
                displayName: "Remote VLM",
                version: "1",
                modelId,
                location,
                supportsVisualQuestions,
                supportsSceneDescription,
                maximumConcurrentOperations,
                maximumPromptCharacters,
                maximumOutputTokens,
                maximumResponseCharacters,
                policy ?? Policy());
        }

        private static IVisionLanguageProvider Provider(
            OpenAiVisionEndpointStyle endpointStyle,
            FakeTransport transport,
            FakeEncoder encoder,
            OpenAiVisionProviderConfiguration? configuration = null)
        {
            OpenAiVisionProviderConfiguration selected =
                configuration ?? Configuration(endpointStyle);
            return endpointStyle == OpenAiVisionEndpointStyle.Responses
                ? new OpenAiResponsesVisionLanguageProvider(
                    selected,
                    transport,
                    encoder)
                : new OpenAiChatCompletionsVisionLanguageProvider(
                    selected,
                    transport,
                    encoder);
        }

        private static ReachyVisionFrameIdentity Identity(
            ulong sourceSequence = 1UL)
        {
            return new ReachyVisionFrameIdentity(
                cameraId: "camera-0",
                sourceSessionId: 1UL,
                sourceSequence,
                sourceTimestampNanoseconds: checked((long)sourceSequence * 1_000L),
                authoritativeSequence: sourceSequence,
                continuityId: 1U);
        }

        internal static RemoteVlmEncodedImage EncodedImage(
            ReachyVisionFrameIdentity identity,
            VisionFrameOrigin sourceOrigin =
                VisionFrameOrigin.TransformedReachyEye,
            int sourceWidth = 10,
            int sourceHeight = 10,
            int width = 10,
            int height = 10,
            RemoteVlmImageFormat format = RemoteVlmImageFormat.Jpeg,
            RemoteVlmInvalidPixelPolicy invalidPixelPolicy =
                RemoteVlmInvalidPixelPolicy.ReplaceWithOpaqueBlack,
            bool validityMaskApplied = true,
            bool containsOnlyValidPixels = true,
            bool upscaled = false,
            byte[]? bytes = null)
        {
            return new RemoteVlmEncodedImage(
                identity,
                sourceOrigin,
                sourceWidth,
                sourceHeight,
                width,
                height,
                format,
                invalidPixelPolicy,
                validityMaskApplied,
                containsOnlyValidPixels,
                upscaled,
                bytes ?? new byte[] { 1, 2, 3 });
        }

        private static ReachyVisionFrame Frame(
            VisionCoverageState state = VisionCoverageState.Normal,
            long validPixelCount = 100L,
            long totalPixelCount = 100L,
            bool shouldStopVisionDrivenTurning = false,
            ulong sourceSequence = 1UL)
        {
            using var resources = new FakeResources(
                width: 10,
                height: 10,
                includeValidityMask: true);
            var coverage = new ReachyVisionCoverage(
                state,
                validPixelCount,
                totalPixelCount,
                hasValidityMask: true,
                shouldStopVisionDrivenTurning,
                diagnostic: "synthetic transformed coverage");
            var frame = new ReachyVisionFrame(
                VisionFrameOrigin.TransformedReachyEye,
                Identity(sourceSequence),
                coverage,
                resources);
            resources.TransferOwnershipToFrame();
            return frame;
        }

        private static ReachyVisionFrame RawFrame()
        {
            using var resources = new FakeResources(
                width: 10,
                height: 10,
                includeValidityMask: false);
            var coverage = new ReachyVisionCoverage(
                VisionCoverageState.Unavailable,
                validPixelCount: 0L,
                totalPixelCount: 0L,
                hasValidityMask: false,
                shouldStopVisionDrivenTurning: true,
                diagnostic: "raw debug coverage unavailable");
            var frame = new ReachyVisionFrame(
                VisionFrameOrigin.RawPhoneDebug,
                Identity(),
                coverage,
                resources);
            resources.TransferOwnershipToFrame();
            return frame;
        }

        private static VisionLanguageRequest Request(
            IVisionLanguageProvider provider,
            ReachyVisionFrame frame,
            string prompt = "What is visible?",
            bool networkAcknowledged = true,
            string requestId = "request-1",
            VisionProviderSelection? selection = null)
        {
            ArgumentNullException.ThrowIfNull(provider);
            VisionProviderSelection selected =
                selection ?? new VisionProviderSelection(provider.Descriptor);
            var context = new VisionRequestContext(
                requestId,
                selected.Current,
                TimeSpan.FromSeconds(5.0));
            return new VisionLanguageRequest(
                frame,
                prompt,
                context,
                networkAcknowledged);
        }

        private static async Task InvokeSuccessfulProvider(
            OpenAiVisionEndpointStyle endpointStyle,
            FakeTransport transport,
            OpenAiVisionProviderConfiguration? configuration = null)
        {
            await using IVisionLanguageProvider provider = Provider(
                endpointStyle,
                transport,
                new FakeEncoder(),
                configuration);
            await using ReachyVisionFrame frame = Frame();
            VisionLanguageResult result = await provider.AnalyzeAsync(
                Request(provider, frame),
                CancellationToken.None).ConfigureAwait(false);
            True(result.Succeeded, "successful provider invocation");
        }

        private static async Task AssertEncodingFailure(
            RemoteVlmImageEncodingStatus encoderStatus,
            VisionOperationStatus expectedStatus,
            string code)
        {
            var encoder = new FakeEncoder
            {
                Handler = (request, token) =>
                    new ValueTask<RemoteVlmImageEncodingResult>(
                        RemoteVlmImageEncodingResult.Failure(
                            encoderStatus,
                            code,
                            requiresEncoderReset: false)),
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
            Equal(expectedStatus, result.Status, "encoding failure mapping");
            Equal(0, transport.CallCount, "encoding failure transport calls");
        }

        private static async Task AssertTransportFailure(
            OpenAiVisionTransportStatus transportStatus,
            VisionOperationStatus expectedStatus,
            OpenAiVisionProviderError error)
        {
            var transport = new FakeTransport(OpenAiVisionEndpointStyle.Responses)
            {
                Handler = (request, token) =>
                    new ValueTask<OpenAiVisionTransportResult>(
                        OpenAiVisionTransportResult.Failure(
                            transportStatus,
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
            Equal(expectedStatus, result.Status, "transport failure mapping");
        }

        private static OpenAiVisionProviderError Error(
            OpenAiVisionProviderErrorCategory category,
            string code)
        {
            return new OpenAiVisionProviderError(
                category,
                code,
                httpStatusCode: null,
                providerRequestId: null,
                detail: "Synthetic provider failure.");
        }
    }
}

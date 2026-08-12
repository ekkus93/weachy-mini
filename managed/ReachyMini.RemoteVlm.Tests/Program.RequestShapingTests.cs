#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
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
    }
}

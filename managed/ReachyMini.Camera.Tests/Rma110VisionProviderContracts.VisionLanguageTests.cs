#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
        private static async Task CloudDisclosureIsRequiredBeforeInvocationAsync()
        {
            ProviderDescriptor descriptor = Descriptor(
                VisionProviderKind.SemanticVisionLanguage,
                "cloud-vlm",
                VisionProviderLocation.Cloud);
            var selection = new VisionProviderSelection(descriptor);
            await using var resources = new FakeResources(
                10,
                10,
                hasValidity: true);
            await using ReachyVisionFrame frame = Frame(
                resources,
                VisionFrameOrigin.TransformedReachyEye,
                VisionCoverageState.Normal,
                sourceSequence: 15UL);
            VisionRequestContext context = Context(
                "vlm-disclosure",
                selection,
                TimeSpan.FromSeconds(1.0));
            var request = new VisionLanguageRequest(
                frame,
                "What is visible?",
                context,
                networkDisclosureAcknowledged: false);
            await using var provider = new FakeVisionLanguageProvider(
                descriptor,
                (_, _) => new ValueTask<VisionLanguageResult>(
                    VisionLanguageResult.Success(
                        descriptor,
                        request,
                        "A person is visible.")));

            VisionLanguageResult result = await VisionProviderExecutor
                .AnalyzeVisionLanguageAsync(
                    provider,
                    request,
                    selection,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Equal(
                VisionOperationStatus.Unavailable,
                result.Status,
                "network disclosure status");
            Equal(0, provider.CallCount, "cloud provider not invoked");
            Equal(null, result.Text, "no undisclosed cloud result");
            await frame.DisposeAsync().ConfigureAwait(false);
        }
    }
}

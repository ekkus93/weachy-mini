#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
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
    }
}

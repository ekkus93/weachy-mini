#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
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

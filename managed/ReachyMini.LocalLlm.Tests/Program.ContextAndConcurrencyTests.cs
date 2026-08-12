#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static async Task TestContextPreflightAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { TokenCount = 2000 };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-context", "A long request."),
            new CollectingSink(),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ContextLimit, "Context overflow was not rejected.");
        Require(runtime.StartCount == 0, "Context overflow reached native generation start.");
        Require(runtime.LastMessages.Count == 2, "Context preflight silently dropped history/messages.");
    }

    private static async Task TestBusyAndCancellationAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { BlockUntilCancel = true };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task<LocalLlmGenerationResult> first = provider.GenerateAsync(
            Request("req-first", "Wait here."), new CollectingSink(), cancellation.Token);
        await runtime.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        LocalLlmGenerationResult busy = await provider.GenerateAsync(
            Request("req-busy", "Second request."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(busy.Status == LocalLlmGenerationStatus.Busy, "Concurrent request was not rejected as Busy.");
        Require(runtime.StartCount == 1, "Busy request was queued or started implicitly.");
        cancellation.Cancel();
        LocalLlmGenerationResult cancelled = await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Require(cancelled.Status == LocalLlmGenerationStatus.Cancelled, "Cancellation did not remain explicit.");
        Require(runtime.CancelCount == 1, "Cancellation did not issue exactly one native cancel.");
        Require(runtime.ReleaseCount == 1, "Cancelled generation handle was not released.");
    }

    private static async Task TestResetSuppressesStaleOutputAsync()
    {
        using FakeRuntime runtime = new FakeRuntime
        {
            BlockUntilCancel = true,
            EmitStaleTextAfterCancel = true,
        };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        CollectingSink sink = new CollectingSink();
        Task<LocalLlmGenerationResult> generation = provider.GenerateAsync(
            Request("req-reset", "Track this conversation."), sink, CancellationToken.None);
        await runtime.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        ulong priorEpoch = provider.ConversationEpoch;
        ulong newEpoch = provider.ResetConversation();
        Require(newEpoch != priorEpoch, "Conversation reset did not rotate the epoch.");
        LocalLlmGenerationResult result = await generation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.Superseded, "Reset generation was not superseded.");
        Require(!sink.Text.Contains("STALE", StringComparison.Ordinal), "Post-reset stale text reached the stream sink.");
        Require(runtime.CancelCount == 1, "Reset issued more than one native cancel.");
        Require(runtime.ReleaseCount == 1, "Reset generation handle was not released.");
    }
}

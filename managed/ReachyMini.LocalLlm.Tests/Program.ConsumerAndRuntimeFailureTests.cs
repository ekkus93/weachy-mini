#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static async Task TestStreamConsumerFailureAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueText(1UL, ValidIntent);
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-sink-fail", "Generate."),
            new ThrowingSink(LocalLlmStreamEventType.Text),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ConsumerFailure, "Text sink failure was swallowed.");
        Require(runtime.CancelCount == 1, "Text sink failure issued the wrong cancel count.");
        Require(runtime.ReleaseCount == 1, "Text sink failure leaked the generation handle.");
    }

    private static async Task TestTerminalConsumerFailureAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueText(1UL, ValidIntent);
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-terminal-fail", "Generate."),
            new ThrowingSink(LocalLlmStreamEventType.Completed),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ConsumerFailure, "Terminal sink failure was swallowed.");
        Require(result.Detail.Contains("Succeeded", StringComparison.Ordinal),
            "Terminal sink failure did not preserve the underlying disposition.");
    }

    private static async Task TestRuntimeTerminalErrorAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueError(1UL, 11, "decode failed");
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-runtime-error", "Generate."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.RuntimeFailure, "Native terminal error was not preserved.");
        Require(result.NativeStatus == 11, "Native terminal status was lost.");
        Require(result.Detail.Contains("decode failed", StringComparison.Ordinal), "Native terminal detail was lost.");
        Require(runtime.ReleaseCount == 1, "Native error terminal generation was not released.");
    }

    private static async Task TestFailedCancelDoesNotRetryAsync()
    {
        using FakeRuntime runtime = new FakeRuntime
        {
            BlockUntilCancel = true,
            CancelStatus = 11,
            TerminalAfterFailedCancel = true,
        };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task<LocalLlmGenerationResult> generation = provider.GenerateAsync(
            Request("req-cancel-fail", "Generate."),
            new CollectingSink(),
            cancellation.Token);
        await runtime.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        cancellation.Cancel();
        LocalLlmGenerationResult result = await generation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.RuntimeFailure,
            "Failed native cancel did not remain an explicit runtime failure.");
        Require(result.NativeStatus == 11, "Failed native cancel status was lost.");
        Require(runtime.CancelCount == 1, "Failed native cancel was retried implicitly.");
        Require(runtime.ReleaseCount == 1, "Terminal generation was not released after failed cancel.");
    }

    private static async Task TestPollExceptionCleanupAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { ThrowOnNextPoll = true };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-poll-throw", "Generate."),
            new CollectingSink(),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.RuntimeFailure,
            "Thrown poll did not become an explicit runtime failure.");
        Require(result.Detail.Contains("poll threw", StringComparison.OrdinalIgnoreCase),
            "Thrown poll detail was not preserved.");
        Require(runtime.CancelCount == 1, "Thrown poll did not issue exactly one cleanup cancel.");
        Require(runtime.ReleaseCount == 1, "Thrown poll leaked the generation handle.");
    }

    private static async Task TestMetricsExceptionStillReleasesAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { ThrowOnMetrics = true };
        runtime.EnqueueText(1UL, ValidIntent);
        runtime.EnqueueCompleted(2UL);
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-metrics-throw", "Generate."),
            new CollectingSink(),
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.RuntimeFailure,
            "Thrown metrics collection did not become an explicit runtime failure.");
        Require(result.Detail.Contains("metrics", StringComparison.OrdinalIgnoreCase),
            "Thrown metrics detail was not preserved.");
        Require(runtime.CancelCount == 0, "Terminal metrics failure issued an unnecessary cancel.");
        Require(runtime.ReleaseCount == 1, "Thrown metrics collection leaked the generation handle.");
    }
}

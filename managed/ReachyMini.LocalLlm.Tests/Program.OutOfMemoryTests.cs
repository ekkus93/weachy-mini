#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static async Task TestOutOfMemoryBeforeNativeHandleAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { ThrowOutOfMemoryOnStart = true };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-oom-start", "Allocate."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ResourceExhausted,
            "Pre-handle OOM was not classified as resource exhaustion.");
        Require(provider.State == LocalLlmProviderState.Faulted,
            "Pre-handle OOM did not fault the provider for explicit recovery.");
        Require(runtime.StartCount == 1 && runtime.ReleaseCount == 0,
            "Pre-handle OOM fabricated or leaked a known generation handle.");
    }

    private static async Task TestOutOfMemoryAfterNativeStartAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { ThrowOutOfMemoryOnNextPoll = true };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-oom-poll", "Allocate."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ResourceExhausted,
            "Post-start OOM was not classified as resource exhaustion.");
        Require(provider.State == LocalLlmProviderState.Faulted,
            "Post-start OOM did not fault the provider for explicit recovery.");
        Require(runtime.CancelCount == 1 && runtime.ReleaseCount == 1,
            "Post-start OOM did not cancel/drain/release the generation exactly once.");
    }

    private static async Task TestOutOfMemoryCleanupFailureFaultsAsync()
    {
        using FakeRuntime runtime = new FakeRuntime
        {
            ThrowOutOfMemoryOnNextPoll = true,
            CancelStatus = 7,
        };
        await using LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        LocalLlmGenerationResult result = await provider.GenerateAsync(
            Request("req-oom-cleanup", "Allocate."), new CollectingSink(), CancellationToken.None)
            .ConfigureAwait(false);
        Require(result.Status == LocalLlmGenerationStatus.ResourceExhausted,
            "OOM cleanup failure lost the resource-exhausted classification.");
        Require(provider.State == LocalLlmProviderState.Faulted,
            "OOM cleanup failure did not leave the provider faulted.");
        Require(runtime.CancelCount == 1 && runtime.ReleaseCount == 0,
            "OOM cleanup failure silently released or retried uncertain native state.");
    }

    private static async Task TestOutOfMemoryReloadRecoveryAndSecondGenerationAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { ThrowOutOfMemoryOnNextPoll = true };
        LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        try
        {
            LocalLlmGenerationResult exhausted = await provider.GenerateAsync(
                Request("req-oom-recover", "Allocate."), new CollectingSink(), CancellationToken.None)
                .ConfigureAwait(false);
            Require(exhausted.Status == LocalLlmGenerationStatus.ResourceExhausted,
                "Recovery proof did not begin from typed resource exhaustion.");
            Require(provider.State == LocalLlmProviderState.Faulted,
                "OOM recovery proof did not fault the provider before explicit reload.");

            LocalLlmReloadResult reload = await provider.ReloadAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Require(reload.Status == LocalLlmReloadStatus.Reloaded,
                "Explicit reload did not recover the provider after OOM: " + reload.Detail);
            Require(provider.State == LocalLlmProviderState.Ready,
                "Explicit post-OOM reload did not return the provider to Ready.");

            runtime.EnqueueText(1UL, ValidIntent);
            runtime.EnqueueCompleted(2UL);
            LocalLlmGenerationResult recovered = await provider.GenerateAsync(
                Request("req-after-oom-reload", "Recover."), new CollectingSink(), CancellationToken.None)
                .ConfigureAwait(false);
            Require(recovered.Status == LocalLlmGenerationStatus.Succeeded,
                "Second generation after explicit OOM reload did not succeed: " + recovered.Detail);
            Require(recovered.Intent != null,
                "Second generation after explicit OOM reload lost the validated behavior intent.");
            Require(runtime.StartCount == 2,
                "OOM recovery replayed or skipped a generation unexpectedly.");
            Require(runtime.CancelCount == 1,
                "OOM recovery introduced an extra hidden cancellation or retry.");
            Require(runtime.ReleaseCount == 2,
                "OOM recovery did not release exactly the failed and recovered generation handles.");
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        Require(runtime.LoadCount == 2 && runtime.UnloadCount == 2,
            "Post-OOM recovery did not own exactly one initial and one explicitly reloaded model handle.");
    }
}

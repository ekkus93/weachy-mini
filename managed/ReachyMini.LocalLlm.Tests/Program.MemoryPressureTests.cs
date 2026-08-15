#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.AppState;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static async Task TestMemoryPressureReleasesOnlyIdleModelAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        await using LocalLlmProvider provider =
            await CreateProviderAsync(runtime).ConfigureAwait(false);

        ReachyMemoryPressureReleaseResult release =
            provider.ReleaseForMemoryPressure();
        Require(
            release.Status == ReachyMemoryPressureReleaseStatus.Released,
            "Idle local LLM was not released under memory pressure.");
        Require(
            provider.State == LocalLlmProviderState.Unavailable,
            "Idle model release did not move provider to explicit unavailable state.");
        Require(runtime.UnloadCount == 1, "Idle model was not unloaded exactly once.");

        LocalLlmReloadResult reload = await provider.ReloadAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Require(
            reload.Status == LocalLlmReloadStatus.Reloaded,
            "Memory-pressure release could not be recovered by explicit reload.");
        Require(provider.State == LocalLlmProviderState.Ready, "Reload did not restore ready state.");
    }

    private static async Task TestMemoryPressureRetainsActiveGenerationAsync()
    {
        using FakeRuntime runtime = new FakeRuntime { BlockUntilCancel = true };
        await using LocalLlmProvider provider =
            await CreateProviderAsync(runtime).ConfigureAwait(false);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Task<LocalLlmGenerationResult> generation = provider.GenerateAsync(
            Request("req-memory-pressure", "Keep this generation intact."),
            new CollectingSink(),
            cancellation.Token);
        await runtime.GenerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);

        ReachyMemoryPressureReleaseResult release =
            provider.ReleaseForMemoryPressure();
        Require(
            release.Status == ReachyMemoryPressureReleaseStatus.RetainedActiveState,
            "Memory pressure did not preserve active generation state.");
        Require(runtime.UnloadCount == 0, "Active model was unloaded during generation.");
        Require(runtime.CancelCount == 0, "Memory pressure cancelled an active generation.");

        cancellation.Cancel();
        LocalLlmGenerationResult result = await generation
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);
        Require(
            result.Status == LocalLlmGenerationStatus.Cancelled,
            "Caller cancellation changed after memory-pressure retention.");
    }

    private static async Task TestMemoryPressureRetainsActiveReloadAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        await using LocalLlmProvider provider =
            await CreateProviderAsync(runtime).ConfigureAwait(false);
        runtime.LoadEntered.Reset();
        runtime.BlockLoad = true;

        Task<LocalLlmReloadResult> reloadTask =
            provider.ReloadAsync(CancellationToken.None);
        Require(
            runtime.LoadEntered.Wait(TimeSpan.FromSeconds(5)),
            "Reload did not enter the native model load gate.");
        Require(runtime.UnloadCount == 1, "Reload did not unload its prior model exactly once.");

        ReachyMemoryPressureReleaseResult release =
            provider.ReleaseForMemoryPressure();
        Require(
            release.Status == ReachyMemoryPressureReleaseStatus.RetainedActiveState,
            "Memory pressure did not preserve the active reload transition.");
        Require(runtime.UnloadCount == 1, "Memory pressure double-unloaded during reload.");

        runtime.LoadRelease.Set();
        LocalLlmReloadResult reload = await reloadTask
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);
        Require(
            reload.Status == LocalLlmReloadStatus.Reloaded,
            "Reload did not recover after memory-pressure retention.");
    }
}

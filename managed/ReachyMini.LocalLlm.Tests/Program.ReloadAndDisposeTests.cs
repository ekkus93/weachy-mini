#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static async Task TestReloadRecoveryAndDisposeAsync()
    {
        using FakeRuntime runtime = new FakeRuntime();
        runtime.EnqueueLoad(0, 101UL, string.Empty);
        runtime.EnqueueLoad(6, 0UL, "reload load failed");
        runtime.EnqueueLoad(0, 202UL, string.Empty);
        LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        try
        {
            LocalLlmReloadResult failed = await provider.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            Require(failed.Status == LocalLlmReloadStatus.RuntimeFailure, "Reload failure was hidden.");
            Require(provider.State == LocalLlmProviderState.Faulted, "Failed reload did not fault provider state.");
            LocalLlmReloadResult recovered = await provider.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            Require(recovered.Status == LocalLlmReloadStatus.Reloaded,
                "Provider did not recover in-process on explicit reload.");
            Require(provider.State == LocalLlmProviderState.Ready, "Recovered provider did not return to Ready.");
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        Require(runtime.UnloadCount == 2, "Reload/dispose did not unload exactly the owned model handles.");
    }
}

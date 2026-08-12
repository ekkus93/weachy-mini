#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static async Task UnavailableStubReportsNoRuntimeOrCapability()
        {
            await using var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            Equal(
                LocalVlmAdapterState.Unavailable,
                adapter.Availability.State,
                "stub state");
            False(adapter.Availability.RuntimePresent, "stub runtime");
            False(adapter.Availability.CanCreateProvider, "stub availability");
            False(adapter.Capabilities.CanLoadModels, "stub load capability");
            False(adapter.Capabilities.CanCreateProviders, "stub create capability");
            Equal(0, adapter.Capabilities.MaximumConcurrentLoads, "stub load capacity");
        }

        private static async Task UnavailableStubNeverCreatesOrFallsBack()
        {
            await using var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            LocalVlmProviderCreationResult result =
                await adapter.CreateProviderAsync(
                    Configuration(),
                    CancellationToken.None).ConfigureAwait(false);
            Equal(
                LocalVlmProviderCreationStatus.Unavailable,
                result.Status,
                "stub creation status");
            True(result.Provider == null, "stub no provider");
            False(result.RequiresAdapterReset, "stub reset");
            Contains("no fallback", result.Diagnostic, "stub fallback diagnostic");
            Contains("download", result.Diagnostic, "stub download diagnostic");
        }

        private static async Task UnavailableStubHonorsPreCancellation()
        {
            await using var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            LocalVlmProviderCreationResult result =
                await adapter.CreateProviderAsync(
                    Configuration(),
                    cancellation.Token).ConfigureAwait(false);
            Equal(
                LocalVlmProviderCreationStatus.Cancelled,
                result.Status,
                "stub cancellation");
            True(result.Provider == null, "cancelled no provider");
        }

        private static async Task UnavailableStubDisposalIsIdempotent()
        {
            var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            await adapter.DisposeAsync().ConfigureAwait(false);
            await adapter.DisposeAsync().ConfigureAwait(false);
            Equal(
                LocalVlmAdapterState.Disposed,
                adapter.Availability.State,
                "disposed state");
            LocalVlmProviderCreationResult result =
                await adapter.CreateProviderAsync(
                    Configuration(),
                    CancellationToken.None).ConfigureAwait(false);
            Equal(
                LocalVlmProviderCreationStatus.Unavailable,
                result.Status,
                "disposed creation");
        }
    }
}

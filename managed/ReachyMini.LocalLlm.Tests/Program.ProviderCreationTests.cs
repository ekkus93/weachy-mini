#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static async Task TestManifestArtifactAndAbiFailuresAsync()
    {
        LocalModelManifest manifest = CreateManifest();
        LocalLlmExecutionProfile profile = LocalLlmExecutionProfile.CreateRma133V6Baseline();

        using (FakeRuntime mismatchRuntime = new FakeRuntime())
        {
            LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
                manifest,
                CreateApprovedArtifact("wrong-model"),
                profile,
                mismatchRuntime,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Status == LocalLlmProviderCreationStatus.InvalidConfiguration,
                "Manifest/artifact identity mismatch did not fail closed.");
            Require(mismatchRuntime.LoadCount == 0, "Mismatch reached native model load.");
        }

        using (FakeRuntime abiRuntime = new FakeRuntime { AbiVersion = 1U })
        {
            LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
                manifest,
                CreateApprovedArtifact(),
                profile,
                abiRuntime,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Status == LocalLlmProviderCreationStatus.Unavailable,
                "ABI mismatch did not surface as unavailable.");
            Require(abiRuntime.LoadCount == 0, "ABI mismatch reached native model load.");
        }

        using (FakeRuntime loadRuntime = new FakeRuntime())
        {
            loadRuntime.EnqueueLoad(6, 0UL, "load failed");
            LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
                manifest,
                CreateApprovedArtifact(),
                profile,
                loadRuntime,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Status == LocalLlmProviderCreationStatus.RuntimeFailure,
                "Native model-load failure was not preserved.");
            Require(result.Provider == null, "Failed model load returned a provider.");
        }
    }
}

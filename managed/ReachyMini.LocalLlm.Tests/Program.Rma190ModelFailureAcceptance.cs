#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    // RMA-190 deterministic local-model failure acceptance. This is one
    // integration scenario spanning the production package manager and the
    // production LocalLlmProvider. The package bytes and OOM trigger are
    // explicit deterministic fault fixtures; they are not physical memory
    // measurements and do not qualify RMA-135/RMA-184 performance claims.
    private static async Task TestRma190ModelFailureScenarioAsync()
    {
        byte[] packageBytes = Encoding.UTF8.GetBytes(
            "rma190-deterministic-model-package-fixture-v1");
        LocalModelManifest packageManifest = CreateRma190PackageManifest(packageBytes);
        string root = Path.Combine(
            Path.GetTempPath(),
            "reachy-rma190-model-failure-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        try
        {
            using var manager = new LocalModelPackageManager(
                root,
                new Rma190StorageProbe(),
                new LocalModelPackageOptions(
                    maximumArtifactBytes: 1024L * 1024L,
                    safetyReserveBytes: 0L));
            using var source = new MemoryStream(packageBytes, writable: false);
            LocalModelPackageResult installed = await manager.ImportAsync(
                    packageManifest,
                    source,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(installed.Succeeded, "RMA-190 model fixture import failed: " + installed.Detail);
            LocalModelApprovedArtifact approved = installed.Artifact ??
                throw new InvalidOperationException(
                    "RMA-190 model fixture import did not issue an approved artifact.");

            File.WriteAllText(
                approved.FullPath,
                "rma190-corrupted-model-fixture",
                Encoding.UTF8);
            LocalModelPackageResult corrupted = await manager.ResolveInstalledAsync(
                    packageManifest,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                !corrupted.Succeeded &&
                    corrupted.Failure == LocalModelPackageFailure.InstalledArtifactCorrupt,
                "RMA-190 corrupt installed model was not rejected by the production package manager.");
            Require(
                corrupted.Artifact == null,
                "RMA-190 corrupt installed model exposed an approved artifact.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        using FakeRuntime runtime = new FakeRuntime
        {
            ThrowOutOfMemoryOnNextPoll = true,
        };
        LocalLlmProvider provider = await CreateProviderAsync(runtime).ConfigureAwait(false);
        try
        {
            LocalLlmGenerationResult exhausted = await provider.GenerateAsync(
                    Request("rma190-oom", "Exercise deterministic resource exhaustion."),
                    new CollectingSink(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                exhausted.Status == LocalLlmGenerationStatus.ResourceExhausted,
                "RMA-190 OOM fixture was not surfaced as typed resource exhaustion.");
            Require(
                provider.State == LocalLlmProviderState.Faulted,
                "RMA-190 OOM did not fault the provider pending explicit recovery.");
            Require(
                runtime.StartCount == 1 &&
                    runtime.CancelCount == 1 &&
                    runtime.ReleaseCount == 1,
                "RMA-190 OOM path silently retried or failed to clean its generation exactly once.");

            LocalLlmReloadResult reload = await provider.ReloadAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                reload.Status == LocalLlmReloadStatus.Reloaded &&
                    provider.State == LocalLlmProviderState.Ready,
                "RMA-190 explicit reload did not recover the faulted provider.");

            runtime.EnqueueText(1UL, ValidIntent);
            runtime.EnqueueCompleted(2UL);
            LocalLlmGenerationResult recovered = await provider.GenerateAsync(
                    Request("rma190-after-oom", "Recover after explicit reload."),
                    new CollectingSink(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                recovered.Status == LocalLlmGenerationStatus.Succeeded &&
                    recovered.Intent != null,
                "RMA-190 provider did not produce a validated behavior intent after explicit reload.");
            Require(
                runtime.StartCount == 2 &&
                    runtime.CancelCount == 1 &&
                    runtime.ReleaseCount == 2,
                "RMA-190 recovery introduced a hidden retry, cancellation, or leaked generation.");
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        Require(
            runtime.LoadCount == 2 && runtime.UnloadCount == 2,
            "RMA-190 recovery did not own exactly the initial and explicitly reloaded model handles.");
    }

    private static LocalModelManifest CreateRma190PackageManifest(byte[] bytes)
    {
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new LocalModelManifest(
            LocalModelManifestPolicy.CurrentSchemaVersion,
            new LocalModelIdentity(
                "rma190.model-failure-fixture",
                "rma190.model-failure-fixture",
                "RMA-190 deterministic model-failure fixture",
                "1",
                new Uri("https://models.example.invalid/rma190"),
                "deterministic-fixture-v1",
                "NOASSERTION",
                experimental: true,
                "Deterministic RMA-190 package corruption fixture only."),
            new LocalModelRuntimeRequirement(
                "reachy_llama",
                LocalModelManifestPolicy.ReachyLlamaAbiVersion,
                requiresNetworkAccess: false),
            new LocalModelArtifact(
                "rma190-model.gguf",
                bytes.Length,
                sha256),
            new LocalModelGgufMetadata(
                3,
                "rma190-fixture",
                "fixture-q",
                1L,
                "fixture-tokenizer",
                "fixture-pre"),
            new LocalModelInferenceProfile(
                1024,
                "{{ rma190 fixture }}",
                Array.Empty<string>(),
                new LocalModelMemoryEstimate(1024L * 1024L, 1024, 128),
                1),
            new LocalModelDeviceCompatibility(
                SupportedAbis,
                26,
                Array.Empty<string>(),
                1024L * 1024L,
                LocalModelManifestPolicy.ReachyLlamaAbiVersion));
    }

    private sealed class Rma190StorageProbe : ILocalModelStorageProbe
    {
        public long GetAvailableBytes(string managedStoreRoot)
        {
            Require(
                Path.IsPathRooted(managedStoreRoot),
                "RMA-190 package manager supplied a non-rooted managed store.");
            return long.MaxValue;
        }
    }
}

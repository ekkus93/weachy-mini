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
    private static async Task TestRma190ModelFailureEndToEndAsync()
    {
        await TestRma190CorruptPackageFailsClosedAsync().ConfigureAwait(false);
        await TestRma190OutOfMemoryRequiresExplicitReloadAsync().ConfigureAwait(false);
    }

    private static async Task TestRma190CorruptPackageFailsClosedAsync()
    {
        byte[] modelBytes = Encoding.UTF8.GetBytes(
            "synthetic-rma190-model-package-payload-v1");
        LocalModelManifest manifest = CreateRma190PackageManifest(modelBytes);
        string root = Path.Combine(
            Path.GetTempPath(),
            "reachy-rma190-model-failure-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var manager = new LocalModelPackageManager(
                root,
                new Rma190StorageProbe(),
                new LocalModelPackageOptions(
                    maximumArtifactBytes: 1024L * 1024L,
                    safetyReserveBytes: 0L));
            using var source = new MemoryStream(modelBytes, writable: false);
            LocalModelPackageResult installed = await manager.ImportAsync(
                    manifest,
                    source,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                installed.Succeeded,
                "RMA-190 corruption setup could not install the fixture: " +
                installed.Detail);
            LocalModelApprovedArtifact approved = installed.Artifact ??
                throw new InvalidOperationException(
                    "RMA-190 corruption setup returned no approved artifact.");

            byte[] tampered = (byte[])modelBytes.Clone();
            tampered[0] ^= 0x5A;
            File.WriteAllBytes(approved.FullPath, tampered);

            LocalModelPackageResult resolved = await manager.ResolveInstalledAsync(
                    manifest,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                !resolved.Succeeded,
                "RMA-190 corrupt installed model unexpectedly resolved.");
            Require(
                resolved.Failure == LocalModelPackageFailure.InstalledArtifactCorrupt,
                "RMA-190 corrupt installed model returned the wrong failure: " +
                resolved.Failure);
            Require(
                resolved.Artifact == null,
                "RMA-190 corrupt installed model exposed an approved path.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task TestRma190OutOfMemoryRequiresExplicitReloadAsync()
    {
        using var runtime = new FakeRuntime
        {
            ThrowOutOfMemoryOnNextPoll = true,
        };
        LocalLlmProvider provider = await CreateProviderAsync(runtime)
            .ConfigureAwait(false);
        try
        {
            LocalLlmGenerationResult exhausted = await provider.GenerateAsync(
                    Request("rma190-oom", "Allocate."),
                    new CollectingSink(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                exhausted.Status == LocalLlmGenerationStatus.ResourceExhausted,
                "RMA-190 OOM did not surface typed resource exhaustion.");
            Require(
                provider.State == LocalLlmProviderState.Faulted,
                "RMA-190 OOM did not fault the provider.");
            Require(
                runtime.StartCount == 1,
                "RMA-190 OOM silently retried generation.");
            Require(
                runtime.CancelCount == 1 && runtime.ReleaseCount == 1,
                "RMA-190 OOM did not cancel and release the failed generation exactly once.");

            LocalLlmReloadResult reload = await provider.ReloadAsync(
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                reload.Status == LocalLlmReloadStatus.Reloaded,
                "RMA-190 explicit reload did not recover the OOM-faulted provider: " +
                reload.Detail);
            Require(
                provider.State == LocalLlmProviderState.Ready,
                "RMA-190 explicit reload did not return the provider to Ready.");

            runtime.EnqueueText(1UL, ValidIntent);
            runtime.EnqueueCompleted(2UL);
            LocalLlmGenerationResult recovered = await provider.GenerateAsync(
                    Request("rma190-after-oom-reload", "Recover."),
                    new CollectingSink(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            Require(
                recovered.Status == LocalLlmGenerationStatus.Succeeded,
                "RMA-190 generation did not recover after explicit reload: " +
                recovered.Detail);
            Require(
                recovered.Intent != null,
                "RMA-190 recovered generation lost the validated behavior intent.");
            Require(
                runtime.StartCount == 2,
                "RMA-190 OOM recovery added a hidden retry or skipped the explicit replay.");
            Require(
                runtime.CancelCount == 1,
                "RMA-190 explicit recovery added an unexpected hidden cancellation.");
        }
        finally
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static LocalModelManifest CreateRma190PackageManifest(
        byte[] modelBytes)
    {
        return new LocalModelManifest(
            LocalModelManifestPolicy.CurrentSchemaVersion,
            new LocalModelIdentity(
                "rma190.synthetic",
                "rma190.synthetic-model",
                "RMA-190 Synthetic Model",
                "1",
                new Uri("https://models.example.invalid/repository"),
                "synthetic-revision",
                "NOASSERTION",
                experimental: true,
                "Synthetic RMA-190 model-failure fixture only."),
            new LocalModelRuntimeRequirement(
                "reachy_llama",
                LocalModelManifestPolicy.ReachyLlamaAbiVersion,
                requiresNetworkAccess: false),
            new LocalModelArtifact(
                "model.gguf",
                modelBytes.Length,
                Rma190Sha256(modelBytes)),
            new LocalModelGgufMetadata(
                3,
                "synthetic",
                "synthetic-q",
                1L,
                "synthetic-tokenizer",
                "synthetic-pre"),
            new LocalModelInferenceProfile(
                1024,
                "{{ synthetic }}",
                Array.Empty<string>(),
                new LocalModelMemoryEstimate(
                    1024L * 1024L,
                    1024,
                    128),
                1),
            new LocalModelDeviceCompatibility(
                SupportedAbis,
                26,
                Array.Empty<string>(),
                1024L * 1024L,
                LocalModelManifestPolicy.ReachyLlamaAbiVersion));
    }

    private static string Rma190Sha256(byte[] bytes)
    {
        byte[] digest = SHA256.HashData(bytes);
        var builder = new StringBuilder(digest.Length * 2);
        for (int index = 0; index < digest.Length; ++index)
        {
            builder.Append(
                digest[index].ToString(
                    "x2",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private sealed class Rma190StorageProbe : ILocalModelStorageProbe
    {
        public long GetAvailableBytes(string managedStoreRoot)
        {
            Require(
                Path.IsPathRooted(managedStoreRoot),
                "RMA-190 package-manager storage probe received a non-rooted store.");
            return long.MaxValue;
        }
    }
}

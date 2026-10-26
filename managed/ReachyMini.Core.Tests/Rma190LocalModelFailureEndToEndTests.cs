#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.Core.Tests
{
    internal static class Rma190LocalModelFailureEndToEndTests
    {
        private static readonly byte[] PackageBytes =
            Encoding.UTF8.GetBytes("rma190-local-model-package");

        [ModuleInitializer]
        internal static void Run()
        {
            CorruptInstalledModelCannotBecomeApprovedArtifact().GetAwaiter().GetResult();
            ProviderOutOfMemoryFaultsWithoutHiddenRetry().GetAwaiter().GetResult();
        }

        private static async Task CorruptInstalledModelCannotBecomeApprovedArtifact()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "reachy-rma190-corrupt-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var manager = new LocalModelPackageManager(
                    root,
                    new ConstantStorageProbe(),
                    new LocalModelPackageOptions(
                        maximumArtifactBytes: 1024 * 1024,
                        safetyReserveBytes: 0));
                LocalModelManifest manifest = CreatePackageManifest();
                using var source = new MemoryStream(PackageBytes, writable: false);
                LocalModelPackageResult imported = await manager.ImportAsync(
                    manifest,
                    source,
                    CancellationToken.None).ConfigureAwait(false);
                True(imported.Succeeded, "fixture package import");
                LocalModelApprovedArtifact artifact = imported.Artifact ??
                    throw new InvalidOperationException(
                        "RMA-190 model-failure fixture did not receive an approved artifact.");

                File.WriteAllText(artifact.FullPath, "tampered", Encoding.UTF8);
                LocalModelPackageResult resolved = await manager.ResolveInstalledAsync(
                    manifest,
                    CancellationToken.None).ConfigureAwait(false);

                False(resolved.Succeeded, "tampered package resolution succeeded");
                Equal(
                    LocalModelPackageFailure.InstalledArtifactCorrupt,
                    resolved.Failure,
                    "tampered package failure");
                True(resolved.Artifact == null, "tampered package exposed approved artifact");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static async Task ProviderOutOfMemoryFaultsWithoutHiddenRetry()
        {
            LocalModelManifest manifest = LocalLlmBehaviorContract.CreateSelectedManifest();
            var artifact = new LocalModelApprovedArtifact(
                LocalLlmBehaviorContract.ManifestId,
                LocalLlmBehaviorContract.ModelId,
                Path.Combine(Path.GetTempPath(), "rma190-selected-model.gguf"),
                LocalLlmBehaviorContract.ArtifactBytes,
                LocalLlmBehaviorContract.ArtifactSha256);
            using var runtime = new OomRuntime();
            LocalLlmProviderCreationResult creation =
                await LocalLlmProvider.CreateForTestingAsync(
                    manifest,
                    artifact,
                    LocalLlmExecutionProfile.CreateRma133V6Baseline(),
                    runtime,
                    CancellationToken.None).ConfigureAwait(false);
            Equal(
                LocalLlmProviderCreationStatus.Created,
                creation.Status,
                "provider creation");
            LocalLlmProvider provider = creation.Provider ??
                throw new InvalidOperationException(
                    "RMA-190 OOM acceptance created no provider.");
            try
            {
                LocalLlmGenerationResult result = await provider.GenerateAsync(
                    new LocalLlmGenerationRequest(
                        "rma190-oom",
                        new[]
                        {
                            new LocalLlmChatMessage(
                                LocalLlmChatRole.User,
                                "Generate a safe behavior."),
                        }),
                    new NullSink(),
                    CancellationToken.None).ConfigureAwait(false);

                Equal(
                    LocalLlmGenerationStatus.ResourceExhausted,
                    result.Status,
                    "OOM generation status");
                Equal(LocalLlmProviderState.Faulted, provider.State, "OOM provider state");
                Equal(1, runtime.StartCount, "OOM generation attempt count");
                Equal(0, runtime.ReleaseCount, "OOM pre-handle release count");

                LocalLlmReloadResult reload = await provider.ReloadAsync(
                    CancellationToken.None).ConfigureAwait(false);
                Equal(LocalLlmReloadStatus.Reloaded, reload.Status, "explicit OOM reload");
                Equal(LocalLlmProviderState.Ready, provider.State, "post-reload provider state");
                Equal(2, runtime.LoadCount, "explicit reload model-load count");
                Equal(1, runtime.UnloadCount, "explicit reload unload count");
                Equal(1, runtime.StartCount, "reload did not replay failed generation");
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static LocalModelManifest CreatePackageManifest()
        {
            return new LocalModelManifest(
                LocalModelManifestPolicy.CurrentSchemaVersion,
                new LocalModelIdentity(
                    "rma190.corruption",
                    "rma190-corruption-model",
                    "RMA-190 corruption fixture",
                    "1",
                    new Uri("https://models.example.invalid/rma190"),
                    "fixture",
                    "NOASSERTION",
                    experimental: true,
                    "RMA-190 package-integrity fixture only."),
                new LocalModelRuntimeRequirement(
                    "reachy_llama",
                    LocalModelManifestPolicy.ReachyLlamaAbiVersion,
                    requiresNetworkAccess: false),
                new LocalModelArtifact(
                    "model.gguf",
                    PackageBytes.Length,
                    Sha256(PackageBytes)),
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
                    new LocalModelMemoryEstimate(1024 * 1024, 1024, 128),
                    1),
                new LocalModelDeviceCompatibility(
                    new[] { "arm64-v8a" },
                    26,
                    Array.Empty<string>(),
                    1024 * 1024,
                    LocalModelManifestPolicy.ReachyLlamaAbiVersion));
        }

        private static string Sha256(byte[] bytes)
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

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-190 local-model failure acceptance failed for {description}: " +
                    $"expected={expected}; actual={actual}.");
            }
        }

        private static void True(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 local-model failure acceptance failed for {description}: expected true.");
            }
        }

        private static void False(bool condition, string description)
        {
            if (condition)
            {
                throw new InvalidOperationException(
                    $"RMA-190 local-model failure acceptance failed for {description}: expected false.");
            }
        }

        private sealed class ConstantStorageProbe : ILocalModelStorageProbe
        {
            public long GetAvailableBytes(string managedStoreRoot)
            {
                if (!Path.IsPathRooted(managedStoreRoot))
                {
                    throw new InvalidOperationException(
                        "RMA-190 storage probe received a non-rooted path.");
                }
                return long.MaxValue;
            }
        }

        private sealed class NullSink : ILocalLlmStreamSink
        {
            public ValueTask OnEventAsync(
                LocalLlmStreamEvent streamEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }
        }

        private sealed class OomRuntime : ILocalLlmRuntime
        {
            private ulong nextModelHandle = 100UL;
            private bool throwOomOnStart = true;

            internal int LoadCount { get; private set; }
            internal int UnloadCount { get; private set; }
            internal int StartCount { get; private set; }
            internal int ReleaseCount { get; private set; }

            public uint GetAbiVersion()
            {
                return ReachyLlamaNativeContract.AbiVersion;
            }

            public LocalLlmRuntimeLoadResult LoadModel(string fullPath, bool checkTensors)
            {
                if (!Path.IsPathRooted(fullPath) || !checkTensors)
                {
                    return new LocalLlmRuntimeLoadResult(2, "invalid load contract", 0UL);
                }
                LoadCount = checked(LoadCount + 1);
                return new LocalLlmRuntimeLoadResult(0, string.Empty, ++nextModelHandle);
            }

            public LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle)
            {
                UnloadCount = checked(UnloadCount + 1);
                return new LocalLlmRuntimeCallResult(0, string.Empty);
            }

            public LocalLlmRuntimeTemplateResult ApplyChatTemplate(
                ulong modelHandle,
                string? chatTemplate,
                IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
            {
                return new LocalLlmRuntimeTemplateResult(
                    0,
                    string.Empty,
                    "templated-prompt");
            }

            public LocalLlmRuntimeTokenCountResult CountTokens(
                ulong modelHandle,
                string prompt)
            {
                return new LocalLlmRuntimeTokenCountResult(0, string.Empty, 8);
            }

            public LocalLlmRuntimeStartResult StartConstrained(
                ulong modelHandle,
                string prompt,
                LocalLlmExecutionProfile profile,
                string grammar,
                string grammarRoot)
            {
                StartCount = checked(StartCount + 1);
                if (throwOomOnStart)
                {
                    throwOomOnStart = false;
                    throw new OutOfMemoryException("simulated RMA-190 provider OOM");
                }
                return new LocalLlmRuntimeStartResult(0, string.Empty, 500UL);
            }

            public LocalLlmRuntimePollResult Poll(ulong generationHandle)
            {
                return new LocalLlmRuntimePollResult(
                    0,
                    string.Empty,
                    LocalLlmRuntimePollKind.Completed,
                    0,
                    1UL,
                    string.Empty);
            }

            public LocalLlmRuntimeCallResult Cancel(ulong generationHandle)
            {
                return new LocalLlmRuntimeCallResult(0, string.Empty);
            }

            public LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle)
            {
                return new LocalLlmRuntimeMetricsResult(0, string.Empty, null);
            }

            public LocalLlmRuntimeCallResult Release(ulong generationHandle)
            {
                ReleaseCount = checked(ReleaseCount + 1);
                return new LocalLlmRuntimeCallResult(0, string.Empty);
            }

            public void Dispose()
            {
            }
        }
    }
}

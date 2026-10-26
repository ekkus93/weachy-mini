#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

namespace ReachyMini.Core.Tests
{
    internal static partial class Program
    {
        private static readonly string[] Rma190StopTokens = { "<|im_end|>", "<|endoftext|>" };
        private static readonly string[] Rma190SupportedAbis = { "arm64-v8a" };
        private static readonly byte[] Rma190CorruptModelBytes = Encoding.UTF8.GetBytes(
            "rma190-corrupt-model-fixture");

        private static async Task TestRma190ModelCorruptionAndOomScenarioAsync()
        {
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
                LocalModelManifest corruptManifest = CreateRma190PackageManifest(
                    new string('0', 64));
                using var source = new MemoryStream(Rma190CorruptModelBytes, writable: false);
                LocalModelPackageResult corrupt = await manager.ImportAsync(
                    corruptManifest,
                    source,
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(false, corrupt.Succeeded, "RMA-190 corrupt model import success");
                AssertEqual(
                    LocalModelPackageFailure.Sha256Mismatch,
                    corrupt.Failure,
                    "RMA-190 corrupt model classification");
                AssertEqual<LocalModelApprovedArtifact?>(
                    null,
                    corrupt.Artifact,
                    "RMA-190 corrupt model never becomes approved");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }

            using var runtime = new Rma190OomRuntime();
            LocalLlmProviderCreationResult creation = await LocalLlmProvider.CreateForTestingAsync(
                CreateRma190LlmManifest(),
                CreateRma190ApprovedArtifact(),
                LocalLlmExecutionProfile.CreateRma133V6Baseline(),
                runtime,
                CancellationToken.None).ConfigureAwait(false);
            AssertEqual(
                LocalLlmProviderCreationStatus.Created,
                creation.Status,
                "RMA-190 OOM provider creation");
            LocalLlmProvider provider = creation.Provider ??
                throw new InvalidOperationException("RMA-190 OOM provider creation omitted provider.");
            try
            {
                LocalLlmGenerationResult exhausted = await provider.GenerateAsync(
                    Rma190LocalRequest("rma190-oom", "Allocate."),
                    new Rma190CollectingSink(),
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(
                    LocalLlmGenerationStatus.ResourceExhausted,
                    exhausted.Status,
                    "RMA-190 OOM classification");
                AssertEqual(
                    LocalLlmProviderState.Faulted,
                    provider.State,
                    "RMA-190 OOM faults provider for explicit recovery");

                LocalLlmReloadResult reload = await provider.ReloadAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                AssertEqual(
                    LocalLlmReloadStatus.Reloaded,
                    reload.Status,
                    "RMA-190 explicit OOM reload");
                AssertEqual(
                    LocalLlmProviderState.Ready,
                    provider.State,
                    "RMA-190 provider ready after explicit OOM reload");

                LocalLlmGenerationResult recovered = await provider.GenerateAsync(
                    Rma190LocalRequest("rma190-after-oom", "Recover."),
                    new Rma190CollectingSink(),
                    CancellationToken.None).ConfigureAwait(false);
                AssertEqual(
                    LocalLlmGenerationStatus.Succeeded,
                    recovered.Status,
                    "RMA-190 generation succeeds after explicit OOM reload");
                if (recovered.Intent == null)
                {
                    throw new InvalidOperationException(
                        "RMA-190 recovered generation omitted validated behavior intent.");
                }
                AssertEqual(2, runtime.StartCount, "RMA-190 OOM recovery generation count");
                AssertEqual(1, runtime.CancelCount, "RMA-190 OOM hidden retry/cancel count");
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static LocalModelManifest CreateRma190PackageManifest(string sha)
        {
            return new LocalModelManifest(
                LocalModelManifestPolicy.CurrentSchemaVersion,
                new LocalModelIdentity(
                    "rma190.corrupt-fixture",
                    "rma190.corrupt-fixture-model",
                    "RMA-190 Corrupt Fixture",
                    "1",
                    new Uri("https://models.example.invalid/rma190"),
                    "synthetic-revision",
                    "NOASSERTION",
                    experimental: true,
                    "Synthetic RMA-190 corruption fixture only."),
                new LocalModelRuntimeRequirement(
                    "reachy_llama",
                    LocalModelManifestPolicy.ReachyLlamaAbiVersion,
                    requiresNetworkAccess: false),
                new LocalModelArtifact(
                    "model.gguf",
                    Rma190CorruptModelBytes.Length,
                    sha),
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
                    new LocalModelMemoryEstimate(1024L * 1024L, 1024, 128),
                    1),
                new LocalModelDeviceCompatibility(
                    Rma190SupportedAbis,
                    26,
                    Array.Empty<string>(),
                    1024L * 1024L,
                    LocalModelManifestPolicy.ReachyLlamaAbiVersion));
        }

        private static LocalModelManifest CreateRma190LlmManifest()
        {
            return new LocalModelManifest(
                1,
                new LocalModelIdentity(
                    LocalLlmBehaviorContract.ManifestId,
                    LocalLlmBehaviorContract.ModelId,
                    "Qwen3 0.6B Q4_K_M",
                    "q4_k_m-8e42d41",
                    new Uri("https://huggingface.co/Qwen/Qwen3-0.6B-GGUF"),
                    "8e42d41f70cb6c571f58c3f31bd9287b372d97cc",
                    "Apache-2.0",
                    experimental: false,
                    experimentalReason: string.Empty),
                new LocalModelRuntimeRequirement("reachy_llama", 2, requiresNetworkAccess: false),
                new LocalModelArtifact(
                    "qwen3/qwen3-0.6b-q4_k_m.gguf",
                    LocalLlmBehaviorContract.ArtifactBytes,
                    LocalLlmBehaviorContract.ArtifactSha256),
                new LocalModelGgufMetadata(
                    3,
                    "qwen3",
                    "Q4_K_M",
                    596049920L,
                    "gpt2",
                    "qwen2"),
                new LocalModelInferenceProfile(
                    40960,
                    "manifest-template-must-not-be-used",
                    Rma190StopTokens,
                    new LocalModelMemoryEstimate(740380672L, 2048, 256),
                    4),
                new LocalModelDeviceCompatibility(
                    Rma190SupportedAbis,
                    26,
                    Array.Empty<string>(),
                    740380672L,
                    2));
        }

        private static LocalModelApprovedArtifact CreateRma190ApprovedArtifact()
        {
            return new LocalModelApprovedArtifact(
                LocalLlmBehaviorContract.ManifestId,
                LocalLlmBehaviorContract.ModelId,
                Path.Combine(Path.GetTempPath(), "rma190-qwen3-0.6b-q4_k_m.gguf"),
                LocalLlmBehaviorContract.ArtifactBytes,
                LocalLlmBehaviorContract.ArtifactSha256);
        }

        private static LocalLlmGenerationRequest Rma190LocalRequest(
            string requestId,
            string userText)
        {
            return new LocalLlmGenerationRequest(
                requestId,
                new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, userText) });
        }

        private sealed class Rma190StorageProbe : ILocalModelStorageProbe
        {
            public long GetAvailableBytes(string managedStoreRoot)
            {
                if (!Path.IsPathRooted(managedStoreRoot))
                {
                    throw new InvalidOperationException(
                        "RMA-190 package manager supplied a non-rooted store path.");
                }
                return long.MaxValue;
            }
        }

        private sealed class Rma190CollectingSink : ILocalLlmStreamSink
        {
            public ValueTask OnEventAsync(
                LocalLlmStreamEvent streamEvent,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class Rma190OomRuntime : ILocalLlmRuntime
        {
            private int pollStage;
            private bool mandatoryPromptMeasured;
            private bool generationCancelled;

            internal int StartCount { get; private set; }
            internal int CancelCount { get; private set; }

            public uint GetAbiVersion() => 2U;

            public LocalLlmRuntimeLoadResult LoadModel(string fullPath, bool checkTensors)
            {
                if (!Path.IsPathRooted(fullPath) || !checkTensors)
                {
                    throw new InvalidOperationException(
                        "RMA-190 local provider did not request a rooted, tensor-checked model load.");
                }
                return new LocalLlmRuntimeLoadResult(0, string.Empty, 100UL);
            }

            public LocalLlmRuntimeCallResult UnloadModel(ulong modelHandle)
            {
                return new LocalLlmRuntimeCallResult(0, string.Empty);
            }

            public LocalLlmRuntimeTemplateResult ApplyChatTemplate(
                ulong modelHandle,
                string? chatTemplate,
                IReadOnlyList<LocalLlmRuntimeChatMessage> messages)
            {
                return new LocalLlmRuntimeTemplateResult(0, string.Empty, "templated-prompt");
            }

            public LocalLlmRuntimeTokenCountResult CountTokens(ulong modelHandle, string prompt)
            {
                int count = mandatoryPromptMeasured ? 100 : 8;
                mandatoryPromptMeasured = true;
                return new LocalLlmRuntimeTokenCountResult(0, string.Empty, count);
            }

            public LocalLlmRuntimeStartResult StartConstrained(
                ulong modelHandle,
                string prompt,
                LocalLlmExecutionProfile profile,
                string grammar,
                string grammarRoot)
            {
                ++StartCount;
                pollStage = 0;
                generationCancelled = false;
                return new LocalLlmRuntimeStartResult(
                    0,
                    string.Empty,
                    checked(5000UL + (ulong)StartCount));
            }

            public LocalLlmRuntimePollResult Poll(ulong generationHandle)
            {
                if (StartCount == 1 && pollStage == 0)
                {
                    pollStage = 1;
                    throw new OutOfMemoryException("synthetic RMA-190 generation OOM");
                }
                if (generationCancelled)
                {
                    return new LocalLlmRuntimePollResult(
                        0,
                        string.Empty,
                        LocalLlmRuntimePollKind.Cancelled,
                        13,
                        2UL,
                        string.Empty);
                }
                if (pollStage == 0)
                {
                    pollStage = 1;
                    return new LocalLlmRuntimePollResult(
                        0,
                        string.Empty,
                        LocalLlmRuntimePollKind.Text,
                        0,
                        1UL,
                        "{\"schema_version\":1,\"speech\":\"Recovered.\",\"expression\":\"attentive\",\"gesture\":\"nod\",\"urgency\":\"normal\"}");
                }
                return new LocalLlmRuntimePollResult(
                    0,
                    string.Empty,
                    LocalLlmRuntimePollKind.Completed,
                    0,
                    2UL,
                    string.Empty);
            }

            public LocalLlmRuntimeCallResult Cancel(ulong generationHandle)
            {
                ++CancelCount;
                generationCancelled = true;
                return new LocalLlmRuntimeCallResult(0, string.Empty);
            }

            public LocalLlmRuntimeMetricsResult GetGenerationMetrics(ulong generationHandle)
            {
                return new LocalLlmRuntimeMetricsResult(
                    0,
                    string.Empty,
                    new LocalLlmGenerationMetrics(
                        100UL,
                        20UL,
                        1000UL,
                        2000UL,
                        3000UL,
                        2048,
                        256,
                        4,
                        4));
            }

            public LocalLlmRuntimeCallResult Release(ulong generationHandle)
            {
                return new LocalLlmRuntimeCallResult(0, string.Empty);
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
            }
        }
    }
}

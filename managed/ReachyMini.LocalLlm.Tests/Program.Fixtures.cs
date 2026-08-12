#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.LocalModels;

internal static partial class Program
{
    private static readonly string[] StopTokens = { "<|im_end|>", "<|endoftext|>" };
    private static readonly string[] SupportedAbis = { "arm64-v8a" };

    private static async Task<LocalLlmProvider> CreateProviderAsync(
        FakeRuntime runtime,
        LocalLlmExecutionProfile? profile = null)
    {
        LocalLlmProviderCreationResult result = await LocalLlmProvider.CreateForTestingAsync(
            CreateManifest(),
            CreateApprovedArtifact(),
            profile ?? LocalLlmExecutionProfile.CreateRma133V6Baseline(),
            runtime,
            CancellationToken.None).ConfigureAwait(false);
        Require(result.Status == LocalLlmProviderCreationStatus.Created,
            "Provider creation failed: " + result.Detail);
        return result.Provider ?? throw new InvalidOperationException("Created provider result had no provider.");
    }

    private static LocalLlmGenerationRequest Request(string requestId, string userText)
    {
        return new LocalLlmGenerationRequest(
            requestId,
            new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, userText) });
    }

    private static LocalModelManifest CreateManifest()
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
            new LocalModelGgufMetadata(3, "qwen3", "Q4_K_M", 596049920L, "gpt2", "qwen2"),
            new LocalModelInferenceProfile(
                40960,
                "manifest-template-must-not-be-used",
                StopTokens,
                new LocalModelMemoryEstimate(740380672L, 2048, 256),
                4),
            new LocalModelDeviceCompatibility(
                SupportedAbis,
                26,
                Array.Empty<string>(),
                740380672L,
                2));
    }

    private static LocalModelApprovedArtifact CreateApprovedArtifact(string? modelId = null)
    {
        return new LocalModelApprovedArtifact(
            LocalLlmBehaviorContract.ManifestId,
            modelId ?? LocalLlmBehaviorContract.ModelId,
            Path.Combine(Path.GetTempPath(), "rma134-qwen3-0.6b-q4_k_m.gguf"),
            LocalLlmBehaviorContract.ArtifactBytes,
            LocalLlmBehaviorContract.ArtifactSha256);
    }
}

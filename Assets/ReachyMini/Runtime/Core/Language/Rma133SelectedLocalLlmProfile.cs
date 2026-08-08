#nullable enable

using System;
using ReachyMini.LocalModels;

namespace ReachyMini.Language
{
    public static class Rma133SelectedLocalLlmProfile
    {
        public const string ManifestId = "rma133.qwen3-0.6b-q4-k-m.v1";
        public const string ModelId = "qwen3-0.6b";
        public const string DisplayName = "Qwen3 0.6B Q4_K_M";
        public const string ModelVersion = "q4_k_m-8e42d41";
        public const string SourceUri = "https://huggingface.co/Qwen/Qwen3-0.6B-GGUF";
        public const string SourceRevision = "8e42d41f70cb6c571f58c3f31bd9287b372d97cc";
        public const string LicenseId = "Apache-2.0";
        public const string RelativePath = "qwen3/qwen3-0.6b-q4_k_m.gguf";
        public const long FileSizeBytes = 396_704_416L;
        public const string Sha256 =
            "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e";
        public const string Architecture = "qwen3";
        public const string Quantization = "Q4_K_M";
        public const long ParameterCount = 596_049_920L;
        public const string TokenizerModel = "gpt2";
        public const string TokenizerPre = "qwen2";
        public const uint ContextLimitTokens = 40_960U;
        public const long PeakRamBytes = 740_380_672L;
        public const uint BasisContextTokens = 2_048U;
        public const uint BasisBatchTokens = 256U;
        public const int RecommendedThreads = 4;
        public const int MinimumAndroidApi = 26;
        public const long MinimumRamBytes = 740_380_672L;
        public const int ReachyLlamaAbiVersion = 2;
        public const string UserPromptSuffix = "/no_think";
        public const string GrammarRoot = "root";

        private static readonly string[] StopTokens =
        {
            "<|im_end|>",
            "<|endoftext|>",
        };

        private static readonly string[] AndroidAbis =
        {
            "arm64-v8a",
        };

        private static readonly string[] RequiredCpuFeatures = Array.Empty<string>();

        public static LocalModelManifest CreateManifest(string exactChatTemplate)
        {
            if (string.IsNullOrWhiteSpace(exactChatTemplate))
            {
                throw new ArgumentException(
                    "The exact selected-model chat template is required.",
                    nameof(exactChatTemplate));
            }

            return new LocalModelManifest(
                schemaVersion: 1,
                new LocalModelIdentity(
                    ManifestId,
                    ModelId,
                    DisplayName,
                    ModelVersion,
                    new Uri(SourceUri),
                    SourceRevision,
                    LicenseId,
                    experimental: false,
                    experimentalReason: string.Empty),
                new LocalModelRuntimeRequirement(
                    "reachy_llama",
                    ReachyLlamaAbiVersion,
                    requiresNetworkAccess: false),
                new LocalModelArtifact(
                    RelativePath,
                    FileSizeBytes,
                    Sha256),
                new LocalModelGgufMetadata(
                    ggufVersion: 3,
                    Architecture,
                    Quantization,
                    ParameterCount,
                    TokenizerModel,
                    TokenizerPre),
                new LocalModelInferenceProfile(
                    checked((int)ContextLimitTokens),
                    exactChatTemplate,
                    StopTokens,
                    new LocalModelMemoryEstimate(
                        PeakRamBytes,
                        checked((int)BasisContextTokens),
                        checked((int)BasisBatchTokens)),
                    RecommendedThreads),
                new LocalModelDeviceCompatibility(
                    AndroidAbis,
                    MinimumAndroidApi,
                    RequiredCpuFeatures,
                    MinimumRamBytes,
                    ReachyLlamaAbiVersion));
        }

        public static LocalLlmProviderConfiguration CreateProviderConfiguration(
            string exactSystemPrompt,
            string exactGrammar)
        {
            return new LocalLlmProviderConfiguration(
                exactSystemPrompt,
                exactGrammar,
                GrammarRoot,
                UserPromptSuffix,
                LocalLlmExecutionProfile.CreateInitialProductCoexistenceProfile(),
                maximumCommittedHistoryTurns: 8,
                managedEventQueueCapacity: 64);
        }
    }
}

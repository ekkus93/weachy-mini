#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.LocalModels
{
    public static class LocalModelManifestPolicy
    {
        public const int CurrentSchemaVersion = 1;

        public const int ReachyLlamaAbiVersion = 2;

        public const int MaximumStopTokenCount = 32;

        public const int MaximumCatalogCount = 256;
    }

    internal static class LocalModelManifestValidation
    {
        public static string RequireIdentifier(string value, string name)
        {
            string text = RequireBoundedText(value, name, 128);
            for (int index = 0; index < text.Length; ++index)
            {
                char character = text[index];
                bool valid =
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '.' ||
                    character == '_' ||
                    character == '-';
                if (!valid ||
                    (index == 0 &&
                        !(character >= 'a' && character <= 'z') &&
                        !(character >= '0' && character <= '9')))
                {
                    throw new ArgumentException(
                        "Local-model identifiers must use lowercase ASCII letters, digits, '.', '_' or '-', and start with a letter or digit.",
                        name);
                }
            }
            return text;
        }

        public static string RequireBoundedText(
            string value,
            string name,
            int maximumLength)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Local-model manifest text cannot be empty or whitespace.",
                    name);
            }
            if (value.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Local-model manifest text exceeds its bounded length.");
            }
            return value;
        }

        public static string RequireSha256(string value, string name)
        {
            string text = RequireBoundedText(value, name, 64);
            if (text.Length != 64)
            {
                throw new ArgumentException(
                    "Local-model SHA-256 values must contain exactly 64 lowercase hexadecimal characters.",
                    name);
            }
            for (int index = 0; index < text.Length; ++index)
            {
                char character = text[index];
                bool valid =
                    (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f');
                if (!valid)
                {
                    throw new ArgumentException(
                        "Local-model SHA-256 values must contain exactly 64 lowercase hexadecimal characters.",
                        name);
                }
            }
            return text;
        }

        public static string RequireSafeRelativeGgufPath(string value, string name)
        {
            string text = RequireBoundedText(value, name, 240);
            if (text[0] == '/' ||
                text[0] == '\\' ||
                text.Contains('\\') ||
                text.Contains(':') ||
                !text.EndsWith(".gguf", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Local-model artifacts must use short slash-separated relative paths ending in lowercase '.gguf'.",
                    name);
            }

            string[] segments = text.Split('/');
            for (int index = 0; index < segments.Length; ++index)
            {
                string segment = segments[index];
                if (segment.Length == 0 ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Local-model artifact paths cannot contain empty, '.' or '..' segments.",
                        name);
                }
            }
            return text;
        }
    }

    public sealed class LocalModelIdentity
    {
        public LocalModelIdentity(
            string manifestId,
            string modelId,
            string displayName,
            string modelVersion,
            Uri sourceUri,
            string sourceRevision,
            string licenseId,
            bool experimental,
            string experimentalReason)
        {
            ManifestId = LocalModelManifestValidation.RequireIdentifier(
                manifestId,
                nameof(manifestId));
            ModelId = LocalModelManifestValidation.RequireIdentifier(
                modelId,
                nameof(modelId));
            DisplayName = LocalModelManifestValidation.RequireBoundedText(
                displayName,
                nameof(displayName),
                128);
            ModelVersion = LocalModelManifestValidation.RequireBoundedText(
                modelVersion,
                nameof(modelVersion),
                128);
            if (sourceUri == null)
            {
                throw new ArgumentNullException(nameof(sourceUri));
            }
            if (!sourceUri.IsAbsoluteUri ||
                sourceUri.AbsoluteUri.Length > 2048 ||
                !string.Equals(
                    sourceUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(sourceUri.Host) ||
                !string.IsNullOrEmpty(sourceUri.UserInfo) ||
                !string.IsNullOrEmpty(sourceUri.Fragment))
            {
                throw new ArgumentException(
                    "Local-model provenance must use an absolute HTTPS URI without credentials or a fragment.",
                    nameof(sourceUri));
            }
            SourceUri = sourceUri;
            SourceRevision = LocalModelManifestValidation.RequireBoundedText(
                sourceRevision,
                nameof(sourceRevision),
                128);
            LicenseId = LocalModelManifestValidation.RequireBoundedText(
                licenseId,
                nameof(licenseId),
                128);
            Experimental = experimental;
            if (experimental)
            {
                ExperimentalReason = LocalModelManifestValidation.RequireBoundedText(
                    experimentalReason,
                    nameof(experimentalReason),
                    512);
            }
            else
            {
                if (experimentalReason == null)
                {
                    throw new ArgumentNullException(nameof(experimentalReason));
                }
                if (experimentalReason.Length != 0)
                {
                    throw new ArgumentException(
                        "A non-experimental manifest must use an empty experimental reason.",
                        nameof(experimentalReason));
                }
                ExperimentalReason = string.Empty;
            }
        }

        public string ManifestId { get; }

        public string ModelId { get; }

        public string DisplayName { get; }

        public string ModelVersion { get; }

        public Uri SourceUri { get; }

        public string SourceRevision { get; }

        public string LicenseId { get; }

        public bool Experimental { get; }

        public string ExperimentalReason { get; }
    }

    public sealed class LocalModelRuntimeRequirement
    {
        public LocalModelRuntimeRequirement(
            string runtimeId,
            int abiVersion,
            bool requiresNetworkAccess)
        {
            RuntimeId = LocalModelManifestValidation.RequireIdentifier(
                runtimeId,
                nameof(runtimeId));
            if (!string.Equals(RuntimeId, "reachy_llama", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "RMA-131 schema version 1 targets the first-party reachy_llama runtime only.",
                    nameof(runtimeId));
            }
            if (abiVersion != LocalModelManifestPolicy.ReachyLlamaAbiVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(abiVersion),
                    abiVersion,
                    "The local-model manifest requires a different reachy_llama ABI version.");
            }
            if (requiresNetworkAccess)
            {
                throw new ArgumentException(
                    "A local reachy_llama model cannot require network access for inference.",
                    nameof(requiresNetworkAccess));
            }
            AbiVersion = abiVersion;
            RequiresNetworkAccess = false;
        }

        public string RuntimeId { get; }

        public int AbiVersion { get; }

        public bool RequiresNetworkAccess { get; }
    }

    public sealed class LocalModelArtifact
    {
        public LocalModelArtifact(
            string relativePath,
            long fileSizeBytes,
            string sha256)
        {
            RelativePath = LocalModelManifestValidation.RequireSafeRelativeGgufPath(
                relativePath,
                nameof(relativePath));
            if (fileSizeBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
            }
            FileSizeBytes = fileSizeBytes;
            Sha256 = LocalModelManifestValidation.RequireSha256(
                sha256,
                nameof(sha256));
        }

        public string RelativePath { get; }

        public long FileSizeBytes { get; }

        public string Sha256 { get; }
    }

    public sealed class LocalModelGgufMetadata
    {
        public LocalModelGgufMetadata(
            int ggufVersion,
            string architecture,
            string quantization,
            long parameterCount,
            string tokenizerModel,
            string tokenizerPre)
        {
            if (ggufVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ggufVersion));
            }
            GgufVersion = ggufVersion;
            Architecture = LocalModelManifestValidation.RequireBoundedText(
                architecture,
                nameof(architecture),
                128);
            Quantization = LocalModelManifestValidation.RequireBoundedText(
                quantization,
                nameof(quantization),
                128);
            if (parameterCount <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(parameterCount));
            }
            ParameterCount = parameterCount;
            TokenizerModel = LocalModelManifestValidation.RequireBoundedText(
                tokenizerModel,
                nameof(tokenizerModel),
                128);
            TokenizerPre = LocalModelManifestValidation.RequireBoundedText(
                tokenizerPre,
                nameof(tokenizerPre),
                128);
        }

        public int GgufVersion { get; }

        public string Architecture { get; }

        public string Quantization { get; }

        public long ParameterCount { get; }

        public string TokenizerModel { get; }

        public string TokenizerPre { get; }
    }

    public sealed class LocalModelMemoryEstimate
    {
        public LocalModelMemoryEstimate(
            long peakRamBytes,
            int basisContextTokens,
            int basisBatchTokens)
        {
            if (peakRamBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(peakRamBytes));
            }
            if (basisContextTokens <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(basisContextTokens));
            }
            if (basisBatchTokens <= 0 || basisBatchTokens > basisContextTokens)
            {
                throw new ArgumentOutOfRangeException(nameof(basisBatchTokens));
            }
            PeakRamBytes = peakRamBytes;
            BasisContextTokens = basisContextTokens;
            BasisBatchTokens = basisBatchTokens;
        }

        public long PeakRamBytes { get; }

        public int BasisContextTokens { get; }

        public int BasisBatchTokens { get; }
    }

    public sealed class LocalModelInferenceProfile
    {
        private readonly ReadOnlyCollection<string> stopTokens;

        public LocalModelInferenceProfile(
            int contextLimitTokens,
            string chatTemplate,
            IReadOnlyList<string> stopTokens,
            LocalModelMemoryEstimate memoryEstimate,
            int recommendedThreads)
        {
            if (contextLimitTokens <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contextLimitTokens));
            }
            ContextLimitTokens = contextLimitTokens;
            ChatTemplate = LocalModelManifestValidation.RequireBoundedText(
                chatTemplate,
                nameof(chatTemplate),
                65536);
            if (stopTokens == null)
            {
                throw new ArgumentNullException(nameof(stopTokens));
            }
            if (stopTokens.Count > LocalModelManifestPolicy.MaximumStopTokenCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stopTokens),
                    "A local-model manifest cannot declare more than 32 stop tokens.");
            }
            var copy = new List<string>(stopTokens.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < stopTokens.Count; ++index)
            {
                string token = LocalModelManifestValidation.RequireBoundedText(
                    stopTokens[index],
                    nameof(stopTokens),
                    256);
                if (!unique.Add(token))
                {
                    throw new ArgumentException(
                        "Local-model stop tokens must be unique with ordinal comparison.",
                        nameof(stopTokens));
                }
                copy.Add(token);
            }
            this.stopTokens = copy.AsReadOnly();
            MemoryEstimate = memoryEstimate ??
                throw new ArgumentNullException(nameof(memoryEstimate));
            if (memoryEstimate.BasisContextTokens > contextLimitTokens)
            {
                throw new ArgumentException(
                    "The memory-estimate context cannot exceed the model context limit.",
                    nameof(memoryEstimate));
            }
            if (recommendedThreads <= 0 || recommendedThreads > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(recommendedThreads));
            }
            RecommendedThreads = recommendedThreads;
        }

        public int ContextLimitTokens { get; }

        public string ChatTemplate { get; }

        public IReadOnlyList<string> StopTokens => stopTokens;

        public LocalModelMemoryEstimate MemoryEstimate { get; }

        public int RecommendedThreads { get; }
    }

    public sealed class LocalModelDeviceCompatibility
    {
        private readonly ReadOnlyCollection<string> androidAbis;
        private readonly ReadOnlyCollection<string> requiredCpuFeatures;

        public LocalModelDeviceCompatibility(
            IReadOnlyList<string> androidAbis,
            int minimumAndroidApi,
            IReadOnlyList<string> requiredCpuFeatures,
            long minimumRamBytes,
            int reachyLlamaAbiVersion)
        {
            if (androidAbis == null)
            {
                throw new ArgumentNullException(nameof(androidAbis));
            }
            if (androidAbis.Count != 1 ||
                !string.Equals(
                    androidAbis[0],
                    "arm64-v8a",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "RMA-131 schema version 1 supports exactly the arm64-v8a Android ABI.",
                    nameof(androidAbis));
            }
            this.androidAbis = new List<string>(androidAbis).AsReadOnly();
            if (minimumAndroidApi < 26)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumAndroidApi));
            }
            MinimumAndroidApi = minimumAndroidApi;
            if (requiredCpuFeatures == null)
            {
                throw new ArgumentNullException(nameof(requiredCpuFeatures));
            }
            if (requiredCpuFeatures.Count > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredCpuFeatures),
                    "A local-model manifest cannot require more than 32 CPU features.");
            }
            var featureCopy = new List<string>(requiredCpuFeatures.Count);
            var featureSet = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < requiredCpuFeatures.Count; ++index)
            {
                string feature = LocalModelManifestValidation.RequireIdentifier(
                    requiredCpuFeatures[index],
                    nameof(requiredCpuFeatures));
                if (!featureSet.Add(feature))
                {
                    throw new ArgumentException(
                        "Required CPU features must be unique.",
                        nameof(requiredCpuFeatures));
                }
                featureCopy.Add(feature);
            }
            this.requiredCpuFeatures = featureCopy.AsReadOnly();
            if (minimumRamBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumRamBytes));
            }
            MinimumRamBytes = minimumRamBytes;
            if (reachyLlamaAbiVersion != LocalModelManifestPolicy.ReachyLlamaAbiVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(reachyLlamaAbiVersion));
            }
            ReachyLlamaAbiVersion = reachyLlamaAbiVersion;
        }

        public IReadOnlyList<string> AndroidAbis => androidAbis;

        public int MinimumAndroidApi { get; }

        public IReadOnlyList<string> RequiredCpuFeatures => requiredCpuFeatures;

        public long MinimumRamBytes { get; }

        public int ReachyLlamaAbiVersion { get; }
    }

    public sealed class LocalModelManifest
    {
        public LocalModelManifest(
            int schemaVersion,
            LocalModelIdentity identity,
            LocalModelRuntimeRequirement runtime,
            LocalModelArtifact artifact,
            LocalModelGgufMetadata ggufMetadata,
            LocalModelInferenceProfile inference,
            LocalModelDeviceCompatibility deviceCompatibility)
        {
            if (schemaVersion != LocalModelManifestPolicy.CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    schemaVersion,
                    "Unsupported local-model manifest schema version.");
            }
            SchemaVersion = schemaVersion;
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            GgufMetadata = ggufMetadata ??
                throw new ArgumentNullException(nameof(ggufMetadata));
            Inference = inference ?? throw new ArgumentNullException(nameof(inference));
            DeviceCompatibility = deviceCompatibility ??
                throw new ArgumentNullException(nameof(deviceCompatibility));
            if (deviceCompatibility.ReachyLlamaAbiVersion != runtime.AbiVersion)
            {
                throw new ArgumentException(
                    "Runtime and device-compatibility ABI requirements must match exactly.",
                    nameof(deviceCompatibility));
            }
            if (deviceCompatibility.MinimumRamBytes < inference.MemoryEstimate.PeakRamBytes)
            {
                throw new ArgumentException(
                    "The device minimum RAM cannot be smaller than the declared peak-RAM estimate.",
                    nameof(deviceCompatibility));
            }
        }

        public int SchemaVersion { get; }

        public LocalModelIdentity Identity { get; }

        public LocalModelRuntimeRequirement Runtime { get; }

        public LocalModelArtifact Artifact { get; }

        public LocalModelGgufMetadata GgufMetadata { get; }

        public LocalModelInferenceProfile Inference { get; }

        public LocalModelDeviceCompatibility DeviceCompatibility { get; }
    }

    public sealed class LocalModelManifestCatalog
    {
        private readonly ReadOnlyCollection<LocalModelManifest> manifests;
        private readonly Dictionary<string, LocalModelManifest> byModelId;

        public LocalModelManifestCatalog(IReadOnlyList<LocalModelManifest> manifests)
        {
            if (manifests == null)
            {
                throw new ArgumentNullException(nameof(manifests));
            }
            if (manifests.Count > LocalModelManifestPolicy.MaximumCatalogCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(manifests),
                    "A local-model catalog cannot contain more than 256 manifests.");
            }

            var copy = new List<LocalModelManifest>(manifests.Count);
            var manifestIds = new HashSet<string>(StringComparer.Ordinal);
            byModelId = new Dictionary<string, LocalModelManifest>(StringComparer.Ordinal);
            for (int index = 0; index < manifests.Count; ++index)
            {
                LocalModelManifest manifest = manifests[index] ??
                    throw new ArgumentException(
                        "Local-model catalogs cannot contain null manifests.",
                        nameof(manifests));
                if (!manifestIds.Add(manifest.Identity.ManifestId))
                {
                    throw new ArgumentException(
                        "Local-model manifest IDs must be unique.",
                        nameof(manifests));
                }
                if (byModelId.ContainsKey(manifest.Identity.ModelId))
                {
                    throw new ArgumentException(
                        "Local-model model IDs must be unique.",
                        nameof(manifests));
                }
                byModelId.Add(manifest.Identity.ModelId, manifest);
                copy.Add(manifest);
            }
            this.manifests = copy.AsReadOnly();
        }

        public IReadOnlyList<LocalModelManifest> Manifests => manifests;

        public bool TryGetByModelId(string modelId, out LocalModelManifest? manifest)
        {
            string id = LocalModelManifestValidation.RequireIdentifier(
                modelId,
                nameof(modelId));
            return byModelId.TryGetValue(id, out manifest);
        }

        public LocalModelManifest GetRequiredByModelId(string modelId)
        {
            if (!TryGetByModelId(modelId, out LocalModelManifest? manifest) || manifest == null)
            {
                throw new KeyNotFoundException(
                    $"Local-model catalog does not contain exact model ID '{modelId}'.");
            }
            return manifest;
        }
    }
}

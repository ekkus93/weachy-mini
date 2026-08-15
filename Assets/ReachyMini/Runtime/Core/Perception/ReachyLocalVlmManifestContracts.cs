#nullable enable

using System;
using ReachyMini.Security;

namespace ReachyMini.Perception
{
    public sealed class LocalVlmModelIdentity
    {
        public LocalVlmModelIdentity(
            string manifestId,
            string modelId,
            string displayName,
            string modelVersion,
            Uri sourceUri,
            string sourceRevision,
            string licenseId)
        {
            ManifestId = RequireIdentifier(manifestId, nameof(manifestId));
            ModelId = RequireIdentifier(modelId, nameof(modelId));
            DisplayName = RequireBoundedText(
                displayName,
                nameof(displayName),
                128);
            ModelVersion = RequireBoundedText(
                modelVersion,
                nameof(modelVersion),
                128);
            SourceUri = RequireHttpsUri(sourceUri, nameof(sourceUri));
            SourceRevision = RequireBoundedText(
                sourceRevision,
                nameof(sourceRevision),
                128);
            LicenseId = RequireBoundedText(
                licenseId,
                nameof(licenseId),
                128);
        }

        public string ManifestId { get; }

        public string ModelId { get; }

        public string DisplayName { get; }

        public string ModelVersion { get; }

        public Uri SourceUri { get; }

        public string SourceRevision { get; }

        public string LicenseId { get; }

        internal static string RequireBoundedText(
            string value,
            string name,
            int maximumLength)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Local VLM manifest text exceeds its bounded length.");
            }
            return text;
        }

        internal static string RequireIdentifier(string value, string name)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > 128)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Local VLM identifiers cannot exceed 128 characters.");
            }

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
                        "Local VLM identifiers must use lowercase ASCII letters, digits, '.', '_' or '-', and start with a letter or digit.",
                        name);
                }
            }

            return text;
        }

        private static Uri RequireHttpsUri(Uri value, string name)
        {
            ReachyNetworkEndpointSecurity.RequirePublicHttpsUri(value, name);
            return value;
        }
    }

    public sealed class LocalVlmRuntimeRequirement
    {
        public LocalVlmRuntimeRequirement(
            string runtimeId,
            string runtimeVersion,
            string architecture,
            string quantization,
            long parameterCount,
            bool requiresNetworkAccess)
        {
            RuntimeId = LocalVlmModelIdentity.RequireIdentifier(
                runtimeId,
                nameof(runtimeId));
            RuntimeVersion = LocalVlmModelIdentity.RequireBoundedText(
                runtimeVersion,
                nameof(runtimeVersion),
                128);
            Architecture = LocalVlmModelIdentity.RequireBoundedText(
                architecture,
                nameof(architecture),
                128);
            Quantization = LocalVlmModelIdentity.RequireBoundedText(
                quantization,
                nameof(quantization),
                128);
            if (parameterCount <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(parameterCount));
            }
            if (requiresNetworkAccess)
            {
                throw new ArgumentException(
                    "A local VLM runtime cannot require network access.",
                    nameof(requiresNetworkAccess));
            }

            ParameterCount = parameterCount;
            RequiresNetworkAccess = false;
        }

        public string RuntimeId { get; }

        public string RuntimeVersion { get; }

        public string Architecture { get; }

        public string Quantization { get; }

        public long ParameterCount { get; }

        public bool RequiresNetworkAccess { get; }
    }

    public sealed class LocalVlmModelLimits
    {
        public LocalVlmModelLimits(
            int contextWindowTokens,
            int maximumOutputTokens,
            int maximumPromptCharacters,
            int maximumImageWidth,
            int maximumImageHeight,
            long minimumRamBytes,
            long minimumStorageBytes)
        {
            if (contextWindowTokens <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contextWindowTokens));
            }
            if (maximumOutputTokens <= 0 ||
                maximumOutputTokens > contextWindowTokens)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutputTokens));
            }
            if (maximumPromptCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPromptCharacters));
            }
            if (maximumImageWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumImageWidth));
            }
            if (maximumImageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumImageHeight));
            }
            if (minimumRamBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumRamBytes));
            }
            if (minimumStorageBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumStorageBytes));
            }

            ContextWindowTokens = contextWindowTokens;
            MaximumOutputTokens = maximumOutputTokens;
            MaximumPromptCharacters = maximumPromptCharacters;
            MaximumImageWidth = maximumImageWidth;
            MaximumImageHeight = maximumImageHeight;
            MinimumRamBytes = minimumRamBytes;
            MinimumStorageBytes = minimumStorageBytes;
        }

        public int ContextWindowTokens { get; }

        public int MaximumOutputTokens { get; }

        public int MaximumPromptCharacters { get; }

        public int MaximumImageWidth { get; }

        public int MaximumImageHeight { get; }

        public long MinimumRamBytes { get; }

        public long MinimumStorageBytes { get; }
    }

    public sealed class LocalVlmDistribution
    {
        public LocalVlmDistribution(
            LocalVlmArtifactSource artifactSource,
            bool requiredForFirstRelease,
            bool automaticDownloadAllowed)
        {
            if (!Enum.IsDefined(
                    typeof(LocalVlmArtifactSource),
                    artifactSource))
            {
                throw new ArgumentOutOfRangeException(nameof(artifactSource));
            }
            if (requiredForFirstRelease)
            {
                throw new ArgumentException(
                    "RMA-114 local VLMs must remain optional for the first release.",
                    nameof(requiredForFirstRelease));
            }
            if (automaticDownloadAllowed)
            {
                throw new ArgumentException(
                    "RMA-114 does not permit automatic local-model downloads.",
                    nameof(automaticDownloadAllowed));
            }

            ArtifactSource = artifactSource;
            RequiredForFirstRelease = false;
            AutomaticDownloadAllowed = false;
        }

        public LocalVlmArtifactSource ArtifactSource { get; }

        public bool RequiredForFirstRelease { get; }

        public bool AutomaticDownloadAllowed { get; }
    }

    public sealed class LocalVlmSemanticCapabilities
    {
        public LocalVlmSemanticCapabilities(
            bool supportsVisualQuestions,
            bool supportsSceneDescription,
            bool supportsCancellation,
            int maximumConcurrentOperations)
        {
            if (!supportsVisualQuestions && !supportsSceneDescription)
            {
                throw new ArgumentException(
                    "A local VLM manifest must declare at least one semantic capability.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "A local VLM adapter must support cancellation.",
                    nameof(supportsCancellation));
            }
            if (maximumConcurrentOperations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }

            SupportsVisualQuestions = supportsVisualQuestions;
            SupportsSceneDescription = supportsSceneDescription;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentOperations = maximumConcurrentOperations;
        }

        public bool SupportsVisualQuestions { get; }

        public bool SupportsSceneDescription { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentOperations { get; }
    }

}

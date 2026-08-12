#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Perception
{
    public sealed class LocalVlmArtifactDescriptor
    {
        public LocalVlmArtifactDescriptor(
            string relativePath,
            string sha256,
            long sizeBytes)
        {
            RelativePath = RequireSafeRelativePath(
                relativePath,
                nameof(relativePath));
            Sha256 = RequireSha256(sha256, nameof(sha256));
            if (sizeBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            }
            SizeBytes = sizeBytes;
        }

        public string RelativePath { get; }

        public string Sha256 { get; }

        public long SizeBytes { get; }

        private static string RequireSafeRelativePath(
            string value,
            string name)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > 240 ||
                text[0] == '/' ||
                text[0] == '\\' ||
                text.Contains('\\') ||
                text.Contains(':'))
            {
                throw new ArgumentException(
                    "Local VLM artifact paths must be short, slash-separated relative paths.",
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
                        "Local VLM artifact paths cannot contain empty, '.' or '..' segments.",
                        name);
                }
            }
            return text;
        }

        private static string RequireSha256(string value, string name)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length != 64)
            {
                throw new ArgumentException(
                    "Local VLM artifact SHA-256 values must contain exactly 64 lowercase hexadecimal characters.",
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
                        "Local VLM artifact SHA-256 values must contain exactly 64 lowercase hexadecimal characters.",
                        name);
                }
            }
            return text;
        }
    }

    public sealed class LocalVlmModelManifest
    {
        public const int CurrentSchemaVersion = 1;

        public const int MaximumArtifactCount = 64;

        private readonly ReadOnlyCollection<LocalVlmArtifactDescriptor> artifacts;

        public LocalVlmModelManifest(
            int schemaVersion,
            LocalVlmModelIdentity identity,
            LocalVlmRuntimeRequirement runtime,
            LocalVlmModelLimits limits,
            LocalVlmDistribution distribution,
            LocalVlmSemanticCapabilities capabilities,
            IReadOnlyList<LocalVlmArtifactDescriptor> artifacts)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    schemaVersion,
                    "Unsupported local VLM manifest schema version.");
            }
            SchemaVersion = schemaVersion;
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            Distribution = distribution ??
                throw new ArgumentNullException(nameof(distribution));
            Capabilities = capabilities ??
                throw new ArgumentNullException(nameof(capabilities));
            if (artifacts == null)
            {
                throw new ArgumentNullException(nameof(artifacts));
            }
            if (artifacts.Count == 0)
            {
                throw new ArgumentException(
                    "A local VLM manifest must declare at least one artifact.",
                    nameof(artifacts));
            }
            if (artifacts.Count > MaximumArtifactCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(artifacts),
                    "A local VLM manifest cannot declare more than 64 artifacts.");
            }

            var copy = new List<LocalVlmArtifactDescriptor>(artifacts.Count);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            long totalSizeBytes = 0L;
            for (int index = 0; index < artifacts.Count; ++index)
            {
                LocalVlmArtifactDescriptor artifact = artifacts[index] ??
                    throw new ArgumentException(
                        "Local VLM artifact lists cannot contain null entries.",
                        nameof(artifacts));
                if (!paths.Add(artifact.RelativePath))
                {
                    throw new ArgumentException(
                        "Local VLM artifact paths must be unique.",
                        nameof(artifacts));
                }
                totalSizeBytes = checked(totalSizeBytes + artifact.SizeBytes);
                copy.Add(artifact);
            }
            if (limits.MinimumStorageBytes < totalSizeBytes)
            {
                throw new ArgumentException(
                    "The local VLM storage estimate cannot be smaller than the declared artifact bytes.",
                    nameof(limits));
            }

            this.artifacts = copy.AsReadOnly();
            TotalArtifactBytes = totalSizeBytes;
        }

        public int SchemaVersion { get; }

        public LocalVlmModelIdentity Identity { get; }

        public LocalVlmRuntimeRequirement Runtime { get; }

        public LocalVlmModelLimits Limits { get; }

        public LocalVlmDistribution Distribution { get; }

        public LocalVlmSemanticCapabilities Capabilities { get; }

        public IReadOnlyList<LocalVlmArtifactDescriptor> Artifacts => artifacts;

        public long TotalArtifactBytes { get; }

        public VisionLanguageCapabilities CreateVisionLanguageCapabilities()
        {
            return new VisionLanguageCapabilities(
                Capabilities.SupportsVisualQuestions,
                Capabilities.SupportsSceneDescription,
                Capabilities.SupportsCancellation,
                Capabilities.MaximumConcurrentOperations,
                Limits.MaximumPromptCharacters);
        }
    }
}

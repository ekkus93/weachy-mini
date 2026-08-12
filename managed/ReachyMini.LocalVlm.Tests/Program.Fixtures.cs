#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static LocalVlmModelManifest Manifest(
            int schemaVersion = LocalVlmModelManifest.CurrentSchemaVersion,
            LocalVlmModelIdentity? identity = null,
            LocalVlmRuntimeRequirement? runtime = null,
            LocalVlmModelLimits? limits = null,
            LocalVlmDistribution? distribution = null,
            LocalVlmSemanticCapabilities? capabilities = null,
            IReadOnlyList<LocalVlmArtifactDescriptor>? artifacts = null)
        {
            return new LocalVlmModelManifest(
                schemaVersion,
                identity ?? Identity(),
                runtime ?? Runtime(),
                limits ?? Limits(),
                distribution ?? Distribution(),
                capabilities ?? Capabilities(),
                artifacts ?? new[]
                {
                    Artifact("weights/model.gguf", sizeBytes: 200L),
                    Artifact(
                        "tokenizer/tokenizer.json",
                        new string('b', 64),
                        100L),
                });
        }

        private static LocalVlmModelIdentity Identity(
            string manifestId = "example-local-vlm",
            string modelId = "example-local-vlm")
        {
            return new LocalVlmModelIdentity(
                manifestId,
                modelId,
                "Example local VLM",
                "1.0.0",
                new Uri("https://example.com/models/example-local-vlm", UriKind.Absolute),
                "0123456789abcdef",
                "Apache-2.0");
        }

        private static LocalVlmRuntimeRequirement Runtime(
            long parameterCount = 500_000_000L,
            bool requiresNetworkAccess = false)
        {
            return new LocalVlmRuntimeRequirement(
                "example-runtime",
                "1.0.0",
                "example-vlm",
                "q4",
                parameterCount,
                requiresNetworkAccess);
        }

        private static LocalVlmModelLimits Limits(
            int contextWindowTokens = 4096,
            int maximumOutputTokens = 512,
            int maximumPromptCharacters = 4096,
            int maximumImageWidth = 1024,
            int maximumImageHeight = 1024,
            long minimumRamBytes = 1_000_000L,
            long minimumStorageBytes = 300L)
        {
            return new LocalVlmModelLimits(
                contextWindowTokens,
                maximumOutputTokens,
                maximumPromptCharacters,
                maximumImageWidth,
                maximumImageHeight,
                minimumRamBytes,
                minimumStorageBytes);
        }

        private static LocalVlmDistribution Distribution(
            bool requiredForFirstRelease = false,
            bool automaticDownloadAllowed = false)
        {
            return new LocalVlmDistribution(
                LocalVlmArtifactSource.UserProvided,
                requiredForFirstRelease,
                automaticDownloadAllowed);
        }

        private static LocalVlmSemanticCapabilities Capabilities(
            bool supportsVisualQuestions = true,
            bool supportsSceneDescription = true,
            bool supportsCancellation = true,
            int maximumConcurrentOperations = 1)
        {
            return new LocalVlmSemanticCapabilities(
                supportsVisualQuestions,
                supportsSceneDescription,
                supportsCancellation,
                maximumConcurrentOperations);
        }

        private static LocalVlmArtifactDescriptor Artifact(
            string relativePath,
            string? sha256 = null,
            long sizeBytes = 100L)
        {
            return new LocalVlmArtifactDescriptor(
                relativePath,
                sha256 ?? new string('a', 64),
                sizeBytes);
        }

        private static LocalVlmProviderConfiguration Configuration(
            string localArtifactRoot = "/models/example")
        {
            return new LocalVlmProviderConfiguration(
                Manifest(),
                localArtifactRoot,
                "provider-instance",
                artifactIntegrityVerified: true);
        }

        private static FakeProvider Provider(
            LocalVlmProviderConfiguration configuration,
            VisionProviderLocation location,
            string? providerId = null,
            int? maximumPromptCharacters = null)
        {
            LocalVlmModelManifest manifest = configuration.Manifest;
            return new FakeProvider(
                new ProviderDescriptor(
                    VisionProviderKind.SemanticVisionLanguage,
                    providerId ?? manifest.Identity.ModelId,
                    configuration.ProviderInstanceId,
                    manifest.Identity.DisplayName,
                    manifest.Identity.ModelVersion,
                    location),
                new VisionLanguageCapabilities(
                    manifest.Capabilities.SupportsVisualQuestions,
                    manifest.Capabilities.SupportsSceneDescription,
                    manifest.Capabilities.SupportsCancellation,
                    manifest.Capabilities.MaximumConcurrentOperations,
                    maximumPromptCharacters ??
                        manifest.Limits.MaximumPromptCharacters));
        }

        private static JsonDocument LoadSchema()
        {
            return JsonDocument.Parse(File.ReadAllText(
                Path.Combine(
                    RepoRoot(),
                    "models",
                    "manifests",
                    "local-vlm-manifest.schema.json")));
        }

        private static string RepoRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "models")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }
}

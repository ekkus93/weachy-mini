#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void ReleasePolicyIsOptionalAndFailClosed()
        {
            False(LocalVlmReleasePolicy.RequiredForFirstRelease, "first-release requirement");
            False(LocalVlmReleasePolicy.AutomaticModelDownloadEnabled, "automatic download");
            False(LocalVlmReleasePolicy.AutomaticProviderFallbackEnabled, "automatic fallback");
            False(LocalVlmReleasePolicy.CandidateBenchmarkingEnabled, "premature benchmarking");
        }

        private static void ValidManifestPublishesExactCapabilities()
        {
            LocalVlmModelManifest manifest = Manifest();
            Equal(LocalVlmModelManifest.CurrentSchemaVersion, manifest.SchemaVersion, "schema version");
            Equal("example-local-vlm", manifest.Identity.ManifestId, "manifest id");
            Equal(300L, manifest.TotalArtifactBytes, "artifact total");
            Equal(2, manifest.Artifacts.Count, "artifact count");
            False(manifest.Runtime.RequiresNetworkAccess, "runtime network");
            False(manifest.Distribution.RequiredForFirstRelease, "manifest optional");
            False(manifest.Distribution.AutomaticDownloadAllowed, "manifest download");

            VisionLanguageCapabilities capabilities =
                manifest.CreateVisionLanguageCapabilities();
            True(capabilities.SupportsVisualQuestions, "visual questions");
            True(capabilities.SupportsSceneDescription, "scene description");
            True(capabilities.SupportsCancellation, "provider cancellation");
            Equal(1, capabilities.MaximumConcurrentOperations, "provider concurrency");
            Equal(4096, capabilities.MaximumPromptCharacters, "prompt limit");
        }

        private static void ManifestRejectsUnsupportedSchema()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Manifest(schemaVersion: 2),
                "unsupported schema");
        }
    }
}

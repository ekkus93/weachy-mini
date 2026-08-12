#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void ArtifactRejectsTraversal()
        {
            Throws<ArgumentException>(
                () => Artifact("../model.gguf"),
                "leading traversal");
            Throws<ArgumentException>(
                () => Artifact("weights/../model.gguf"),
                "nested traversal");
            Throws<ArgumentException>(
                () => Artifact("weights//model.gguf"),
                "empty segment");
        }

        private static void ArtifactRejectsBackslashesAndSchemes()
        {
            Throws<ArgumentException>(
                () => Artifact("weights\\model.gguf"),
                "backslash path");
            Throws<ArgumentException>(
                () => Artifact("https://example.com/model.gguf"),
                "network artifact path");
            Throws<ArgumentException>(
                () => Artifact("C:/model.gguf"),
                "drive artifact path");
        }

        private static void ArtifactRejectsUppercaseOrShortHashes()
        {
            Throws<ArgumentException>(
                () => Artifact("model.gguf", new string('A', 64)),
                "uppercase hash");
            Throws<ArgumentException>(
                () => Artifact("model.gguf", "abcd"),
                "short hash");
        }

        private static void ArtifactRejectsZeroLength()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Artifact("model.gguf", sizeBytes: 0L),
                "zero artifact size");
        }

        private static void ManifestRequiresArtifacts()
        {
            Throws<ArgumentException>(
                () => Manifest(artifacts: Array.Empty<LocalVlmArtifactDescriptor>()),
                "empty artifacts");
        }

        private static void ManifestRejectsDuplicateArtifacts()
        {
            LocalVlmArtifactDescriptor artifact = Artifact("model.gguf");
            Throws<ArgumentException>(
                () => Manifest(artifacts: new[] { artifact, artifact }),
                "duplicate artifact");
        }

        private static void ManifestRejectsNullArtifacts()
        {
            Throws<ArgumentException>(
                () => Manifest(
                    artifacts: new LocalVlmArtifactDescriptor[] { null! }),
                "null artifact");
        }


        private static void ManifestRejectsTooManyArtifacts()
        {
            var artifacts = new List<LocalVlmArtifactDescriptor>();
            for (int index = 0; index <= LocalVlmModelManifest.MaximumArtifactCount; ++index)
            {
                artifacts.Add(Artifact(
                    "weights/model-" + index + ".bin",
                    sizeBytes: 1L));
            }
            Throws<ArgumentOutOfRangeException>(
                () => Manifest(
                    limits: Limits(minimumStorageBytes: artifacts.Count),
                    artifacts: artifacts),
                "too many artifacts");
        }

        private static void ManifestRejectsUnderstatedStorage()
        {
            Throws<ArgumentException>(
                () => Manifest(limits: Limits(minimumStorageBytes: 299L)),
                "understated storage");
        }

        private static void ManifestCopiesArtifactLists()
        {
            var artifacts = new List<LocalVlmArtifactDescriptor>
            {
                Artifact("weights/model.gguf", sizeBytes: 100L),
            };
            LocalVlmModelManifest manifest = Manifest(
                limits: Limits(minimumStorageBytes: 100L),
                artifacts: artifacts);
            artifacts.Add(Artifact("tokenizer.json", sizeBytes: 20L));
            Equal(1, manifest.Artifacts.Count, "immutable artifact copy");
            Equal(100L, manifest.TotalArtifactBytes, "immutable artifact total");
        }
    }
}

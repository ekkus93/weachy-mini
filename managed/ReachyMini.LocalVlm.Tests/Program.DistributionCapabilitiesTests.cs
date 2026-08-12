#nullable enable

using System;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void DistributionRejectsFirstReleaseRequirement()
        {
            Throws<ArgumentException>(
                () => Distribution(requiredForFirstRelease: true),
                "required local model");
        }

        private static void DistributionRejectsAutomaticDownloads()
        {
            Throws<ArgumentException>(
                () => Distribution(automaticDownloadAllowed: true),
                "automatic download");
        }

        private static void SemanticCapabilitiesRequireAFeature()
        {
            Throws<ArgumentException>(
                () => Capabilities(
                    supportsVisualQuestions: false,
                    supportsSceneDescription: false),
                "no semantic feature");
        }

        private static void SemanticCapabilitiesRequireCancellation()
        {
            Throws<ArgumentException>(
                () => Capabilities(supportsCancellation: false),
                "no cancellation");
            Throws<ArgumentOutOfRangeException>(
                () => Capabilities(maximumConcurrentOperations: 0),
                "zero concurrency");
        }
    }
}

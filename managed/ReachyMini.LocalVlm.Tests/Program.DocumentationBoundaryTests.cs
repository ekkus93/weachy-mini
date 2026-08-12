#nullable enable

using System;
using System.IO;
using System.Linq;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void ManifestDirectoryContainsNoModelPayloads()
        {
            string directory = Path.Combine(RepoRoot(), "models", "manifests");
            string[] forbiddenExtensions =
            {
                ".gguf",
                ".onnx",
                ".safetensors",
                ".bin",
                ".pt",
                ".pth",
                ".tflite",
            };
            string[] forbidden = Directory.EnumerateFiles(
                    directory,
                    "*",
                    SearchOption.AllDirectories)
                .Where(path => forbiddenExtensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            Equal(0, forbidden.Length, "manifest directory payloads");
        }

        private static void DocumentationDefersBenchmarkingAndDownloads()
        {
            string readme = File.ReadAllText(
                Path.Combine(RepoRoot(), "models", "manifests", "README.md"));
            Contains("optional", readme, "manifest optional documentation");
            Contains("automatic download", readme, "download documentation");
            Contains("benchmark", readme, "benchmark documentation");
            Contains("RMA-114", readme, "milestone documentation");
        }

        private static void SourceContractContainsNoDownloadOrFallbackExecution()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    RepoRoot(),
                    "Assets",
                    "ReachyMini",
                    "Runtime",
                    "Core",
                    "Perception",
                    "ReachyLocalVisionLanguageContracts.cs"));
            Contains(
                "AutomaticModelDownloadEnabled => false",
                source,
                "source download policy");
            Contains(
                "AutomaticProviderFallbackEnabled => false",
                source,
                "source fallback policy");
            Contains(
                "No local VLM runtime or model is installed; no fallback or download was attempted.",
                source,
                "stub fail-closed diagnostic");
            False(source.Contains("HttpClient", StringComparison.Ordinal), "no HTTP client");
            False(source.Contains("WebRequest", StringComparison.Ordinal), "no web request");
            False(source.Contains("Process.Start", StringComparison.Ordinal), "no subprocess fallback");
        }
    }
}

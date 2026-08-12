#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static void SourceAndDocumentationDeclareFailClosedBoundary()
        {
            string root = RepoRoot();
            // ReachyOpenAiVisionLanguageAdapters.cs was split
            // (docs/LARGE_FILE_REFACTOR_TODO.md, file #5) into
            // ReachyOpenAiVision*.cs and ReachyRemoteVlm*.cs files. Concatenate every
            // split-out piece so this contract check keeps covering the same source,
            // regardless of which file each token now lives in.
            string perceptionDirectory = Path.Combine(
                root, "Assets", "ReachyMini", "Runtime", "Core", "Perception");
            var sourceFiles = new List<string>();
            sourceFiles.AddRange(Directory.GetFiles(perceptionDirectory, "ReachyOpenAiVision*.cs"));
            sourceFiles.AddRange(Directory.GetFiles(perceptionDirectory, "ReachyRemoteVlm*.cs"));
            var sourceBuilder = new System.Text.StringBuilder();
            foreach (string sourceFile in sourceFiles)
            {
                sourceBuilder.Append(File.ReadAllText(sourceFile));
            }
            string source = sourceBuilder.ToString();
            string architecture = File.ReadAllText(Path.Combine(
                root,
                "docs",
                "architecture",
                "OPENAI_COMPATIBLE_VLM_ADAPTERS.md"));

            Contains(
                "AutomaticProviderFallbackEnabled => false",
                source,
                "automatic fallback disabled");
            Contains(
                "AutomaticRetryEnabled => false",
                source,
                "automatic retry disabled");
            Contains("public bool StoreResponse { get; }", source, "response storage disabled");
            Contains("public bool Stream { get; }", source, "streaming disabled");
            Contains(
                "VisionFrameOrigin.TransformedReachyEye",
                source,
                "transformed-frame boundary");
            Contains(
                "No world-model entity history",
                source,
                "stale entity exclusion");
            False(source.Contains("HttpClient", StringComparison.Ordinal), "no HTTP client");
            False(source.Contains("WebRequest", StringComparison.Ordinal), "no web request");
            False(source.Contains("Process.Start", StringComparison.Ordinal), "no subprocess");

            Contains("Responses", architecture, "Responses documentation");
            Contains("Chat Completions", architecture, "Chat documentation");
            Contains("validity mask", architecture, "validity documentation");
            Contains("coverage", architecture, "coverage documentation");
            Contains("fallback", architecture, "fallback documentation");
            Contains("stale", architecture, "stale evidence documentation");
        }

        private static string RepoRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "docs")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Unable to locate repository root.");
        }
    }
}

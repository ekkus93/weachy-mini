#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.LocalModels;

namespace ReachyMini.LocalModels.Tests
{
    internal static class Program
    {
        private const string ValidSha =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static readonly string[] Arm64AndroidAbis = { "arm64-v8a" };
        private static readonly string[] X64AndroidAbis = { "x86_64" };
        private static readonly string[] DuplicateCpuFeatures = { "dotprod", "dotprod" };
        private static readonly string[] DuplicateStopTokens = { "same", "same" };
        private static readonly string[] SyntheticStopTokens = { "<|synthetic_eos|>" };
        private static readonly string[] UnsafeArtifactPaths =
        {
            "/absolute/model.gguf",
            "../escape.gguf",
            "nested/../escape.gguf",
            "nested\\model.gguf",
            "C:/model.gguf",
            "model.GGUF",
        };

        private static int checks;

        private static int Main()
        {
            ValidManifestPreservesAllRequiredMetadata();
            SchemaAndRuntimeMismatchesFailClosed();
            IdentityAndExperimentalStateAreStrict();
            ArtifactMetadataRejectsUnsafeOrUnverifiableValues();
            GgufMetadataAndInferenceBoundsAreStrict();
            MemoryAndDeviceCompatibilityMustAgree();
            CatalogUsesExactDataDrivenIdentityWithoutFallback();

            Console.WriteLine(
                $"RMA-131 local-model managed contracts passed ({checks} checks).");
            return 0;
        }

        private static void ValidManifestPreservesAllRequiredMetadata()
        {
            LocalModelManifest manifest = CreateManifest(
                manifestId: "manifest.alpha",
                modelId: "model.alpha");

            Equal(1, manifest.SchemaVersion, "schema version");
            Equal("manifest.alpha", manifest.Identity.ManifestId, "manifest ID");
            Equal("model.alpha", manifest.Identity.ModelId, "model ID");
            Equal("Synthetic Model", manifest.Identity.DisplayName, "display name");
            Equal("synthetic-v1", manifest.Identity.ModelVersion, "model version");
            Equal("synthetic-revision", manifest.Identity.SourceRevision, "source revision");
            Equal("LicenseRef-Synthetic", manifest.Identity.LicenseId, "license ID");
            True(manifest.Identity.Experimental, "experimental marker");
            Contains(manifest.Identity.ExperimentalReason, "Synthetic", "experimental reason");
            Equal("reachy_llama", manifest.Runtime.RuntimeId, "runtime ID");
            Equal(2, manifest.Runtime.AbiVersion, "runtime ABI");
            False(manifest.Runtime.RequiresNetworkAccess, "local runtime network requirement");
            Equal("models/synthetic.gguf", manifest.Artifact.RelativePath, "artifact path");
            Equal(123456789L, manifest.Artifact.FileSizeBytes, "file size");
            Equal(ValidSha, manifest.Artifact.Sha256, "file SHA-256");
            Equal(3, manifest.GgufMetadata.GgufVersion, "GGUF version");
            Equal("synthetic-decoder", manifest.GgufMetadata.Architecture, "architecture");
            Equal("SYNTHETIC_Q4", manifest.GgufMetadata.Quantization, "quantization");
            Equal(600000000L, manifest.GgufMetadata.ParameterCount, "parameter count");
            Equal("synthetic-tokenizer", manifest.GgufMetadata.TokenizerModel, "tokenizer model");
            Equal("synthetic-pre", manifest.GgufMetadata.TokenizerPre, "tokenizer pre");
            Equal(4096, manifest.Inference.ContextLimitTokens, "context limit");
            Equal("{{ messages }}", manifest.Inference.ChatTemplate, "chat template");
            Equal(1, manifest.Inference.StopTokens.Count, "stop-token count");
            Equal("<|synthetic_eos|>", manifest.Inference.StopTokens[0], "stop token");
            Equal(1073741824L, manifest.Inference.MemoryEstimate.PeakRamBytes, "peak RAM");
            Equal(4096, manifest.Inference.MemoryEstimate.BasisContextTokens, "memory context");
            Equal(512, manifest.Inference.MemoryEstimate.BasisBatchTokens, "memory batch");
            Equal(4, manifest.Inference.RecommendedThreads, "recommended threads");
            Equal(1, manifest.DeviceCompatibility.AndroidAbis.Count, "Android ABI count");
            Equal("arm64-v8a", manifest.DeviceCompatibility.AndroidAbis[0], "Android ABI");
            Equal(26, manifest.DeviceCompatibility.MinimumAndroidApi, "minimum API");
            Equal(0, manifest.DeviceCompatibility.RequiredCpuFeatures.Count, "CPU feature count");
            Equal(2147483648L, manifest.DeviceCompatibility.MinimumRamBytes, "minimum RAM");
            Equal(2, manifest.DeviceCompatibility.ReachyLlamaAbiVersion, "compatibility ABI");
        }

        private static void SchemaAndRuntimeMismatchesFailClosed()
        {
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelManifest(
                    2,
                    CreateIdentity("manifest.alpha", "model.alpha"),
                    CreateRuntime(),
                    CreateArtifact(),
                    CreateGguf(),
                    CreateInference(),
                    CreateCompatibility()),
                "unknown schema version");
            Throws<ArgumentException>(
                () => new LocalModelRuntimeRequirement("other_runtime", 2, false),
                "wrong runtime ID");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelRuntimeRequirement("reachy_llama", 1, false),
                "wrong runtime ABI");
            Throws<ArgumentException>(
                () => new LocalModelRuntimeRequirement("reachy_llama", 2, true),
                "network-required local runtime");
        }

        private static void IdentityAndExperimentalStateAreStrict()
        {
            Throws<ArgumentException>(
                () => CreateIdentity("bad/manifest", "model.alpha"),
                "unsafe manifest ID");
            Throws<ArgumentException>(
                () => CreateIdentity("manifest.alpha", "Model.Alpha"),
                "uppercase model ID");
            Throws<ArgumentException>(
                () => CreateIdentityWithUri("http://example.invalid/model", true, "Synthetic."),
                "non-HTTPS provenance");
            Throws<ArgumentException>(
                () => CreateIdentityWithUri(
                    "https://user:secret@example.invalid/model",
                    true,
                    "Synthetic."),
                "credentialed provenance");
            Throws<ArgumentException>(
                () => CreateIdentityWithUri(
                    "https://example.invalid/model#fragment",
                    true,
                    "Synthetic."),
                "fragment provenance");
            Throws<ArgumentException>(
                () => CreateIdentityWithUri(
                    "https://example.invalid/model",
                    true,
                    string.Empty),
                "experimental reason required");
            Throws<ArgumentException>(
                () => CreateIdentityWithUri(
                    "https://example.invalid/model",
                    false,
                    "contradictory reason"),
                "non-experimental contradiction");
        }

        private static void ArtifactMetadataRejectsUnsafeOrUnverifiableValues()
        {
            for (int index = 0; index < UnsafeArtifactPaths.Length; ++index)
            {
                string path = UnsafeArtifactPaths[index];
                Throws<ArgumentException>(
                    () => new LocalModelArtifact(path, 1L, ValidSha),
                    $"unsafe artifact path {path}");
            }
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelArtifact("model.gguf", 0L, ValidSha),
                "zero-size artifact");
            Throws<ArgumentException>(
                () => new LocalModelArtifact("model.gguf", 1L, new string('A', 64)),
                "uppercase hash");
            Throws<ArgumentException>(
                () => new LocalModelArtifact("model.gguf", 1L, "abcd"),
                "short hash");
        }

        private static void GgufMetadataAndInferenceBoundsAreStrict()
        {
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelGgufMetadata(
                    0,
                    "architecture",
                    "Q4",
                    1L,
                    "tokenizer",
                    "pre"),
                "GGUF version");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelGgufMetadata(
                    3,
                    "architecture",
                    "Q4",
                    0L,
                    "tokenizer",
                    "pre"),
                "parameter count");
            Throws<ArgumentException>(
                () => new LocalModelInferenceProfile(
                    4096,
                    string.Empty,
                    Array.Empty<string>(),
                    CreateMemoryEstimate(),
                    4),
                "empty chat template");
            Throws<ArgumentException>(
                () => new LocalModelInferenceProfile(
                    4096,
                    "template",
                    DuplicateStopTokens,
                    CreateMemoryEstimate(),
                    4),
                "duplicate stop tokens");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelInferenceProfile(
                    4096,
                    "template",
                    CreateStopTokens(33),
                    CreateMemoryEstimate(),
                    4),
                "stop-token count");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelInferenceProfile(
                    4096,
                    "template",
                    Array.Empty<string>(),
                    CreateMemoryEstimate(),
                    65),
                "thread upper bound");
            Throws<ArgumentException>(
                () => new LocalModelInferenceProfile(
                    2048,
                    "template",
                    Array.Empty<string>(),
                    new LocalModelMemoryEstimate(1L, 4096, 512),
                    4),
                "memory context exceeds context limit");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelMemoryEstimate(1L, 1024, 2048),
                "batch exceeds basis context");
        }

        private static void MemoryAndDeviceCompatibilityMustAgree()
        {
            Throws<ArgumentException>(
                () => new LocalModelDeviceCompatibility(
                    X64AndroidAbis,
                    26,
                    Array.Empty<string>(),
                    2147483648L,
                    2),
                "wrong Android ABI");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelDeviceCompatibility(
                    Arm64AndroidAbis,
                    25,
                    Array.Empty<string>(),
                    2147483648L,
                    2),
                "API below native floor");
            Throws<ArgumentException>(
                () => new LocalModelDeviceCompatibility(
                    Arm64AndroidAbis,
                    26,
                    DuplicateCpuFeatures,
                    2147483648L,
                    2),
                "duplicate CPU feature");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalModelDeviceCompatibility(
                    Arm64AndroidAbis,
                    26,
                    Array.Empty<string>(),
                    2147483648L,
                    1),
                "device/runtime ABI mismatch");
            Throws<ArgumentException>(
                () => new LocalModelManifest(
                    1,
                    CreateIdentity("manifest.alpha", "model.alpha"),
                    CreateRuntime(),
                    CreateArtifact(),
                    CreateGguf(),
                    CreateInference(),
                    new LocalModelDeviceCompatibility(
                        Arm64AndroidAbis,
                        26,
                        Array.Empty<string>(),
                        536870912L,
                        2)),
                "minimum RAM understates peak estimate");
        }

        private static void CatalogUsesExactDataDrivenIdentityWithoutFallback()
        {
            LocalModelManifest alpha = CreateManifest("manifest.alpha", "model.alpha");
            LocalModelManifest beta = CreateManifest("manifest.beta", "model.beta");
            var catalog = new LocalModelManifestCatalog(
                new List<LocalModelManifest> { alpha, beta });

            Equal(2, catalog.Manifests.Count, "catalog count");
            True(
                catalog.TryGetByModelId("model.beta", out LocalModelManifest? found),
                "exact lookup");
            Same(beta, found, "exact lookup identity");
            False(
                catalog.TryGetByModelId("model.bet", out LocalModelManifest? fuzzy),
                "no prefix fallback");
            Equal<LocalModelManifest?>(null, fuzzy, "failed lookup returns no model");
            Throws<KeyNotFoundException>(
                () => catalog.GetRequiredByModelId("model.unknown"),
                "missing model has no default fallback");
            Throws<ArgumentException>(
                () => new LocalModelManifestCatalog(
                    new List<LocalModelManifest>
                    {
                        alpha,
                        CreateManifest("manifest.alpha", "model.other"),
                    }),
                "duplicate manifest ID");
            Throws<ArgumentException>(
                () => new LocalModelManifestCatalog(
                    new List<LocalModelManifest>
                    {
                        alpha,
                        CreateManifest("manifest.other", "model.alpha"),
                    }),
                "duplicate model ID");
        }

        private static LocalModelManifest CreateManifest(string manifestId, string modelId)
        {
            return new LocalModelManifest(
                1,
                CreateIdentity(manifestId, modelId),
                CreateRuntime(),
                CreateArtifact(),
                CreateGguf(),
                CreateInference(),
                CreateCompatibility());
        }

        private static LocalModelIdentity CreateIdentity(string manifestId, string modelId)
        {
            return new LocalModelIdentity(
                manifestId,
                modelId,
                "Synthetic Model",
                "synthetic-v1",
                new Uri("https://example.invalid/models/synthetic", UriKind.Absolute),
                "synthetic-revision",
                "LicenseRef-Synthetic",
                true,
                "Synthetic test model; not approved or bundled.");
        }

        private static LocalModelIdentity CreateIdentityWithUri(
            string uri,
            bool experimental,
            string experimentalReason)
        {
            return new LocalModelIdentity(
                "manifest.alpha",
                "model.alpha",
                "Synthetic Model",
                "synthetic-v1",
                new Uri(uri, UriKind.Absolute),
                "synthetic-revision",
                "LicenseRef-Synthetic",
                experimental,
                experimentalReason);
        }

        private static LocalModelRuntimeRequirement CreateRuntime()
        {
            return new LocalModelRuntimeRequirement("reachy_llama", 2, false);
        }

        private static LocalModelArtifact CreateArtifact()
        {
            return new LocalModelArtifact("models/synthetic.gguf", 123456789L, ValidSha);
        }

        private static LocalModelGgufMetadata CreateGguf()
        {
            return new LocalModelGgufMetadata(
                3,
                "synthetic-decoder",
                "SYNTHETIC_Q4",
                600000000L,
                "synthetic-tokenizer",
                "synthetic-pre");
        }

        private static LocalModelMemoryEstimate CreateMemoryEstimate()
        {
            return new LocalModelMemoryEstimate(1073741824L, 4096, 512);
        }

        private static LocalModelInferenceProfile CreateInference()
        {
            return new LocalModelInferenceProfile(
                4096,
                "{{ messages }}",
                SyntheticStopTokens,
                CreateMemoryEstimate(),
                4);
        }

        private static LocalModelDeviceCompatibility CreateCompatibility()
        {
            return new LocalModelDeviceCompatibility(
                Arm64AndroidAbis,
                26,
                Array.Empty<string>(),
                2147483648L,
                2);
        }

        private static string[] CreateStopTokens(int count)
        {
            var values = new string[count];
            for (int index = 0; index < count; ++index)
            {
                values[index] = $"stop-{index}";
            }
            return values;
        }

        private static void Contains(string actual, string expected, string label)
        {
            ++checks;
            if (!actual.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RMA-131 managed contract failed for {label}: '{actual}' lacks '{expected}'.");
            }
        }

        private static void Same(object expected, object? actual, string label)
        {
            ++checks;
            if (!ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-131 managed contract failed for {label}: references differ.");
            }
        }

        private static void True(bool actual, string label)
        {
            ++checks;
            if (!actual)
            {
                throw new InvalidOperationException(
                    $"RMA-131 managed contract failed for {label}: expected true.");
            }
        }

        private static void False(bool actual, string label)
        {
            ++checks;
            if (actual)
            {
                throw new InvalidOperationException(
                    $"RMA-131 managed contract failed for {label}: expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            ++checks;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-131 managed contract failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void Throws<TException>(Func<object?> action, string label)
            where TException : Exception
        {
            ++checks;
            try
            {
                object? result = action();
                GC.KeepAlive(result);
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"RMA-131 managed contract failed for {label}: expected {typeof(TException).Name}.");
        }
    }
}

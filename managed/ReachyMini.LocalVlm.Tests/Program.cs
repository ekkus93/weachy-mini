#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static class Program
    {
        private static readonly string[] RequiredManifestSections =
        {
            "schema_version",
            "identity",
            "runtime",
            "limits",
            "distribution",
            "capabilities",
            "artifacts",
        };

        private static readonly string[] RequiredArtifactFields =
        {
            "relative_path",
            "sha256",
            "size_bytes",
        };

        private static async Task<int> Main()
        {
            ReleasePolicyIsOptionalAndFailClosed();
            ValidManifestPublishesExactCapabilities();
            ManifestRejectsUnsupportedSchema();
            IdentityRejectsUnsafeIdentifiers();
            IdentityRequiresHttpsProvenance();
            IdentityRejectsOverlongMetadata();
            RuntimeRejectsNetworkDependence();
            RuntimeRejectsZeroParameters();
            LimitsRejectInvalidTokenRelationships();
            DistributionRejectsFirstReleaseRequirement();
            DistributionRejectsAutomaticDownloads();
            SemanticCapabilitiesRequireAFeature();
            SemanticCapabilitiesRequireCancellation();
            ArtifactRejectsTraversal();
            ArtifactRejectsBackslashesAndSchemes();
            ArtifactRejectsUppercaseOrShortHashes();
            ArtifactRejectsZeroLength();
            ManifestRequiresArtifacts();
            ManifestRejectsDuplicateArtifacts();
            ManifestRejectsNullArtifacts();
            ManifestRejectsTooManyArtifacts();
            ManifestRejectsUnderstatedStorage();
            ManifestCopiesArtifactLists();
            AdapterCapabilitiesRepresentUnavailableHonestly();
            AdapterCapabilitiesRequireLoadBeforeCreate();
            OperationalAdaptersRequireCancellationAndCapacity();
            AvailabilityRequiresRuntimeForAvailableState();
            UnavailableAndDisposedStatesRejectRuntimeClaims();
            ProviderConfigurationRequiresVerifiedArtifacts();
            ProviderConfigurationRejectsNetworkRoots();
            ProviderConfigurationAcceptsLocalRoots();
            await ProviderCreationAcceptsExactOnDeviceProvider().ConfigureAwait(false);
            await ProviderCreationRejectsRemoteProviders().ConfigureAwait(false);
            await ProviderCreationRejectsIdentityMismatch().ConfigureAwait(false);
            await ProviderCreationRejectsCapabilityMismatch().ConfigureAwait(false);
            await UnavailableStubReportsNoRuntimeOrCapability().ConfigureAwait(false);
            await UnavailableStubNeverCreatesOrFallsBack().ConfigureAwait(false);
            await UnavailableStubHonorsPreCancellation().ConfigureAwait(false);
            await UnavailableStubDisposalIsIdempotent().ConfigureAwait(false);
            SchemaDeclaresAllRequiredManifestSections();
            SchemaForbidsNetworkAndFirstReleaseRequirement();
            SchemaRequiresIntegrityMetadataAndSafePaths();
            ManifestDirectoryContainsNoModelPayloads();
            DocumentationDefersBenchmarkingAndDownloads();
            SourceContractContainsNoDownloadOrFallbackExecution();
            Console.WriteLine("RMA-114 local VLM extension-point contracts passed.");
            return 0;
        }

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

        private static void IdentityRejectsUnsafeIdentifiers()
        {
            Throws<ArgumentException>(
                () => Identity(manifestId: "Bad Id"),
                "unsafe manifest id");
            Throws<ArgumentException>(
                () => Identity(manifestId: "-leading"),
                "leading punctuation");
            Throws<ArgumentException>(
                () => Identity(modelId: "org/model"),
                "slash in model id");
        }

        private static void IdentityRequiresHttpsProvenance()
        {
            Throws<ArgumentException>(
                () => new LocalVlmModelIdentity(
                    "manifest",
                    "model",
                    "Model",
                    "1",
                    new Uri("http://example.com/model", UriKind.Absolute),
                    "revision",
                    "Apache-2.0"),
                "http provenance");
            Throws<ArgumentException>(
                () => new LocalVlmModelIdentity(
                    "manifest",
                    "model",
                    "Model",
                    "1",
                    new Uri("model", UriKind.Relative),
                    "revision",
                    "Apache-2.0"),
                "relative provenance");
        }


        private static void IdentityRejectsOverlongMetadata()
        {
            Throws<ArgumentOutOfRangeException>(
                () => new LocalVlmModelIdentity(
                    "manifest",
                    "model",
                    new string('x', 129),
                    "1",
                    new Uri("https://example.com/model", UriKind.Absolute),
                    "revision",
                    "Apache-2.0"),
                "overlong display name");
        }

        private static void RuntimeRejectsNetworkDependence()
        {
            Throws<ArgumentException>(
                () => Runtime(requiresNetworkAccess: true),
                "network runtime");
        }

        private static void RuntimeRejectsZeroParameters()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Runtime(parameterCount: 0L),
                "zero parameters");
        }

        private static void LimitsRejectInvalidTokenRelationships()
        {
            Throws<ArgumentOutOfRangeException>(
                () => Limits(contextWindowTokens: 0),
                "zero context");
            Throws<ArgumentOutOfRangeException>(
                () => Limits(contextWindowTokens: 128, maximumOutputTokens: 129),
                "output exceeds context");
            Throws<ArgumentOutOfRangeException>(
                () => Limits(maximumImageWidth: 0),
                "zero image width");
            Throws<ArgumentOutOfRangeException>(
                () => Limits(minimumRamBytes: 0L),
                "zero ram");
        }

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

        private static void AdapterCapabilitiesRepresentUnavailableHonestly()
        {
            var capabilities = new LocalVlmAdapterCapabilities(
                canLoadModels: false,
                canCreateProviders: false,
                supportsCancellation: false,
                maximumConcurrentLoads: 0);
            False(capabilities.CanLoadModels, "unavailable load");
            False(capabilities.CanCreateProviders, "unavailable create");
            False(capabilities.SupportsCancellation, "unavailable operational cancellation");
            Equal(0, capabilities.MaximumConcurrentLoads, "unavailable capacity");
        }

        private static void AdapterCapabilitiesRequireLoadBeforeCreate()
        {
            Throws<ArgumentException>(
                () => new LocalVlmAdapterCapabilities(
                    canLoadModels: false,
                    canCreateProviders: true,
                    supportsCancellation: true,
                    maximumConcurrentLoads: 1),
                "create without load");
        }

        private static void OperationalAdaptersRequireCancellationAndCapacity()
        {
            Throws<ArgumentException>(
                () => new LocalVlmAdapterCapabilities(
                    canLoadModels: true,
                    canCreateProviders: true,
                    supportsCancellation: false,
                    maximumConcurrentLoads: 1),
                "operational cancellation");
            Throws<ArgumentOutOfRangeException>(
                () => new LocalVlmAdapterCapabilities(
                    canLoadModels: true,
                    canCreateProviders: true,
                    supportsCancellation: true,
                    maximumConcurrentLoads: 0),
                "operational capacity");
            Throws<ArgumentException>(
                () => new LocalVlmAdapterCapabilities(
                    canLoadModels: false,
                    canCreateProviders: false,
                    supportsCancellation: true,
                    maximumConcurrentLoads: 0),
                "unavailable false capability");
        }

        private static void AvailabilityRequiresRuntimeForAvailableState()
        {
            Throws<ArgumentException>(
                () => new LocalVlmAdapterAvailability(
                    LocalVlmAdapterState.Available,
                    runtimePresent: false,
                    "missing"),
                "available without runtime");
            var available = new LocalVlmAdapterAvailability(
                LocalVlmAdapterState.Available,
                runtimePresent: true,
                "runtime ready");
            True(available.CanCreateProvider, "available create state");
        }

        private static void UnavailableAndDisposedStatesRejectRuntimeClaims()
        {
            Throws<ArgumentException>(
                () => new LocalVlmAdapterAvailability(
                    LocalVlmAdapterState.Unavailable,
                    runtimePresent: true,
                    "incorrect"),
                "unavailable runtime");
            Throws<ArgumentException>(
                () => new LocalVlmAdapterAvailability(
                    LocalVlmAdapterState.Disposed,
                    runtimePresent: true,
                    "incorrect"),
                "disposed runtime");
        }

        private static void ProviderConfigurationRequiresVerifiedArtifacts()
        {
            Throws<ArgumentException>(
                () => new LocalVlmProviderConfiguration(
                    Manifest(),
                    "/models/example",
                    "provider-instance",
                    artifactIntegrityVerified: false),
                "unverified artifacts");
        }

        private static void ProviderConfigurationRejectsNetworkRoots()
        {
            Throws<ArgumentException>(
                () => Configuration("https://example.com/models"),
                "https artifact root");
            Throws<ArgumentException>(
                () => Configuration("ftp://example.com/models"),
                "ftp artifact root");
            Throws<ArgumentException>(
                () => Configuration("models/example"),
                "relative artifact root");
            Throws<ArgumentException>(
                () => Configuration("file://server/share"),
                "remote file URI");
            Throws<ArgumentException>(
                () => Configuration(
                    new string((char)92, 2) +
                    "server" + (char)92 + "share"),
                "UNC artifact root");
            Throws<ArgumentException>(
                () => Configuration("//server/share"),
                "slash network share");
            Throws<ArgumentException>(
                () => Configuration("content:///models/example"),
                "content URI without authority");
        }

        private static void ProviderConfigurationAcceptsLocalRoots()
        {
            Equal("/models/example", Configuration("/models/example").LocalArtifactRoot, "unix path");
            Equal(
                "file:///models/example",
                Configuration("file:///models/example").LocalArtifactRoot,
                "file URI");
            Equal(
                "content://models/example",
                Configuration("content://models/example").LocalArtifactRoot,
                "content URI");
            string windowsRoot =
                "C:" + (char)92 + "models" + (char)92 + "example";
            Equal(
                windowsRoot,
                Configuration(windowsRoot).LocalArtifactRoot,
                "Windows drive path");
        }

        private static async Task ProviderCreationAcceptsExactOnDeviceProvider()
        {
            LocalVlmProviderConfiguration configuration = Configuration();
            await using var provider = Provider(
                configuration,
                VisionProviderLocation.OnDevice);
            LocalVlmProviderCreationResult result =
                LocalVlmProviderCreationResult.Created(configuration, provider);
            True(result.Succeeded, "created status");
            Same(provider, result.Provider, "created provider");
            False(result.RequiresAdapterReset, "created reset");
        }

        private static async Task ProviderCreationRejectsRemoteProviders()
        {
            LocalVlmProviderConfiguration configuration = Configuration();
            await using var cloud = Provider(
                configuration,
                VisionProviderLocation.Cloud);
            Throws<ArgumentException>(
                () => LocalVlmProviderCreationResult.Created(configuration, cloud),
                "cloud provider");

            await using var localNetwork = Provider(
                configuration,
                VisionProviderLocation.LocalNetwork);
            Throws<ArgumentException>(
                () => LocalVlmProviderCreationResult.Created(
                    configuration,
                    localNetwork),
                "local-network provider");
        }

        private static async Task ProviderCreationRejectsIdentityMismatch()
        {
            LocalVlmProviderConfiguration configuration = Configuration();
            await using var provider = Provider(
                configuration,
                VisionProviderLocation.OnDevice,
                providerId: "different-model");
            Throws<ArgumentException>(
                () => LocalVlmProviderCreationResult.Created(
                    configuration,
                    provider),
                "provider identity mismatch");
        }

        private static async Task ProviderCreationRejectsCapabilityMismatch()
        {
            LocalVlmProviderConfiguration configuration = Configuration();
            await using var provider = Provider(
                configuration,
                VisionProviderLocation.OnDevice,
                maximumPromptCharacters: 2048);
            Throws<ArgumentException>(
                () => LocalVlmProviderCreationResult.Created(
                    configuration,
                    provider),
                "provider capability mismatch");
        }

        private static async Task UnavailableStubReportsNoRuntimeOrCapability()
        {
            await using var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            Equal(
                LocalVlmAdapterState.Unavailable,
                adapter.Availability.State,
                "stub state");
            False(adapter.Availability.RuntimePresent, "stub runtime");
            False(adapter.Availability.CanCreateProvider, "stub availability");
            False(adapter.Capabilities.CanLoadModels, "stub load capability");
            False(adapter.Capabilities.CanCreateProviders, "stub create capability");
            Equal(0, adapter.Capabilities.MaximumConcurrentLoads, "stub load capacity");
        }

        private static async Task UnavailableStubNeverCreatesOrFallsBack()
        {
            await using var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            LocalVlmProviderCreationResult result =
                await adapter.CreateProviderAsync(
                    Configuration(),
                    CancellationToken.None).ConfigureAwait(false);
            Equal(
                LocalVlmProviderCreationStatus.Unavailable,
                result.Status,
                "stub creation status");
            True(result.Provider == null, "stub no provider");
            False(result.RequiresAdapterReset, "stub reset");
            Contains("no fallback", result.Diagnostic, "stub fallback diagnostic");
            Contains("download", result.Diagnostic, "stub download diagnostic");
        }

        private static async Task UnavailableStubHonorsPreCancellation()
        {
            await using var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            LocalVlmProviderCreationResult result =
                await adapter.CreateProviderAsync(
                    Configuration(),
                    cancellation.Token).ConfigureAwait(false);
            Equal(
                LocalVlmProviderCreationStatus.Cancelled,
                result.Status,
                "stub cancellation");
            True(result.Provider == null, "cancelled no provider");
        }

        private static async Task UnavailableStubDisposalIsIdempotent()
        {
            var adapter =
                new UnavailableLocalVisionLanguageAdapter("unavailable-instance");
            await adapter.DisposeAsync().ConfigureAwait(false);
            await adapter.DisposeAsync().ConfigureAwait(false);
            Equal(
                LocalVlmAdapterState.Disposed,
                adapter.Availability.State,
                "disposed state");
            LocalVlmProviderCreationResult result =
                await adapter.CreateProviderAsync(
                    Configuration(),
                    CancellationToken.None).ConfigureAwait(false);
            Equal(
                LocalVlmProviderCreationStatus.Unavailable,
                result.Status,
                "disposed creation");
        }

        private static void SchemaDeclaresAllRequiredManifestSections()
        {
            using JsonDocument schema = LoadSchema();
            JsonElement root = schema.RootElement;
            False(root.GetProperty("additionalProperties").GetBoolean(), "root closed schema");
            string[] required = root.GetProperty("required")
                .EnumerateArray()
                .Select(static value => value.GetString() ?? string.Empty)
                .ToArray();
            SetEqual(
                RequiredManifestSections,
                required,
                "schema sections");
            Equal(
                1,
                root.GetProperty("properties")
                    .GetProperty("schema_version")
                    .GetProperty("const")
                    .GetInt32(),
                "schema const");
        }

        private static void SchemaForbidsNetworkAndFirstReleaseRequirement()
        {
            using JsonDocument schema = LoadSchema();
            JsonElement properties = schema.RootElement.GetProperty("properties");
            False(
                properties.GetProperty("runtime")
                    .GetProperty("properties")
                    .GetProperty("requires_network_access")
                    .GetProperty("const")
                    .GetBoolean(),
                "schema runtime network");
            JsonElement distribution = properties.GetProperty("distribution")
                .GetProperty("properties");
            False(
                distribution.GetProperty("required_for_first_release")
                    .GetProperty("const")
                    .GetBoolean(),
                "schema first release");
            False(
                distribution.GetProperty("automatic_download_allowed")
                    .GetProperty("const")
                    .GetBoolean(),
                "schema automatic download");
        }

        private static void SchemaRequiresIntegrityMetadataAndSafePaths()
        {
            using JsonDocument schema = LoadSchema();
            JsonElement artifacts = schema.RootElement.GetProperty("properties")
                .GetProperty("artifacts");
            Equal(1, artifacts.GetProperty("minItems").GetInt32(), "schema min artifacts");
            Equal(64, artifacts.GetProperty("maxItems").GetInt32(), "schema max artifacts");
            JsonElement item = artifacts.GetProperty("items");
            string[] required = item.GetProperty("required")
                .EnumerateArray()
                .Select(static value => value.GetString() ?? string.Empty)
                .ToArray();
            SetEqual(
                RequiredArtifactFields,
                required,
                "artifact fields");
            Equal(
                "^[0-9a-f]{64}$",
                item.GetProperty("properties")
                    .GetProperty("sha256")
                    .GetProperty("pattern")
                    .GetString() ?? string.Empty,
                "schema hash pattern");
            Contains(
                "\\.{1,2}",
                item.GetProperty("properties")
                    .GetProperty("relative_path")
                    .GetProperty("pattern")
                    .GetString() ?? string.Empty,
                "schema traversal pattern");
        }

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

        private static void True(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name + " expected true.");
            }
        }

        private static void False(bool value, string name)
        {
            if (value)
            {
                throw new InvalidOperationException(name + " expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string name)
            where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " expected '" + expected + "' but received '" + actual + "'.");
            }
        }

        private static void Same(object expected, object? actual, string name)
        {
            if (!ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException(name + " expected the same instance.");
            }
        }

        private static void Contains(string expected, string actual, string name)
        {
            if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    name + " expected text '" + expected + "'.");
            }
        }

        private static void SetEqual(
            IEnumerable<string> expected,
            IEnumerable<string> actual,
            string name)
        {
            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
            var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
            if (!expectedSet.SetEquals(actualSet))
            {
                throw new InvalidOperationException(name + " sets do not match.");
            }
        }

        private static void Throws<TException>(Func<object?> action, string name)
            where TException : Exception
        {
            try
            {
                _ = action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                name + " expected " + typeof(TException).Name + ".");
        }

        private sealed class FakeProvider : IVisionLanguageProvider
        {
            public FakeProvider(
                ProviderDescriptor descriptor,
                VisionLanguageCapabilities capabilities)
            {
                Descriptor = descriptor ??
                    throw new ArgumentNullException(nameof(descriptor));
                Capabilities = capabilities ??
                    throw new ArgumentNullException(nameof(capabilities));
            }

            public ProviderDescriptor Descriptor { get; }

            public VisionLanguageCapabilities Capabilities { get; }

            public ValueTask<VisionLanguageResult> AnalyzeAsync(
                VisionLanguageRequest request,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<VisionLanguageResult>(
                        VisionLanguageResult.Failure(
                            VisionOperationStatus.Cancelled,
                            Descriptor,
                            request,
                            requiresProviderReset: false,
                            "Cancelled."));
                }
                return new ValueTask<VisionLanguageResult>(
                    VisionLanguageResult.Success(
                        Descriptor,
                        request,
                        "Synthetic semantic output."));
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
    }
}

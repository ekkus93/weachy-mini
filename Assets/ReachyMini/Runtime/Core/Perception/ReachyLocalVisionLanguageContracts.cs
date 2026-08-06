#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
    public static class LocalVlmReleasePolicy
    {
        public static bool RequiredForFirstRelease => false;

        public static bool AutomaticModelDownloadEnabled => false;

        public static bool AutomaticProviderFallbackEnabled => false;

        public static bool CandidateBenchmarkingEnabled => false;
    }

    public enum LocalVlmArtifactSource
    {
        Bundled = 0,
        UserProvided = 1,
        DeveloperProvided = 2,
    }

    public enum LocalVlmAdapterState
    {
        Unavailable = 0,
        Available = 1,
        Faulted = 2,
        Disposed = 3,
    }

    public enum LocalVlmProviderCreationStatus
    {
        Created = 0,
        Unavailable = 1,
        InvalidConfiguration = 2,
        Cancelled = 3,
        RuntimeFailure = 4,
    }

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
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
            if (!value.IsAbsoluteUri ||
                value.AbsoluteUri.Length > 2048 ||
                !string.Equals(
                    value.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Local VLM provenance must use an absolute HTTPS source URI.",
                    name);
            }
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

    public sealed class LocalVlmAdapterDescriptor
    {
        public LocalVlmAdapterDescriptor(
            string runtimeId,
            string runtimeInstanceId,
            string displayName,
            string version)
        {
            RuntimeId = LocalVlmModelIdentity.RequireIdentifier(
                runtimeId,
                nameof(runtimeId));
            RuntimeInstanceId = LocalVlmModelIdentity.RequireIdentifier(
                runtimeInstanceId,
                nameof(runtimeInstanceId));
            DisplayName = LocalVlmModelIdentity.RequireBoundedText(
                displayName,
                nameof(displayName),
                128);
            Version = LocalVlmModelIdentity.RequireBoundedText(
                version,
                nameof(version),
                128);
        }

        public string RuntimeId { get; }

        public string RuntimeInstanceId { get; }

        public string DisplayName { get; }

        public string Version { get; }
    }

    public sealed class LocalVlmAdapterCapabilities
    {
        public LocalVlmAdapterCapabilities(
            bool canLoadModels,
            bool canCreateProviders,
            bool supportsCancellation,
            int maximumConcurrentLoads)
        {
            if (canCreateProviders && !canLoadModels)
            {
                throw new ArgumentException(
                    "An adapter cannot create providers without loading models.",
                    nameof(canCreateProviders));
            }

            bool operational = canLoadModels || canCreateProviders;
            if (operational)
            {
                if (!supportsCancellation)
                {
                    throw new ArgumentException(
                        "Operational local VLM adapters must support cancellation.",
                        nameof(supportsCancellation));
                }
                if (maximumConcurrentLoads <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumConcurrentLoads));
                }
            }
            else
            {
                if (supportsCancellation)
                {
                    throw new ArgumentException(
                        "An unavailable adapter cannot claim operational cancellation support.",
                        nameof(supportsCancellation));
                }
                if (maximumConcurrentLoads != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumConcurrentLoads));
                }
            }

            CanLoadModels = canLoadModels;
            CanCreateProviders = canCreateProviders;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentLoads = maximumConcurrentLoads;
        }

        public bool CanLoadModels { get; }

        public bool CanCreateProviders { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentLoads { get; }
    }

    public sealed class LocalVlmAdapterAvailability
    {
        public LocalVlmAdapterAvailability(
            LocalVlmAdapterState state,
            bool runtimePresent,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(LocalVlmAdapterState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            if (state == LocalVlmAdapterState.Available && !runtimePresent)
            {
                throw new ArgumentException(
                    "An available local VLM adapter must have a runtime present.",
                    nameof(runtimePresent));
            }
            if ((state == LocalVlmAdapterState.Unavailable ||
                    state == LocalVlmAdapterState.Disposed) &&
                runtimePresent)
            {
                throw new ArgumentException(
                    "Unavailable or disposed local VLM adapters cannot report a present runtime.",
                    nameof(runtimePresent));
            }

            State = state;
            RuntimePresent = runtimePresent;
            Diagnostic = ProviderDescriptor.RequireText(
                diagnostic,
                nameof(diagnostic));
        }

        public LocalVlmAdapterState State { get; }

        public bool RuntimePresent { get; }

        public string Diagnostic { get; }

        public bool CanCreateProvider =>
            State == LocalVlmAdapterState.Available && RuntimePresent;
    }

    public sealed class LocalVlmProviderConfiguration
    {
        public LocalVlmProviderConfiguration(
            LocalVlmModelManifest manifest,
            string localArtifactRoot,
            string providerInstanceId,
            bool artifactIntegrityVerified)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            LocalArtifactRoot = RequireLocalArtifactRoot(
                localArtifactRoot,
                nameof(localArtifactRoot));
            ProviderInstanceId = LocalVlmModelIdentity.RequireIdentifier(
                providerInstanceId,
                nameof(providerInstanceId));
            if (!artifactIntegrityVerified)
            {
                throw new ArgumentException(
                    "Local VLM artifacts must be verified against the manifest before adapter use.",
                    nameof(artifactIntegrityVerified));
            }
            ArtifactIntegrityVerified = true;
        }

        public LocalVlmModelManifest Manifest { get; }

        public string LocalArtifactRoot { get; }

        public string ProviderInstanceId { get; }

        public bool ArtifactIntegrityVerified { get; }

        private static string RequireLocalArtifactRoot(
            string value,
            string name)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > 1024)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Local VLM artifact roots cannot exceed 1024 characters.");
            }
            if (text.StartsWith("//", StringComparison.Ordinal) ||
                text[0] == (char)92)
            {
                throw new ArgumentException(
                    "Local VLM artifact roots cannot use UNC or network-share paths.",
                    name);
            }

            bool unixAbsolutePath = text[0] == '/';
            bool windowsDrivePath =
                text.Length >= 3 &&
                ((text[0] >= 'A' && text[0] <= 'Z') ||
                    (text[0] >= 'a' && text[0] <= 'z')) &&
                text[1] == ':' &&
                (text[2] == '/' || text[2] == (char)92);
            if (unixAbsolutePath || windowsDrivePath)
            {
                return text;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException(
                    "Local VLM artifact roots must be absolute local paths, hostless file URIs, or Android content URIs.",
                    name);
            }

            if (string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(uri.Host) ||
                    !string.IsNullOrEmpty(uri.UserInfo))
                {
                    throw new ArgumentException(
                        "Local VLM file URIs cannot name a remote host or credentials.",
                        name);
                }
                return text;
            }

            if (string.Equals(
                    uri.Scheme,
                    "content",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                uri.Port == -1)
            {
                return text;
            }

            throw new ArgumentException(
                "Local VLM artifact roots must be absolute local paths, hostless file URIs, or authority-bearing Android content URIs; network locations are forbidden.",
                name);
        }
    }

    public sealed class LocalVlmProviderCreationResult
    {
        private LocalVlmProviderCreationResult(
            LocalVlmProviderCreationStatus status,
            string manifestId,
            IVisionLanguageProvider? provider,
            bool requiresAdapterReset,
            string diagnostic)
        {
            Status = status;
            ManifestId = manifestId;
            Provider = provider;
            RequiresAdapterReset = requiresAdapterReset;
            Diagnostic = diagnostic;
        }

        public LocalVlmProviderCreationStatus Status { get; }

        public string ManifestId { get; }

        public IVisionLanguageProvider? Provider { get; }

        public bool RequiresAdapterReset { get; }

        public string Diagnostic { get; }

        public bool Succeeded => Status == LocalVlmProviderCreationStatus.Created;

        public static LocalVlmProviderCreationResult Created(
            LocalVlmProviderConfiguration configuration,
            IVisionLanguageProvider provider)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            ProviderDescriptor descriptor = provider.Descriptor ??
                throw new ArgumentException(
                    "A created local VLM provider must publish a descriptor.",
                    nameof(provider));
            VisionLanguageCapabilities capabilities = provider.Capabilities ??
                throw new ArgumentException(
                    "A created local VLM provider must publish capabilities.",
                    nameof(provider));
            LocalVlmModelManifest manifest = configuration.Manifest;
            if (descriptor.Kind != VisionProviderKind.SemanticVisionLanguage ||
                descriptor.Location != VisionProviderLocation.OnDevice)
            {
                throw new ArgumentException(
                    "A local VLM adapter may create only on-device semantic VLM providers.",
                    nameof(provider));
            }
            if (!string.Equals(
                    descriptor.ProviderId,
                    manifest.Identity.ModelId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    descriptor.InstanceId,
                    configuration.ProviderInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    descriptor.Version,
                    manifest.Identity.ModelVersion,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A created local VLM provider must match the selected model and provider instance.",
                    nameof(provider));
            }
            if (capabilities.SupportsVisualQuestions !=
                    manifest.Capabilities.SupportsVisualQuestions ||
                capabilities.SupportsSceneDescription !=
                    manifest.Capabilities.SupportsSceneDescription ||
                capabilities.SupportsCancellation !=
                    manifest.Capabilities.SupportsCancellation ||
                capabilities.MaximumConcurrentOperations !=
                    manifest.Capabilities.MaximumConcurrentOperations ||
                capabilities.MaximumPromptCharacters !=
                    manifest.Limits.MaximumPromptCharacters)
            {
                throw new ArgumentException(
                    "A created local VLM provider must match the manifest capability declaration exactly.",
                    nameof(provider));
            }

            return new LocalVlmProviderCreationResult(
                LocalVlmProviderCreationStatus.Created,
                manifest.Identity.ManifestId,
                provider,
                requiresAdapterReset: false,
                "Local VLM provider created from verified local artifacts.");
        }

        public static LocalVlmProviderCreationResult Failure(
            LocalVlmProviderCreationStatus status,
            string manifestId,
            bool requiresAdapterReset,
            string diagnostic)
        {
            if (status == LocalVlmProviderCreationStatus.Created ||
                !Enum.IsDefined(
                    typeof(LocalVlmProviderCreationStatus),
                    status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new LocalVlmProviderCreationResult(
                status,
                LocalVlmModelIdentity.RequireIdentifier(
                    manifestId,
                    nameof(manifestId)),
                null,
                requiresAdapterReset,
                ProviderDescriptor.RequireText(
                    diagnostic,
                    nameof(diagnostic)));
        }
    }

    public interface ILocalVisionLanguageAdapter : IAsyncDisposable
    {
        LocalVlmAdapterDescriptor Descriptor { get; }

        LocalVlmAdapterCapabilities Capabilities { get; }

        LocalVlmAdapterAvailability Availability { get; }

        ValueTask<LocalVlmProviderCreationResult> CreateProviderAsync(
            LocalVlmProviderConfiguration configuration,
            CancellationToken cancellationToken);
    }

    public sealed class UnavailableLocalVisionLanguageAdapter :
        ILocalVisionLanguageAdapter
    {
        private static readonly LocalVlmAdapterCapabilities unavailableCapabilities =
            new LocalVlmAdapterCapabilities(
                canLoadModels: false,
                canCreateProviders: false,
                supportsCancellation: false,
                maximumConcurrentLoads: 0);

        private static readonly LocalVlmAdapterAvailability unavailableAvailability =
            new LocalVlmAdapterAvailability(
                LocalVlmAdapterState.Unavailable,
                runtimePresent: false,
                "No local VLM runtime is installed. Local semantic vision remains optional.");

        private static readonly LocalVlmAdapterAvailability disposedAvailability =
            new LocalVlmAdapterAvailability(
                LocalVlmAdapterState.Disposed,
                runtimePresent: false,
                "The unavailable local VLM adapter has been disposed.");

        private int disposed;

        public UnavailableLocalVisionLanguageAdapter(
            string runtimeInstanceId)
        {
            Descriptor = new LocalVlmAdapterDescriptor(
                "unavailable-local-vlm",
                runtimeInstanceId,
                "Unavailable local VLM",
                "1");
        }

        public LocalVlmAdapterDescriptor Descriptor { get; }

        public LocalVlmAdapterCapabilities Capabilities =>
            unavailableCapabilities;

        public LocalVlmAdapterAvailability Availability =>
            Volatile.Read(ref disposed) == 0
                ? unavailableAvailability
                : disposedAvailability;

        public ValueTask<LocalVlmProviderCreationResult> CreateProviderAsync(
            LocalVlmProviderConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            LocalVlmProviderCreationResult result;
            if (cancellationToken.IsCancellationRequested)
            {
                result = LocalVlmProviderCreationResult.Failure(
                    LocalVlmProviderCreationStatus.Cancelled,
                    configuration.Manifest.Identity.ManifestId,
                    requiresAdapterReset: false,
                    "Local VLM provider creation was cancelled before invocation.");
            }
            else if (Volatile.Read(ref disposed) != 0)
            {
                result = LocalVlmProviderCreationResult.Failure(
                    LocalVlmProviderCreationStatus.Unavailable,
                    configuration.Manifest.Identity.ManifestId,
                    requiresAdapterReset: false,
                    "The unavailable local VLM adapter is disposed.");
            }
            else
            {
                result = LocalVlmProviderCreationResult.Failure(
                    LocalVlmProviderCreationStatus.Unavailable,
                    configuration.Manifest.Identity.ManifestId,
                    requiresAdapterReset: false,
                    "No local VLM runtime or model is installed; no fallback or download was attempted.");
            }

            return new ValueTask<LocalVlmProviderCreationResult>(result);
        }

        public ValueTask DisposeAsync()
        {
            _ = Interlocked.Exchange(ref disposed, 1);
            return default;
        }
    }
}

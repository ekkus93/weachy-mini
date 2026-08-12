#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Perception
{
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

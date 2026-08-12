#nullable enable

using System;
using System.Threading.Tasks;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
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
    }
}

#nullable enable

using System;
using System.Threading.Tasks;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
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
    }
}

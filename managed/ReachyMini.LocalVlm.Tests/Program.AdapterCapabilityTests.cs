#nullable enable

using System;
using ReachyMini.Perception;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
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
    }
}

#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed class ReachyProviderSelection
    {
        public ReachyProviderSelection(
            ReachyProviderKind kind,
            string providerId,
            string displayName,
            ReachyProviderExecution execution,
            ReachyConnectivityRequirement connectivity,
            bool available,
            string status)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException(
                    "A provider selection requires an identifier.",
                    nameof(providerId));
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A provider selection requires a display name.",
                    nameof(displayName));
            }
            if (string.IsNullOrWhiteSpace(status))
            {
                throw new ArgumentException(
                    "A provider selection requires a status explanation.",
                    nameof(status));
            }
            if (execution == ReachyProviderExecution.Unconfigured &&
                connectivity != ReachyConnectivityRequirement.Unavailable)
            {
                throw new ArgumentException(
                    "An unconfigured provider cannot claim connectivity capability.",
                    nameof(connectivity));
            }
            if ((execution == ReachyProviderExecution.AndroidService ||
                 execution == ReachyProviderExecution.Cloud) &&
                connectivity != ReachyConnectivityRequirement.NetworkRequired)
            {
                throw new ArgumentException(
                    "Network-backed provider selections must be labeled network required.",
                    nameof(connectivity));
            }

            Kind = kind;
            ProviderId = providerId;
            DisplayName = displayName;
            Execution = execution;
            Connectivity = connectivity;
            Available = available;
            Status = status;
        }

        public ReachyProviderKind Kind { get; }

        public string ProviderId { get; }

        public string DisplayName { get; }

        public ReachyProviderExecution Execution { get; }

        public ReachyConnectivityRequirement Connectivity { get; }

        public bool Available { get; }

        public string Status { get; }

        public bool SendsDataOffDevice =>
            Execution == ReachyProviderExecution.AndroidService ||
            Execution == ReachyProviderExecution.Cloud ||
            Connectivity == ReachyConnectivityRequirement.NetworkRequired;
    }
}

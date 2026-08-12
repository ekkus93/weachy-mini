#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class VisionProviderSelectionSnapshot
    {
        internal VisionProviderSelectionSnapshot(
            VisionProviderKind kind,
            string providerInstanceId,
            ulong epoch)
        {
            Kind = kind;
            ProviderInstanceId = ProviderDescriptor.RequireText(
                providerInstanceId,
                nameof(providerInstanceId));
            if (epoch == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(epoch));
            }
            Epoch = epoch;
        }

        public VisionProviderKind Kind { get; }

        public string ProviderInstanceId { get; }

        public ulong Epoch { get; }
    }

    public sealed class VisionProviderSelection
    {
        private readonly object sync = new object();
        private VisionProviderSelectionSnapshot current;

        public VisionProviderSelection(ProviderDescriptor initialProvider)
        {
            if (initialProvider == null)
            {
                throw new ArgumentNullException(nameof(initialProvider));
            }
            current = new VisionProviderSelectionSnapshot(
                initialProvider.Kind,
                initialProvider.InstanceId,
                1UL);
        }

        public VisionProviderSelectionSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        public VisionProviderSelectionSnapshot Select(
            ProviderDescriptor provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            lock (sync)
            {
                if (provider.Kind != current.Kind)
                {
                    throw new ArgumentException(
                        "A provider selection cannot change provider kind.",
                        nameof(provider));
                }
                ulong nextEpoch = checked(current.Epoch + 1UL);
                current = new VisionProviderSelectionSnapshot(
                    provider.Kind,
                    provider.InstanceId,
                    nextEpoch);
                return current;
            }
        }

        public bool IsCurrent(VisionRequestContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (sync)
            {
                return context.ProviderKind == current.Kind &&
                    context.ProviderInstanceId == current.ProviderInstanceId &&
                    context.SelectionEpoch == current.Epoch;
            }
        }
    }

    public sealed class VisionRequestContext
    {
        public static readonly TimeSpan MaximumTimeout =
            TimeSpan.FromMinutes(5.0);

        public VisionRequestContext(
            string requestId,
            VisionProviderSelectionSnapshot selection,
            TimeSpan timeout)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }
            if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    timeout,
                    "Vision operation timeouts must be in (0, 5 minutes].");
            }

            RequestId = ProviderDescriptor.RequireText(
                requestId,
                nameof(requestId));
            ProviderKind = selection.Kind;
            ProviderInstanceId = selection.ProviderInstanceId;
            SelectionEpoch = selection.Epoch;
            Timeout = timeout;
        }

        public string RequestId { get; }

        public VisionProviderKind ProviderKind { get; }

        public string ProviderInstanceId { get; }

        public ulong SelectionEpoch { get; }

        public TimeSpan Timeout { get; }
    }
}

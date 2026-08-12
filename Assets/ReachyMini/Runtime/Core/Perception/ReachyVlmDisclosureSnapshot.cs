#nullable enable

namespace ReachyMini.Perception
{
    public sealed class VlmDisclosureSnapshot
    {
        internal VlmDisclosureSnapshot(
            VisionProviderLocation location,
            bool networkRequired,
            bool costRequired,
            string? networkDisclosure,
            string? costDisclosure,
            bool networkAcknowledged,
            bool costAcknowledged)
        {
            Location = location;
            NetworkRequired = networkRequired;
            CostRequired = costRequired;
            NetworkDisclosure = networkDisclosure;
            CostDisclosure = costDisclosure;
            NetworkAcknowledged = networkAcknowledged;
            CostAcknowledged = costAcknowledged;
        }

        public VisionProviderLocation Location { get; }

        public bool NetworkRequired { get; }

        public bool CostRequired { get; }

        public string? NetworkDisclosure { get; }

        public string? CostDisclosure { get; }

        public bool NetworkAcknowledged { get; }

        public bool CostAcknowledged { get; }

        public bool IsSatisfied =>
            (!NetworkRequired || NetworkAcknowledged) &&
            (!CostRequired || CostAcknowledged);
    }
}

#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class VlmProviderSchedulingPolicy
    {
        public VlmProviderSchedulingPolicy(
            ProviderDescriptor descriptor,
            VisionLanguageCapabilities capabilities,
            int maximumConcurrentOperations,
            int maximumRequestsPerWindow,
            long rateWindowNanoseconds,
            string? networkDisclosure,
            string? costDisclosure)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            if (descriptor.Kind != VisionProviderKind.SemanticVisionLanguage)
            {
                throw new ArgumentException(
                    "VLM scheduling policies require a semantic VLM provider.",
                    nameof(descriptor));
            }
            if (maximumConcurrentOperations <= 0 ||
                maximumConcurrentOperations > capabilities.MaximumConcurrentOperations)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrentOperations));
            }
            if (maximumRequestsPerWindow <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRequestsPerWindow));
            }
            if (rateWindowNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(rateWindowNanoseconds));
            }
            if (descriptor.RequiresNetworkDisclosure && string.IsNullOrWhiteSpace(networkDisclosure))
            {
                throw new ArgumentException(
                    "Network-backed VLM providers require explicit network disclosure text.",
                    nameof(networkDisclosure));
            }
            if (descriptor.Location == VisionProviderLocation.Cloud &&
                string.IsNullOrWhiteSpace(costDisclosure))
            {
                throw new ArgumentException(
                    "Cloud VLM providers require explicit cost disclosure text.",
                    nameof(costDisclosure));
            }

            MaximumConcurrentOperations = maximumConcurrentOperations;
            MaximumRequestsPerWindow = maximumRequestsPerWindow;
            RateWindowNanoseconds = rateWindowNanoseconds;
            NetworkDisclosure = NormalizeOptional(networkDisclosure);
            CostDisclosure = NormalizeOptional(costDisclosure);
        }

        public ProviderDescriptor Descriptor { get; }

        public VisionLanguageCapabilities Capabilities { get; }

        public int MaximumConcurrentOperations { get; }

        public int MaximumRequestsPerWindow { get; }

        public long RateWindowNanoseconds { get; }

        public string? NetworkDisclosure { get; }

        public string? CostDisclosure { get; }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public sealed class VlmSchedulerOptions
    {
        public VlmSchedulerOptions(
            long slowIntervalNanoseconds,
            string? slowIntervalPrompt)
        {
            if (slowIntervalNanoseconds < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(slowIntervalNanoseconds));
            }
            if (slowIntervalNanoseconds > 0L && string.IsNullOrWhiteSpace(slowIntervalPrompt))
            {
                throw new ArgumentException(
                    "An enabled slow interval requires an explicit scene-description prompt.",
                    nameof(slowIntervalPrompt));
            }
            if (slowIntervalNanoseconds == 0L && !string.IsNullOrWhiteSpace(slowIntervalPrompt))
            {
                throw new ArgumentException(
                    "A slow-interval prompt cannot be configured while the interval is disabled.",
                    nameof(slowIntervalPrompt));
            }

            SlowIntervalNanoseconds = slowIntervalNanoseconds;
            SlowIntervalPrompt = string.IsNullOrWhiteSpace(slowIntervalPrompt)
                ? null
                : slowIntervalPrompt.Trim();
        }

        public long SlowIntervalNanoseconds { get; }

        public string? SlowIntervalPrompt { get; }

        public bool SlowIntervalEnabled => SlowIntervalNanoseconds > 0L;

        public static VlmSchedulerOptions ExplicitTriggersOnly { get; } =
            new VlmSchedulerOptions(0L, null);
    }
}

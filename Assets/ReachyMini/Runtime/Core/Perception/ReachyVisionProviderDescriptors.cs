#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class ProviderDescriptor
    {
        public ProviderDescriptor(
            VisionProviderKind kind,
            string providerId,
            string instanceId,
            string displayName,
            string version,
            VisionProviderLocation location)
        {
            if (!Enum.IsDefined(typeof(VisionProviderKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (!Enum.IsDefined(typeof(VisionProviderLocation), location))
            {
                throw new ArgumentOutOfRangeException(nameof(location));
            }

            Kind = kind;
            ProviderId = RequireText(providerId, nameof(providerId));
            InstanceId = RequireText(instanceId, nameof(instanceId));
            DisplayName = RequireText(displayName, nameof(displayName));
            Version = RequireText(version, nameof(version));
            Location = location;
        }

        public VisionProviderKind Kind { get; }

        public string ProviderId { get; }

        public string InstanceId { get; }

        public string DisplayName { get; }

        public string Version { get; }

        public VisionProviderLocation Location { get; }

        public bool RequiresNetworkDisclosure =>
            Location == VisionProviderLocation.LocalNetwork ||
            Location == VisionProviderLocation.Cloud;

        internal static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Provider contract text cannot be empty.",
                    name);
            }
            return value;
        }
    }

    public sealed class FrameSourceCapabilities
    {
        public FrameSourceCapabilities(
            bool supportsGpuColor,
            bool supportsGpuValidityMask,
            bool supportsCancellation,
            int maximumWidth,
            int maximumHeight,
            int maximumOutstandingFrames)
        {
            if (maximumWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumWidth));
            }
            if (maximumHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHeight));
            }
            if (maximumOutstandingFrames <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutstandingFrames));
            }
            if (!supportsGpuColor || !supportsGpuValidityMask)
            {
                throw new ArgumentException(
                    "The RMA-110 frame source must expose GPU color and validity resources.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "The RMA-110 frame source must support cancellation.");
            }

            SupportsGpuColor = supportsGpuColor;
            SupportsGpuValidityMask = supportsGpuValidityMask;
            SupportsCancellation = supportsCancellation;
            MaximumWidth = maximumWidth;
            MaximumHeight = maximumHeight;
            MaximumOutstandingFrames = maximumOutstandingFrames;
        }

        public bool SupportsGpuColor { get; }

        public bool SupportsGpuValidityMask { get; }

        public bool SupportsCancellation { get; }

        public int MaximumWidth { get; }

        public int MaximumHeight { get; }

        public int MaximumOutstandingFrames { get; }
    }

    public sealed class TrackerCapabilities
    {
        public TrackerCapabilities(
            bool supportsFaces,
            bool supportsPeople,
            bool supportsObjects,
            bool supportsMotion,
            bool consumesGpuFrames,
            bool supportsCancellation,
            int maximumConcurrentOperations)
        {
            if (!supportsFaces &&
                !supportsPeople &&
                !supportsObjects &&
                !supportsMotion)
            {
                throw new ArgumentException(
                    "A tracker must declare at least one supported tracking capability.");
            }
            if (!consumesGpuFrames)
            {
                throw new ArgumentException(
                    "RMA-110 trackers must consume the transformed GPU frame contract.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "RMA-110 trackers must support cancellation.");
            }
            if (maximumConcurrentOperations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }

            SupportsFaces = supportsFaces;
            SupportsPeople = supportsPeople;
            SupportsObjects = supportsObjects;
            SupportsMotion = supportsMotion;
            ConsumesGpuFrames = consumesGpuFrames;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentOperations = maximumConcurrentOperations;
        }

        public bool SupportsFaces { get; }

        public bool SupportsPeople { get; }

        public bool SupportsObjects { get; }

        public bool SupportsMotion { get; }

        public bool ConsumesGpuFrames { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentOperations { get; }
    }

    public sealed class VisionLanguageCapabilities
    {
        public VisionLanguageCapabilities(
            bool supportsVisualQuestions,
            bool supportsSceneDescription,
            bool supportsCancellation,
            int maximumConcurrentOperations,
            int maximumPromptCharacters)
        {
            if (!supportsVisualQuestions && !supportsSceneDescription)
            {
                throw new ArgumentException(
                    "A VLM provider must declare at least one semantic capability.");
            }
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "RMA-110 VLM providers must support cancellation.");
            }
            if (maximumConcurrentOperations <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }
            if (maximumPromptCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPromptCharacters));
            }

            SupportsVisualQuestions = supportsVisualQuestions;
            SupportsSceneDescription = supportsSceneDescription;
            SupportsCancellation = supportsCancellation;
            MaximumConcurrentOperations = maximumConcurrentOperations;
            MaximumPromptCharacters = maximumPromptCharacters;
        }

        public bool SupportsVisualQuestions { get; }

        public bool SupportsSceneDescription { get; }

        public bool SupportsCancellation { get; }

        public int MaximumConcurrentOperations { get; }

        public int MaximumPromptCharacters { get; }
    }
}

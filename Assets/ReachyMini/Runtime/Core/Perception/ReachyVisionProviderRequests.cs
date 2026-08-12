#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed class FrameSourceRequest
    {
        public FrameSourceRequest(
            VisionFramePurpose purpose,
            VisionRequestContext context,
            ulong minimumSourceSequence)
        {
            if (!Enum.IsDefined(typeof(VisionFramePurpose), purpose))
            {
                throw new ArgumentOutOfRangeException(nameof(purpose));
            }
            Context = context ??
                throw new ArgumentNullException(nameof(context));
            if (context.ProviderKind != VisionProviderKind.FrameSource)
            {
                throw new ArgumentException(
                    "Frame-source requests require a frame-source provider selection.",
                    nameof(context));
            }

            Purpose = purpose;
            MinimumSourceSequence = minimumSourceSequence;
        }

        public VisionFramePurpose Purpose { get; }

        public VisionRequestContext Context { get; }

        public ulong MinimumSourceSequence { get; }
    }

    public sealed class TrackingRequest
    {
        public TrackingRequest(
            ReachyVisionFrame frame,
            VisionRequestContext context)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Context = context ??
                throw new ArgumentNullException(nameof(context));
            if (context.ProviderKind != VisionProviderKind.LightweightTracker)
            {
                throw new ArgumentException(
                    "Tracking requests require a tracker provider selection.",
                    nameof(context));
            }
        }

        public ReachyVisionFrame Frame { get; }

        public VisionRequestContext Context { get; }
    }

    public sealed class VisionLanguageRequest
    {
        public VisionLanguageRequest(
            ReachyVisionFrame frame,
            string prompt,
            VisionRequestContext context,
            bool networkDisclosureAcknowledged)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Prompt = ProviderDescriptor.RequireText(prompt, nameof(prompt));
            Context = context ??
                throw new ArgumentNullException(nameof(context));
            if (context.ProviderKind !=
                VisionProviderKind.SemanticVisionLanguage)
            {
                throw new ArgumentException(
                    "VLM requests require a semantic VLM provider selection.",
                    nameof(context));
            }

            NetworkDisclosureAcknowledged =
                networkDisclosureAcknowledged;
        }

        public ReachyVisionFrame Frame { get; }

        public string Prompt { get; }

        public VisionRequestContext Context { get; }

        public bool NetworkDisclosureAcknowledged { get; }
    }
}

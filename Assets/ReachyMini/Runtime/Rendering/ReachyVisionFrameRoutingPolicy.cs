#nullable enable

using System;

namespace ReachyMini.Rendering
{
    public enum ReachyVisionFramePurpose
    {
        Tracking = 0,
        VisionLanguage = 1,
        WorldModel = 2,
        Behavior = 3,
        Diagnostics = 4,
        ExplicitRawDebug = 5,
    }

    public sealed class ReachyVisionFrameRoute
    {
        internal ReachyVisionFrameRoute(
            ReachyVisionFramePurpose purpose,
            ReachyCameraHomographyGpuFrame? transformedFrame,
            bool allowsRawPhoneFrame)
        {
            bool rawDebug =
                purpose == ReachyVisionFramePurpose.ExplicitRawDebug;
            if (rawDebug != allowsRawPhoneFrame)
            {
                throw new ArgumentException(
                    "Raw phone access is reserved for explicit debug requests.",
                    nameof(allowsRawPhoneFrame));
            }
            if (rawDebug == (transformedFrame != null))
            {
                throw new ArgumentException(
                    "Vision routes must select either a transformed " +
                    "frame or explicit raw debug access.",
                    nameof(transformedFrame));
            }

            Purpose = purpose;
            TransformedFrame = transformedFrame;
            AllowsRawPhoneFrame = allowsRawPhoneFrame;
        }

        public ReachyVisionFramePurpose Purpose { get; }

        public ReachyCameraHomographyGpuFrame? TransformedFrame { get; }

        public bool UsesTransformedReachyEyeFrame =>
            TransformedFrame != null;

        public bool AllowsRawPhoneFrame { get; }
    }

    public static class ReachyVisionFrameRoutingPolicy
    {
        public static ReachyVisionFrameRoute Select(
            ReachyVisionFramePurpose purpose,
            ReachyCameraHomographyGpuFrame? transformedFrame)
        {
            if (!Enum.IsDefined(
                    typeof(ReachyVisionFramePurpose),
                    purpose))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(purpose),
                    purpose,
                    "Vision frame purpose is not recognized.");
            }

            if (purpose ==
                ReachyVisionFramePurpose.ExplicitRawDebug)
            {
                return new ReachyVisionFrameRoute(
                    purpose,
                    null,
                    allowsRawPhoneFrame: true);
            }

            if (transformedFrame == null)
            {
                throw new InvalidOperationException(
                    "Perception consumers require a transformed Reachy-eye frame.");
            }
            if (!transformedFrame.Coverage.HasCoverage ||
                !transformedFrame.Coverage.HasValidityMask ||
                !transformedFrame.Coverage.CanCreateVisualObservations)
            {
                throw new InvalidOperationException(
                    "The transformed Reachy-eye frame is not eligible " +
                    "for visual observations.");
            }

            return new ReachyVisionFrameRoute(
                purpose,
                transformedFrame,
                allowsRawPhoneFrame: false);
        }
    }
}

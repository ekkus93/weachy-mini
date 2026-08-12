#nullable enable

namespace ReachyMini.Perception
{
    public enum VisionProviderKind
    {
        FrameSource = 0,
        LightweightTracker = 1,
        SemanticVisionLanguage = 2,
    }

    public enum VisionProviderLocation
    {
        OnDevice = 0,
        LocalNetwork = 1,
        Cloud = 2,
    }

    public enum VisionFramePurpose
    {
        Tracking = 0,
        VisionLanguage = 1,
        WorldModel = 2,
        Behavior = 3,
        Diagnostics = 4,
        ExplicitRawDebug = 5,
    }

    public enum VisionFrameOrigin
    {
        TransformedReachyEye = 0,
        RawPhoneDebug = 1,
    }

    public enum VisionCoverageState
    {
        Normal = 0,
        Degraded = 1,
        Unusable = 2,
        Unavailable = 3,
    }

    public enum VisionResourceKind
    {
        Color = 0,
        ValidityMask = 1,
    }

    public enum VisionPixelEncoding
    {
        ProviderNative = 0,
        Rgba8 = 1,
        ValidityMask8 = 2,
        ValidityMaskFloat = 3,
    }

    public enum VisionOperationStatus
    {
        Succeeded = 0,
        NoFrame = 1,
        Unavailable = 2,
        InvalidFrame = 3,
        Cancelled = 4,
        TimedOut = 5,
        ProviderFailure = 6,
        ContractViolation = 7,
        Superseded = 8,
    }
}

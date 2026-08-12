#nullable enable

namespace ReachyMini.Perception
{
    public static class RemoteVlmReleasePolicy
    {
        public static bool AutomaticProviderFallbackEnabled => false;

        public static bool AutomaticRetryEnabled => false;

        public static bool ResponseStorageEnabled => false;

        public static bool StreamingEnabled => false;
    }

    public enum OpenAiVisionEndpointStyle
    {
        Responses = 0,
        ChatCompletions = 1,
    }

    public enum RemoteVlmImageFormat
    {
        Jpeg = 0,
        Png = 1,
        WebP = 2,
    }

    public enum RemoteVlmImageDetail
    {
        Auto = 0,
        Low = 1,
        High = 2,
    }

    public enum RemoteVlmInvalidPixelPolicy
    {
        ReplaceWithOpaqueBlack = 0,
        CropToValidBounds = 1,
    }

    public enum RemoteVlmImageEncodingStatus
    {
        Succeeded = 0,
        InvalidFrame = 1,
        Unsupported = 2,
        Cancelled = 3,
        Failed = 4,
    }

    public enum OpenAiVisionTransportStatus
    {
        Succeeded = 0,
        Cancelled = 1,
        TimedOut = 2,
        Unavailable = 3,
        InvalidRequest = 4,
        Unauthorized = 5,
        RateLimited = 6,
        ServerFailure = 7,
        ProtocolFailure = 8,
    }

    public enum OpenAiVisionProviderErrorCategory
    {
        Authentication = 0,
        Authorization = 1,
        RateLimit = 2,
        InvalidRequest = 3,
        UnsupportedCapability = 4,
        Server = 5,
        Transport = 6,
        Protocol = 7,
        Unknown = 8,
    }
}

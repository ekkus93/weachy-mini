#nullable enable

namespace ReachyMini.Perception
{
    public static class LocalVlmReleasePolicy
    {
        public static bool RequiredForFirstRelease => false;

        public static bool AutomaticModelDownloadEnabled => false;

        public static bool AutomaticProviderFallbackEnabled => false;

        public static bool CandidateBenchmarkingEnabled => false;
    }

    public enum LocalVlmArtifactSource
    {
        Bundled = 0,
        UserProvided = 1,
        DeveloperProvided = 2,
    }

    public enum LocalVlmAdapterState
    {
        Unavailable = 0,
        Available = 1,
        Faulted = 2,
        Disposed = 3,
    }

    public enum LocalVlmProviderCreationStatus
    {
        Created = 0,
        Unavailable = 1,
        InvalidConfiguration = 2,
        Cancelled = 3,
        RuntimeFailure = 4,
    }
}

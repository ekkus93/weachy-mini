#nullable enable

namespace ReachyMini.Perception
{
    public enum VlmScheduleTrigger
    {
        UserVisualQuestion = 0,
        PlannerRequest = 1,
        SignificantSceneChange = 2,
        NewEntity = 3,
        ManualRequest = 4,
        SlowInterval = 5,
    }

    public enum VlmSemanticOperation
    {
        VisualQuestion = 0,
        SceneDescription = 1,
    }

    public enum VlmScheduleStatus
    {
        Scheduled = 0,
        TriggerDisabled = 1,
        IntervalNotDue = 2,
        DuplicateSuppressed = 3,
        StaleContextRejected = 4,
        StaleTimestampRejected = 5,
        ProviderUnavailable = 6,
        CapabilityUnsupported = 7,
        DisclosureRequired = 8,
        RateLimited = 9,
        ConcurrencyLimited = 10,
        ResourceSuspended = 11,
    }

    public enum VlmContextUpdateStatus
    {
        Accepted = 0,
        StaleRejected = 1,
    }

    public enum VlmCompletionStatus
    {
        Completed = 0,
        UnknownRequest = 1,
    }
}

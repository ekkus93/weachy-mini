#nullable enable

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ReachyMini.Perception
{
    public sealed class VlmScheduleDecision
    {
        internal VlmScheduleDecision(
            VlmScheduleStatus status,
            VlmScheduleLease? lease,
            VlmDisclosureSnapshot? disclosure,
            long retryAfterNanoseconds,
            string diagnostic)
        {
            Status = status;
            Lease = lease;
            Disclosure = disclosure;
            RetryAfterNanoseconds = retryAfterNanoseconds;
            Diagnostic = diagnostic;
        }

        public VlmScheduleStatus Status { get; }

        public VlmScheduleLease? Lease { get; }

        public VlmDisclosureSnapshot? Disclosure { get; }

        public long RetryAfterNanoseconds { get; }

        public string Diagnostic { get; }

        public bool Scheduled => Status == VlmScheduleStatus.Scheduled;
    }

    public sealed class VlmContextUpdateResult
    {
        internal VlmContextUpdateResult(
            VlmContextUpdateStatus status,
            int cancelledRequestCount,
            ulong sceneRevision,
            ulong questionRevision,
            string diagnostic)
        {
            Status = status;
            CancelledRequestCount = cancelledRequestCount;
            SceneRevision = sceneRevision;
            QuestionRevision = questionRevision;
            Diagnostic = diagnostic;
        }

        public VlmContextUpdateStatus Status { get; }

        public int CancelledRequestCount { get; }

        public ulong SceneRevision { get; }

        public ulong QuestionRevision { get; }

        public string Diagnostic { get; }
    }

    public sealed class VlmCompletionResult
    {
        internal VlmCompletionResult(
            VlmCompletionStatus status,
            bool wasCancellationRequested,
            string diagnostic)
        {
            Status = status;
            WasCancellationRequested = wasCancellationRequested;
            Diagnostic = diagnostic;
        }

        public VlmCompletionStatus Status { get; }

        public bool WasCancellationRequested { get; }

        public string Diagnostic { get; }
    }

    public sealed class VlmProviderSchedulerSnapshot
    {
        internal VlmProviderSchedulerSnapshot(
            string providerInstanceId,
            int activeRequestCount,
            int recentAdmissionCount,
            long lastSlowIntervalTimestampNanoseconds)
        {
            ProviderInstanceId = providerInstanceId;
            ActiveRequestCount = activeRequestCount;
            RecentAdmissionCount = recentAdmissionCount;
            LastSlowIntervalTimestampNanoseconds = lastSlowIntervalTimestampNanoseconds;
        }

        public string ProviderInstanceId { get; }

        public int ActiveRequestCount { get; }

        public int RecentAdmissionCount { get; }

        public long LastSlowIntervalTimestampNanoseconds { get; }
    }

    public sealed class VlmSchedulerDiagnosticsSnapshot
    {
        internal VlmSchedulerDiagnosticsSnapshot(
            long scheduledRequestCount,
            long duplicateSuppressionCount,
            long staleContextRejectionCount,
            long disclosureRejectionCount,
            long rateLimitRejectionCount,
            long concurrencyRejectionCount,
            long cancellationRequestCount,
            long cancellationCallbackFailureCount,
            long staleTimestampRejectionCount,
            long completedRequestCount,
            long unknownCompletionCount)
        {
            ScheduledRequestCount = scheduledRequestCount;
            DuplicateSuppressionCount = duplicateSuppressionCount;
            StaleContextRejectionCount = staleContextRejectionCount;
            DisclosureRejectionCount = disclosureRejectionCount;
            RateLimitRejectionCount = rateLimitRejectionCount;
            ConcurrencyRejectionCount = concurrencyRejectionCount;
            CancellationRequestCount = cancellationRequestCount;
            CancellationCallbackFailureCount = cancellationCallbackFailureCount;
            StaleTimestampRejectionCount = staleTimestampRejectionCount;
            CompletedRequestCount = completedRequestCount;
            UnknownCompletionCount = unknownCompletionCount;
        }

        public long ScheduledRequestCount { get; }

        public long DuplicateSuppressionCount { get; }

        public long StaleContextRejectionCount { get; }

        public long DisclosureRejectionCount { get; }

        public long RateLimitRejectionCount { get; }

        public long ConcurrencyRejectionCount { get; }

        public long CancellationRequestCount { get; }

        public long CancellationCallbackFailureCount { get; }

        public long StaleTimestampRejectionCount { get; }

        public long CompletedRequestCount { get; }

        public long UnknownCompletionCount { get; }
    }

    public sealed class VlmSchedulerSnapshot
    {
        private readonly ReadOnlyCollection<VlmProviderSchedulerSnapshot> providers;

        internal VlmSchedulerSnapshot(
            ulong sceneRevision,
            ulong questionRevision,
            IReadOnlyList<VlmProviderSchedulerSnapshot> providers,
            VlmSchedulerDiagnosticsSnapshot diagnostics)
        {
            SceneRevision = sceneRevision;
            QuestionRevision = questionRevision;
            this.providers = new List<VlmProviderSchedulerSnapshot>(providers).AsReadOnly();
            Diagnostics = diagnostics;
        }

        public ulong SceneRevision { get; }

        public ulong QuestionRevision { get; }

        public IReadOnlyList<VlmProviderSchedulerSnapshot> Providers => providers;

        public VlmSchedulerDiagnosticsSnapshot Diagnostics { get; }
    }
}

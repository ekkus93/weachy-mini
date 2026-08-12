#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

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

    public sealed class VlmScheduleLease
    {
        private readonly CancellationTokenSource cancellation;
        private readonly CancellationToken cancellationToken;
        private readonly object cancellationSync = new object();
        private bool cancellationRequested;
        private bool cancellationDispatchStarted;
        private bool cancellationDispatchCompleted;
        private bool cancellationDisposed;
        private bool cancellationDisposalRequested;
        private int cancellationDispatchThreadId;

        internal VlmScheduleLease(
            string requestId,
            VlmScheduleSignal signal,
            long scheduledTimestampNanoseconds,
            VlmDisclosureSnapshot disclosure,
            CancellationTokenSource cancellation)
        {
            RequestId = requestId;
            ProviderInstanceId = signal.ProviderInstanceId;
            Trigger = signal.Trigger;
            Operation = signal.Operation;
            TriggerSequence = signal.TriggerSequence;
            SceneRevision = signal.SceneRevision;
            QuestionRevision = signal.Operation == VlmSemanticOperation.VisualQuestion
                ? signal.QuestionRevision
                : 0UL;
            Prompt = signal.Prompt;
            ScheduledTimestampNanoseconds = scheduledTimestampNanoseconds;
            Disclosure = disclosure;
            this.cancellation = cancellation;
            cancellationToken = cancellation.Token;
        }

        public string RequestId { get; }

        public string ProviderInstanceId { get; }

        public VlmScheduleTrigger Trigger { get; }

        public VlmSemanticOperation Operation { get; }

        public ulong TriggerSequence { get; }

        public ulong SceneRevision { get; }

        public ulong QuestionRevision { get; }

        public string Prompt { get; }

        public long ScheduledTimestampNanoseconds { get; }

        public VlmDisclosureSnapshot Disclosure { get; }

        public CancellationToken CancellationToken => cancellationToken;

        public bool IsCancellationRequested
        {
            get
            {
                lock (cancellationSync)
                {
                    return cancellationRequested;
                }
            }
        }

        internal bool MarkCancellationRequested()
        {
            lock (cancellationSync)
            {
                if (cancellationDisposed || cancellationRequested)
                {
                    return false;
                }
                cancellationRequested = true;
                return true;
            }
        }

        internal bool DispatchCancellation()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            lock (cancellationSync)
            {
                if (cancellationDisposed ||
                    !cancellationRequested ||
                    cancellationDispatchStarted)
                {
                    return false;
                }
                cancellationDispatchStarted = true;
                cancellationDispatchThreadId = currentThreadId;
            }

            bool callbackFailure = false;
            try
            {
                cancellation.Cancel();
            }
            catch (AggregateException)
            {
                callbackFailure = true;
            }
            finally
            {
                lock (cancellationSync)
                {
                    cancellationDispatchCompleted = true;
                    cancellationDispatchThreadId = 0;
                    if (cancellationDisposalRequested && !cancellationDisposed)
                    {
                        cancellation.Dispose();
                        cancellationDisposed = true;
                    }
                    Monitor.PulseAll(cancellationSync);
                }
            }
            return callbackFailure;
        }

        internal void DisposeCancellation()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            lock (cancellationSync)
            {
                if (cancellationDisposed)
                {
                    return;
                }
                if (cancellationDispatchStarted && !cancellationDispatchCompleted)
                {
                    cancellationDisposalRequested = true;
                    if (cancellationDispatchThreadId == currentThreadId)
                    {
                        return;
                    }
                    while (!cancellationDispatchCompleted && !cancellationDisposed)
                    {
                        Monitor.Wait(cancellationSync);
                    }
                    if (cancellationDisposed)
                    {
                        return;
                    }
                }
                cancellation.Dispose();
                cancellationDisposed = true;
            }
        }
    }

    public sealed class ReachyVlmScheduler : IDisposable
    {
        public const int MaximumProviderPolicies = 16;

        private sealed class ProviderState
        {
            internal ProviderState(VlmProviderSchedulingPolicy policy, long startTimestampNanoseconds)
            {
                Policy = policy;
                LastAcceptedTriggerSequence = new ulong[Enum.GetValues(typeof(VlmScheduleTrigger)).Length];
                LastSlowIntervalTimestampNanoseconds = startTimestampNanoseconds;
                LastAdmittedTimestampNanoseconds = startTimestampNanoseconds;
            }

            internal VlmProviderSchedulingPolicy Policy { get; }

            internal Queue<long> AdmissionTimestamps { get; } = new Queue<long>();

            internal Dictionary<string, VlmScheduleLease> ActiveRequests { get; } =
                new Dictionary<string, VlmScheduleLease>(StringComparer.Ordinal);

            internal ulong[] LastAcceptedTriggerSequence { get; }

            internal long LastSlowIntervalTimestampNanoseconds { get; set; }

            internal long LastAdmittedTimestampNanoseconds { get; set; }
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, ProviderState> providers;
        private readonly VlmSchedulerOptions options;
        private ulong sceneRevision;
        private ulong questionRevision;
        private ulong nextRequestSequence;
        private long scheduledRequestCount;
        private long duplicateSuppressionCount;
        private long staleContextRejectionCount;
        private long disclosureRejectionCount;
        private long rateLimitRejectionCount;
        private long concurrencyRejectionCount;
        private long cancellationRequestCount;
        private long cancellationCallbackFailureCount;
        private long staleTimestampRejectionCount;
        private long completedRequestCount;
        private long unknownCompletionCount;
        private bool disposed;

        public ReachyVlmScheduler(
            IReadOnlyList<VlmProviderSchedulingPolicy> providerPolicies,
            VlmSchedulerOptions options,
            ulong initialSceneRevision,
            ulong initialQuestionRevision,
            long startTimestampNanoseconds)
        {
            if (providerPolicies == null)
            {
                throw new ArgumentNullException(nameof(providerPolicies));
            }
            if (providerPolicies.Count <= 0 || providerPolicies.Count > MaximumProviderPolicies)
            {
                throw new ArgumentOutOfRangeException(nameof(providerPolicies));
            }
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (initialSceneRevision == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSceneRevision));
            }
            if (startTimestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(startTimestampNanoseconds));
            }

            sceneRevision = initialSceneRevision;
            questionRevision = initialQuestionRevision;
            providers = new Dictionary<string, ProviderState>(
                providerPolicies.Count,
                StringComparer.Ordinal);
            for (int index = 0; index < providerPolicies.Count; ++index)
            {
                VlmProviderSchedulingPolicy policy = providerPolicies[index] ??
                    throw new ArgumentException(
                        "Provider policy collections cannot contain null entries.",
                        nameof(providerPolicies));
                if (!providers.TryAdd(
                        policy.Descriptor.InstanceId,
                        new ProviderState(policy, startTimestampNanoseconds)))
                {
                    throw new ArgumentException(
                        "Provider instance IDs must be unique.",
                        nameof(providerPolicies));
                }
            }
        }

        public VlmContextUpdateResult UpdateContext(
            ulong nextSceneRevision,
            ulong nextQuestionRevision)
        {
            if (nextSceneRevision == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(nextSceneRevision));
            }

            var obsoleteLeases = new List<VlmScheduleLease>();
            ulong appliedSceneRevision;
            ulong appliedQuestionRevision;
            lock (sync)
            {
                ThrowIfDisposed();
                if (nextSceneRevision < sceneRevision ||
                    nextQuestionRevision < questionRevision)
                {
                    staleContextRejectionCount = checked(staleContextRejectionCount + 1L);
                    return new VlmContextUpdateResult(
                        VlmContextUpdateStatus.StaleRejected,
                        0,
                        sceneRevision,
                        questionRevision,
                        "Scene and question revisions cannot regress.");
                }

                bool sceneChanged = nextSceneRevision != sceneRevision;
                bool questionChanged = nextQuestionRevision != questionRevision;
                if (sceneChanged || questionChanged)
                {
                    foreach (ProviderState state in providers.Values)
                    {
                        foreach (VlmScheduleLease lease in state.ActiveRequests.Values)
                        {
                            bool obsolete = sceneChanged ||
                                (questionChanged && lease.QuestionRevision != 0UL);
                            if (obsolete && lease.MarkCancellationRequested())
                            {
                                obsoleteLeases.Add(lease);
                            }
                        }
                    }
                }

                sceneRevision = nextSceneRevision;
                questionRevision = nextQuestionRevision;
                appliedSceneRevision = sceneRevision;
                appliedQuestionRevision = questionRevision;
                cancellationRequestCount = checked(
                    cancellationRequestCount + obsoleteLeases.Count);
            }

            int callbackFailures = 0;
            for (int index = 0; index < obsoleteLeases.Count; ++index)
            {
                if (obsoleteLeases[index].DispatchCancellation())
                {
                    callbackFailures = checked(callbackFailures + 1);
                }
            }
            if (callbackFailures > 0)
            {
                lock (sync)
                {
                    if (!disposed)
                    {
                        cancellationCallbackFailureCount = checked(
                            cancellationCallbackFailureCount + callbackFailures);
                    }
                }
            }

            return new VlmContextUpdateResult(
                VlmContextUpdateStatus.Accepted,
                obsoleteLeases.Count,
                appliedSceneRevision,
                appliedQuestionRevision,
                callbackFailures == 0
                    ? (obsoleteLeases.Count == 0
                        ? "Scheduling context updated without obsolete requests."
                        : "Scheduling context updated and obsolete VLM requests were cancelled.")
                    : "Obsolete requests were cancelled, but one or more cancellation callbacks failed.");
        }

        public VlmScheduleDecision TrySchedule(
            VlmScheduleSignal signal,
            long timestampNanoseconds)
        {
            if (signal == null)
            {
                throw new ArgumentNullException(nameof(signal));
            }
            if (timestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(timestampNanoseconds));
            }

            lock (sync)
            {
                ThrowIfDisposed();
                if (signal.SceneRevision != sceneRevision ||
                    signal.QuestionRevision != questionRevision)
                {
                    staleContextRejectionCount = checked(staleContextRejectionCount + 1L);
                    return Decision(
                        VlmScheduleStatus.StaleContextRejected,
                        null,
                        null,
                        0L,
                        "The schedule signal does not match the current scene and question revisions.");
                }
                if (!providers.TryGetValue(signal.ProviderInstanceId, out ProviderState? state))
                {
                    return Decision(
                        VlmScheduleStatus.ProviderUnavailable,
                        null,
                        null,
                        0L,
                        "The requested VLM provider is not registered; no fallback provider was selected.");
                }
                if (timestampNanoseconds < state.LastAdmittedTimestampNanoseconds)
                {
                    staleTimestampRejectionCount = checked(staleTimestampRejectionCount + 1L);
                    return Decision(
                        VlmScheduleStatus.StaleTimestampRejected,
                        null,
                        Disclosure(state.Policy, signal),
                        0L,
                        "Scheduling timestamps cannot regress behind the provider's last admitted request.");
                }
                if (!Supports(state.Policy.Capabilities, signal.Operation))
                {
                    return Decision(
                        VlmScheduleStatus.CapabilityUnsupported,
                        null,
                        Disclosure(state.Policy, signal),
                        0L,
                        "The selected provider does not support the requested semantic operation.");
                }
                if (signal.Prompt.Length > state.Policy.Capabilities.MaximumPromptCharacters)
                {
                    return Decision(
                        VlmScheduleStatus.CapabilityUnsupported,
                        null,
                        Disclosure(state.Policy, signal),
                        0L,
                        "The prompt exceeds the selected provider's declared character limit.");
                }

                if (signal.Trigger == VlmScheduleTrigger.SlowInterval)
                {
                    if (!options.SlowIntervalEnabled)
                    {
                        return Decision(
                            VlmScheduleStatus.TriggerDisabled,
                            null,
                            Disclosure(state.Policy, signal),
                            0L,
                            "Slow-interval VLM scheduling is disabled by default.");
                    }
                    if (!string.Equals(
                            signal.Prompt,
                            options.SlowIntervalPrompt,
                            StringComparison.Ordinal))
                    {
                        return Decision(
                            VlmScheduleStatus.CapabilityUnsupported,
                            null,
                            Disclosure(state.Policy, signal),
                            0L,
                            "Slow-interval scheduling requires the configured bounded prompt.");
                    }
                    long elapsed = checked(
                        timestampNanoseconds - state.LastSlowIntervalTimestampNanoseconds);
                    if (elapsed < options.SlowIntervalNanoseconds)
                    {
                        return Decision(
                            VlmScheduleStatus.IntervalNotDue,
                            null,
                            Disclosure(state.Policy, signal),
                            options.SlowIntervalNanoseconds - elapsed,
                            "The configured slow interval is not due.");
                    }
                }

                int triggerIndex = (int)signal.Trigger;
                if (signal.TriggerSequence <= state.LastAcceptedTriggerSequence[triggerIndex])
                {
                    duplicateSuppressionCount = checked(duplicateSuppressionCount + 1L);
                    return Decision(
                        VlmScheduleStatus.DuplicateSuppressed,
                        null,
                        Disclosure(state.Policy, signal),
                        0L,
                        "The trigger sequence was already admitted or is older than the admitted trigger.");
                }

                VlmDisclosureSnapshot disclosure = Disclosure(state.Policy, signal);
                if (!disclosure.IsSatisfied)
                {
                    disclosureRejectionCount = checked(disclosureRejectionCount + 1L);
                    return Decision(
                        VlmScheduleStatus.DisclosureRequired,
                        null,
                        disclosure,
                        0L,
                        "Required network or cloud-cost disclosure has not been acknowledged.");
                }

                int admissionCount = CountAdmissionsInWindow(state, timestampNanoseconds);
                if (admissionCount >= state.Policy.MaximumRequestsPerWindow)
                {
                    rateLimitRejectionCount = checked(rateLimitRejectionCount + 1L);
                    long oldest = state.AdmissionTimestamps.Peek();
                    long retry = checked(
                        state.Policy.RateWindowNanoseconds -
                        (timestampNanoseconds - oldest));
                    return Decision(
                        VlmScheduleStatus.RateLimited,
                        null,
                        disclosure,
                        retry > 0L ? retry : 1L,
                        "The selected provider reached its explicit scheduling rate limit.");
                }
                if (state.ActiveRequests.Count >= state.Policy.MaximumConcurrentOperations)
                {
                    concurrencyRejectionCount = checked(concurrencyRejectionCount + 1L);
                    return Decision(
                        VlmScheduleStatus.ConcurrencyLimited,
                        null,
                        disclosure,
                        0L,
                        "The selected provider reached its explicit concurrency limit.");
                }

                PruneAdmissions(state, timestampNanoseconds);
                nextRequestSequence = checked(nextRequestSequence + 1UL);
                string requestId = "vlm-schedule-" + nextRequestSequence.ToString("D20", System.Globalization.CultureInfo.InvariantCulture);
                var cancellation = new CancellationTokenSource();
                var lease = new VlmScheduleLease(
                    requestId,
                    signal,
                    timestampNanoseconds,
                    disclosure,
                    cancellation);
                state.ActiveRequests.Add(requestId, lease);
                state.AdmissionTimestamps.Enqueue(timestampNanoseconds);
                state.LastAcceptedTriggerSequence[triggerIndex] = signal.TriggerSequence;
                state.LastAdmittedTimestampNanoseconds = timestampNanoseconds;
                if (signal.Trigger == VlmScheduleTrigger.SlowInterval)
                {
                    state.LastSlowIntervalTimestampNanoseconds = timestampNanoseconds;
                }
                scheduledRequestCount = checked(scheduledRequestCount + 1L);
                return Decision(
                    VlmScheduleStatus.Scheduled,
                    lease,
                    disclosure,
                    0L,
                    "The VLM request was admitted by the bounded scheduling policy.");
            }
        }

        public VlmCompletionResult Complete(string requestId)
        {
            requestId = ProviderDescriptor.RequireText(requestId, nameof(requestId));
            VlmScheduleLease? completedLease = null;
            lock (sync)
            {
                ThrowIfDisposed();
                foreach (ProviderState state in providers.Values)
                {
                    if (state.ActiveRequests.TryGetValue(requestId, out VlmScheduleLease? lease))
                    {
                        state.ActiveRequests.Remove(requestId);
                        completedRequestCount = checked(completedRequestCount + 1L);
                        completedLease = lease;
                        break;
                    }
                }

                if (completedLease == null)
                {
                    unknownCompletionCount = checked(unknownCompletionCount + 1L);
                    return new VlmCompletionResult(
                        VlmCompletionStatus.UnknownRequest,
                        false,
                        "The completion request ID is not active.");
                }
            }

            bool cancelled = completedLease.IsCancellationRequested;
            completedLease.DisposeCancellation();
            return new VlmCompletionResult(
                VlmCompletionStatus.Completed,
                cancelled,
                cancelled
                    ? "The cancelled VLM request completed and released its concurrency slot."
                    : "The VLM request completed and released its concurrency slot.");
        }

        public VlmSchedulerSnapshot GetSnapshot(long timestampNanoseconds)
        {
            if (timestampNanoseconds <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(timestampNanoseconds));
            }
            lock (sync)
            {
                ThrowIfDisposed();
                foreach (ProviderState state in providers.Values)
                {
                    if (timestampNanoseconds < state.LastAdmittedTimestampNanoseconds)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(timestampNanoseconds),
                            "Scheduler snapshots cannot move behind an admitted request timestamp.");
                    }
                }
                var snapshots = new List<VlmProviderSchedulerSnapshot>(providers.Count);
                foreach (ProviderState state in providers.Values)
                {
                    snapshots.Add(new VlmProviderSchedulerSnapshot(
                        state.Policy.Descriptor.InstanceId,
                        state.ActiveRequests.Count,
                        CountAdmissionsInWindow(state, timestampNanoseconds),
                        state.LastSlowIntervalTimestampNanoseconds));
                }
                snapshots.Sort((left, right) => string.CompareOrdinal(
                    left.ProviderInstanceId,
                    right.ProviderInstanceId));
                return new VlmSchedulerSnapshot(
                    sceneRevision,
                    questionRevision,
                    snapshots,
                    Diagnostics());
            }
        }

        public void Dispose()
        {
            var leases = new List<VlmScheduleLease>();
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                foreach (ProviderState state in providers.Values)
                {
                    foreach (VlmScheduleLease lease in state.ActiveRequests.Values)
                    {
                        _ = lease.MarkCancellationRequested();
                        leases.Add(lease);
                    }
                    state.ActiveRequests.Clear();
                    state.AdmissionTimestamps.Clear();
                }
            }

            for (int index = 0; index < leases.Count; ++index)
            {
                _ = leases[index].DispatchCancellation();
                leases[index].DisposeCancellation();
            }
        }

        private static bool Supports(
            VisionLanguageCapabilities capabilities,
            VlmSemanticOperation operation)
        {
            return operation == VlmSemanticOperation.VisualQuestion
                ? capabilities.SupportsVisualQuestions
                : capabilities.SupportsSceneDescription;
        }

        private static VlmDisclosureSnapshot Disclosure(
            VlmProviderSchedulingPolicy policy,
            VlmScheduleSignal signal)
        {
            bool networkRequired = policy.Descriptor.RequiresNetworkDisclosure;
            bool costRequired = policy.Descriptor.Location == VisionProviderLocation.Cloud;
            return new VlmDisclosureSnapshot(
                policy.Descriptor.Location,
                networkRequired,
                costRequired,
                policy.NetworkDisclosure,
                policy.CostDisclosure,
                signal.NetworkDisclosureAcknowledged,
                signal.CostDisclosureAcknowledged);
        }

        private static int CountAdmissionsInWindow(
            ProviderState state,
            long timestampNanoseconds)
        {
            long minimumTimestamp = checked(
                timestampNanoseconds - state.Policy.RateWindowNanoseconds);
            int count = 0;
            foreach (long admitted in state.AdmissionTimestamps)
            {
                if (admitted > minimumTimestamp)
                {
                    count = checked(count + 1);
                }
            }
            return count;
        }

        private static void PruneAdmissions(
            ProviderState state,
            long timestampNanoseconds)
        {
            long minimumTimestamp = checked(
                timestampNanoseconds - state.Policy.RateWindowNanoseconds);
            while (state.AdmissionTimestamps.Count > 0 &&
                state.AdmissionTimestamps.Peek() <= minimumTimestamp)
            {
                state.AdmissionTimestamps.Dequeue();
            }
        }

        private static VlmScheduleDecision Decision(
            VlmScheduleStatus status,
            VlmScheduleLease? lease,
            VlmDisclosureSnapshot? disclosure,
            long retryAfterNanoseconds,
            string diagnostic)
        {
            return new VlmScheduleDecision(
                status,
                lease,
                disclosure,
                retryAfterNanoseconds,
                diagnostic);
        }

        private VlmSchedulerDiagnosticsSnapshot Diagnostics()
        {
            return new VlmSchedulerDiagnosticsSnapshot(
                scheduledRequestCount,
                duplicateSuppressionCount,
                staleContextRejectionCount,
                disclosureRejectionCount,
                rateLimitRejectionCount,
                concurrencyRejectionCount,
                cancellationRequestCount,
                cancellationCallbackFailureCount,
                staleTimestampRejectionCount,
                completedRequestCount,
                unknownCompletionCount);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReachyVlmScheduler));
            }
        }
    }
}

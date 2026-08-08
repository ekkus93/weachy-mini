#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Language
{
    public enum LocalLlmProviderState
    {
        Created = 0,
        Loading = 1,
        Ready = 2,
        Busy = 3,
        Unavailable = 4,
        Faulted = 5,
        Disposed = 6,
    }

    public enum LocalLlmFailure
    {
        None = 0,
        Busy = 1,
        Unavailable = 2,
        InvalidRequest = 3,
        AbiMismatch = 4,
        ModelMismatch = 5,
        ContextLimit = 6,
        OutputLimit = 7,
        RuntimeFailure = 8,
        InvalidIntent = 9,
        Cancelled = 10,
        TimedOut = 11,
        Disposed = 12,
    }

    public enum LocalLlmEventKind
    {
        OutputDelta = 0,
        Completed = 1,
        Cancelled = 2,
        Failed = 3,
    }

    public sealed class LocalLlmProviderDescriptor
    {
        public LocalLlmProviderDescriptor(
            string providerId,
            string modelId,
            string displayName)
        {
            ProviderId = RequireText(providerId, nameof(providerId), 128);
            ModelId = RequireText(modelId, nameof(modelId), 128);
            DisplayName = RequireText(displayName, nameof(displayName), 256);
            IsOnDevice = true;
            RequiresNetwork = false;
        }

        public string ProviderId { get; }

        public string ModelId { get; }

        public string DisplayName { get; }

        public bool IsOnDevice { get; }

        public bool RequiresNetwork { get; }

        private static string RequireText(string value, string name, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            {
                throw new ArgumentException(
                    $"{name} must contain 1-{maximumLength} characters.",
                    name);
            }
            return value;
        }
    }

    public sealed class LocalLlmProviderCapabilities
    {
        public LocalLlmProviderCapabilities(
            uint contextTokens,
            uint maximumOutputTokens,
            uint threads,
            uint batchThreads,
            uint streamQueueCapacity)
        {
            if (contextTokens == 0U || maximumOutputTokens == 0U ||
                maximumOutputTokens >= contextTokens || threads == 0U ||
                batchThreads == 0U || streamQueueCapacity == 0U)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contextTokens),
                    "Local LLM capabilities contain an invalid execution limit.");
            }
            ContextTokens = contextTokens;
            MaximumOutputTokens = maximumOutputTokens;
            Threads = threads;
            BatchThreads = batchThreads;
            StreamQueueCapacity = streamQueueCapacity;
            SupportsStreaming = true;
            SupportsCancellation = true;
            SupportsConversationReset = true;
        }

        public uint ContextTokens { get; }

        public uint MaximumOutputTokens { get; }

        public uint Threads { get; }

        public uint BatchThreads { get; }

        public uint StreamQueueCapacity { get; }

        public bool SupportsStreaming { get; }

        public bool SupportsCancellation { get; }

        public bool SupportsConversationReset { get; }
    }

    public sealed class LocalLlmProviderAvailability
    {
        public LocalLlmProviderAvailability(
            LocalLlmProviderState state,
            LocalLlmFailure failure,
            string detail,
            ulong revision)
        {
            State = state;
            Failure = failure;
            Detail = detail ?? string.Empty;
            Revision = revision;
        }

        public LocalLlmProviderState State { get; }

        public LocalLlmFailure Failure { get; }

        public string Detail { get; }

        public ulong Revision { get; }

        public bool Available =>
            State == LocalLlmProviderState.Ready ||
            State == LocalLlmProviderState.Busy;
    }

    public sealed class LocalLlmRequest
    {
        public const int MaximumRequestIdCharacters = 128;
        public const int MaximumUserTextCharacters = 8192;
        public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(5.0);

        public LocalLlmRequest(
            string requestId,
            string userText,
            TimeSpan timeout,
            IEnumerable<string>? validTrackedEntityIds = null)
        {
            if (string.IsNullOrWhiteSpace(requestId) ||
                requestId.Length > MaximumRequestIdCharacters)
            {
                throw new ArgumentException(
                    "A local LLM request requires a bounded request identifier.",
                    nameof(requestId));
            }
            if (string.IsNullOrWhiteSpace(userText) ||
                userText.Length > MaximumUserTextCharacters)
            {
                throw new ArgumentException(
                    "Local LLM user text must contain 1-8192 characters.",
                    nameof(userText));
            }
            if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Local LLM timeout must be in (0, 5 minutes].");
            }
            RequestId = requestId;
            UserText = userText;
            Timeout = timeout;
            ValidTrackedEntityIds = CopyValidTrackedEntityIds(validTrackedEntityIds);
        }

        public string RequestId { get; }

        public string UserText { get; }

        public TimeSpan Timeout { get; }

        public IReadOnlyList<string> ValidTrackedEntityIds { get; }

        internal bool IsTrackedEntityAllowed(string entityId)
        {
            for (int index = 0; index < ValidTrackedEntityIds.Count; ++index)
            {
                if (string.Equals(
                        ValidTrackedEntityIds[index],
                        entityId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string[] CopyValidTrackedEntityIds(
            IEnumerable<string>? validTrackedEntityIds)
        {
            if (validTrackedEntityIds == null)
            {
                return Array.Empty<string>();
            }
            var result = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entityId in validTrackedEntityIds)
            {
                if (!LocalLlmBehaviorIntentParser.IsEntityId(entityId))
                {
                    throw new ArgumentException(
                        "Valid tracked-entity IDs must use entity-N syntax.",
                        nameof(validTrackedEntityIds));
                }
                if (!unique.Add(entityId))
                {
                    throw new ArgumentException(
                        "Valid tracked-entity IDs cannot contain duplicates.",
                        nameof(validTrackedEntityIds));
                }
                result.Add(entityId);
                if (result.Count > 128)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(validTrackedEntityIds),
                        "A local LLM request may authorize at most 128 tracked entities.");
                }
            }
            return result.ToArray();
        }
    }

    public sealed class LocalLlmEvent
    {
        private LocalLlmEvent(
            ulong sequence,
            LocalLlmEventKind kind,
            string outputDelta,
            LocalLlmBehaviorIntent? intent,
            LocalLlmFailure failure,
            string detail)
        {
            Sequence = sequence;
            Kind = kind;
            OutputDelta = outputDelta;
            Intent = intent;
            Failure = failure;
            Detail = detail;
        }

        public ulong Sequence { get; }

        public LocalLlmEventKind Kind { get; }

        public string OutputDelta { get; }

        public LocalLlmBehaviorIntent? Intent { get; }

        public LocalLlmFailure Failure { get; }

        public string Detail { get; }

        public bool IsTerminal => Kind != LocalLlmEventKind.OutputDelta;

        internal static LocalLlmEvent Delta(ulong sequence, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException(
                    "A local LLM output delta cannot be empty.",
                    nameof(text));
            }
            return new LocalLlmEvent(
                sequence,
                LocalLlmEventKind.OutputDelta,
                text,
                null,
                LocalLlmFailure.None,
                string.Empty);
        }

        internal static LocalLlmEvent Completed(
            ulong sequence,
            LocalLlmBehaviorIntent intent)
        {
            return new LocalLlmEvent(
                sequence,
                LocalLlmEventKind.Completed,
                string.Empty,
                intent ?? throw new ArgumentNullException(nameof(intent)),
                LocalLlmFailure.None,
                string.Empty);
        }

        internal static LocalLlmEvent Cancelled(ulong sequence, string detail)
        {
            return new LocalLlmEvent(
                sequence,
                LocalLlmEventKind.Cancelled,
                string.Empty,
                null,
                LocalLlmFailure.Cancelled,
                detail ?? string.Empty);
        }

        internal static LocalLlmEvent Failed(
            ulong sequence,
            LocalLlmFailure failure,
            string detail)
        {
            if (failure == LocalLlmFailure.None || failure == LocalLlmFailure.Cancelled)
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }
            return new LocalLlmEvent(
                sequence,
                LocalLlmEventKind.Failed,
                string.Empty,
                null,
                failure,
                detail ?? string.Empty);
        }
    }

    public sealed class LocalLlmOperationResult
    {
        private LocalLlmOperationResult(
            bool succeeded,
            LocalLlmFailure failure,
            string detail)
        {
            Succeeded = succeeded;
            Failure = failure;
            Detail = detail;
        }

        public bool Succeeded { get; }

        public LocalLlmFailure Failure { get; }

        public string Detail { get; }

        internal static LocalLlmOperationResult Success(string detail)
        {
            return new LocalLlmOperationResult(
                true,
                LocalLlmFailure.None,
                detail ?? string.Empty);
        }

        internal static LocalLlmOperationResult Failed(
            LocalLlmFailure failure,
            string detail)
        {
            if (failure == LocalLlmFailure.None)
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }
            return new LocalLlmOperationResult(
                false,
                failure,
                detail ?? string.Empty);
        }
    }

    public interface ILocalLlmProvider : IAsyncDisposable
    {
        LocalLlmProviderDescriptor Descriptor { get; }

        LocalLlmProviderCapabilities Capabilities { get; }

        LocalLlmProviderAvailability Availability { get; }

        ValueTask<LocalLlmOperationResult> LoadAsync(
            CancellationToken cancellationToken);

        ValueTask<LocalLlmOperationResult> ReloadAsync(
            CancellationToken cancellationToken);

        IAsyncEnumerable<LocalLlmEvent> GenerateAsync(
            LocalLlmRequest request,
            CancellationToken cancellationToken);

        ValueTask<LocalLlmOperationResult> ResetConversationAsync(
            CancellationToken cancellationToken);
    }
}

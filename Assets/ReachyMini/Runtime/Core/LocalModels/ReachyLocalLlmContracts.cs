#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public static class LocalLlmProviderPolicy
    {
        public const int MaximumConversationMessages = 32;
        public const int MaximumMessageCharacters = 4096;
        public const int MaximumRequestIdCharacters = 128;
        public const int MaximumDiagnosticCharacters = 384;
        public const int DefaultMaximumResponseUtf8Bytes = 16 * 1024;
    }

    public enum LocalLlmProviderState
    {
        Unavailable = 0,
        Loading = 1,
        Ready = 2,
        Generating = 3,
        Faulted = 4,
        Disposed = 5,
    }

    public enum LocalLlmProviderCreationStatus
    {
        Created = 0,
        InvalidConfiguration = 1,
        Unavailable = 2,
        Cancelled = 3,
        RuntimeFailure = 4,
    }

    public enum LocalLlmReloadStatus
    {
        Reloaded = 0,
        Busy = 1,
        InvalidConfiguration = 2,
        Unavailable = 3,
        Cancelled = 4,
        RuntimeFailure = 5,
        Disposed = 6,
    }

    public enum LocalLlmGenerationStatus
    {
        Succeeded = 0,
        Busy = 1,
        InvalidRequest = 2,
        ContextLimit = 3,
        OutputLimit = 4,
        Cancelled = 5,
        Superseded = 6,
        InvalidIntent = 7,
        RuntimeFailure = 8,
        ConsumerFailure = 9,
        Unavailable = 10,
        Disposed = 11,
        ResourceExhausted = 12,
    }

    public enum LocalLlmChatRole
    {
        User = 0,
        Assistant = 1,
    }

    public enum LocalLlmStreamEventType
    {
        Text = 0,
        Completed = 1,
        Cancelled = 2,
        Superseded = 3,
        Error = 4,
    }

    public enum LocalLlmExpression
    {
        Neutral = 0,
        Attentive = 1,
        Curious = 2,
        Pleased = 3,
        Concerned = 4,
        Surprised = 5,
    }

    public enum LocalLlmGesture
    {
        None = 0,
        Nod = 1,
        SmallHeadTilt = 2,
        Recoil = 3,
    }

    public enum LocalLlmUrgency
    {
        Low = 0,
        Normal = 1,
        High = 2,
    }

    public sealed class LocalLlmExecutionProfile
    {
        public LocalLlmExecutionProfile(
            int contextTokens,
            int batchTokens,
            int microBatchTokens,
            int maximumGeneratedTokens,
            int threads,
            int batchThreads,
            float temperature,
            float minP,
            uint seed,
            int streamQueueCapacity,
            int maximumConversationMessages = LocalLlmProviderPolicy.MaximumConversationMessages,
            int maximumMessageCharacters = LocalLlmProviderPolicy.MaximumMessageCharacters,
            int maximumResponseUtf8Bytes = LocalLlmProviderPolicy.DefaultMaximumResponseUtf8Bytes)
        {
            if (contextTokens < 64)
            {
                throw new ArgumentOutOfRangeException(nameof(contextTokens));
            }
            if (batchTokens <= 0 || batchTokens > contextTokens)
            {
                throw new ArgumentOutOfRangeException(nameof(batchTokens));
            }
            if (microBatchTokens <= 0 || microBatchTokens > batchTokens)
            {
                throw new ArgumentOutOfRangeException(nameof(microBatchTokens));
            }
            if (maximumGeneratedTokens <= 0 || maximumGeneratedTokens >= contextTokens)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumGeneratedTokens));
            }
            if (threads <= 0 || threads > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(threads));
            }
            if (batchThreads <= 0 || batchThreads > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(batchThreads));
            }
            if (float.IsNaN(temperature) || float.IsInfinity(temperature) || temperature < 0.0F)
            {
                throw new ArgumentOutOfRangeException(nameof(temperature));
            }
            if (float.IsNaN(minP) || float.IsInfinity(minP) || minP < 0.0F || minP > 1.0F)
            {
                throw new ArgumentOutOfRangeException(nameof(minP));
            }
            if (streamQueueCapacity <= 0 || streamQueueCapacity > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(streamQueueCapacity));
            }
            if (maximumConversationMessages <= 0 ||
                maximumConversationMessages > LocalLlmProviderPolicy.MaximumConversationMessages)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConversationMessages));
            }
            if (maximumMessageCharacters <= 0 ||
                maximumMessageCharacters > LocalLlmProviderPolicy.MaximumMessageCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMessageCharacters));
            }
            if (maximumResponseUtf8Bytes <= 0 || maximumResponseUtf8Bytes > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResponseUtf8Bytes));
            }

            ContextTokens = contextTokens;
            BatchTokens = batchTokens;
            MicroBatchTokens = microBatchTokens;
            MaximumGeneratedTokens = maximumGeneratedTokens;
            Threads = threads;
            BatchThreads = batchThreads;
            Temperature = temperature;
            MinP = minP;
            Seed = seed;
            StreamQueueCapacity = streamQueueCapacity;
            MaximumConversationMessages = maximumConversationMessages;
            MaximumMessageCharacters = maximumMessageCharacters;
            MaximumResponseUtf8Bytes = maximumResponseUtf8Bytes;
        }

        public int ContextTokens { get; }
        public int BatchTokens { get; }
        public int MicroBatchTokens { get; }
        public int MaximumGeneratedTokens { get; }
        public int Threads { get; }
        public int BatchThreads { get; }
        public float Temperature { get; }
        public float MinP { get; }
        public uint Seed { get; }
        public int StreamQueueCapacity { get; }
        public int MaximumConversationMessages { get; }
        public int MaximumMessageCharacters { get; }
        public int MaximumResponseUtf8Bytes { get; }

        public static LocalLlmExecutionProfile CreateRma133V6Baseline()
        {
            return new LocalLlmExecutionProfile(
                contextTokens: 2048,
                batchTokens: 256,
                microBatchTokens: 64,
                maximumGeneratedTokens: 128,
                threads: 4,
                batchThreads: 4,
                temperature: 0.0F,
                minP: 0.0F,
                seed: 133U,
                streamQueueCapacity: 64);
        }

        internal void ValidateAgainst(LocalModelManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (ContextTokens > manifest.Inference.ContextLimitTokens)
            {
                throw new ArgumentException(
                    "The local LLM execution context exceeds the manifest context limit.",
                    nameof(manifest));
            }
        }
    }

    public sealed class LocalLlmChatMessage
    {
        public LocalLlmChatMessage(LocalLlmChatRole role, string content)
        {
            if (!Enum.IsDefined(typeof(LocalLlmChatRole), role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }
            Role = role;
            Content = RequireBoundedText(
                content,
                nameof(content),
                LocalLlmProviderPolicy.MaximumMessageCharacters,
                allowEmpty: false);
        }

        public LocalLlmChatRole Role { get; }
        public string Content { get; }

        internal static string RequireBoundedText(
            string value,
            string name,
            int maximumCharacters,
            bool allowEmpty)
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
            if ((!allowEmpty && value.Length == 0) || value.Length > maximumCharacters)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            if (value.Contains('\0'))
            {
                throw new ArgumentException(
                    "Local LLM text cannot contain embedded NUL characters.",
                    name);
            }
            return value;
        }
    }

    public sealed class LocalLlmGenerationRequest
    {
        private readonly ReadOnlyCollection<LocalLlmChatMessage> messages;

        public LocalLlmGenerationRequest(
            string requestId,
            IReadOnlyList<LocalLlmChatMessage> messages)
        {
            RequestId = LocalLlmChatMessage.RequireBoundedText(
                requestId,
                nameof(requestId),
                LocalLlmProviderPolicy.MaximumRequestIdCharacters,
                allowEmpty: false);
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }
            if (messages.Count == 0 || messages.Count > LocalLlmProviderPolicy.MaximumConversationMessages)
            {
                throw new ArgumentOutOfRangeException(nameof(messages));
            }

            var copy = new List<LocalLlmChatMessage>(messages.Count);
            for (int index = 0; index < messages.Count; ++index)
            {
                copy.Add(messages[index] ??
                    throw new ArgumentException(
                        "Local LLM conversation messages cannot contain null entries.",
                        nameof(messages)));
            }
            if (copy[copy.Count - 1].Role != LocalLlmChatRole.User)
            {
                throw new ArgumentException(
                    "A local LLM generation request must end with a user message.",
                    nameof(messages));
            }
            this.messages = copy.AsReadOnly();
        }

        public string RequestId { get; }
        public IReadOnlyList<LocalLlmChatMessage> Messages => messages;
    }

    public sealed class LocalLlmGazeTarget
    {
        internal LocalLlmGazeTarget(string entityId)
        {
            Kind = "tracked_entity";
            EntityId = entityId;
        }

        public string Kind { get; }
        public string EntityId { get; }
    }

    public sealed class LocalLlmBehaviorIntent
    {
        internal LocalLlmBehaviorIntent(
            string speech,
            LocalLlmGazeTarget? gazeTarget,
            LocalLlmExpression expression,
            LocalLlmGesture gesture,
            LocalLlmUrgency urgency)
        {
            SchemaVersion = 1;
            Speech = speech;
            GazeTarget = gazeTarget;
            Expression = expression;
            Gesture = gesture;
            Urgency = urgency;
        }

        public int SchemaVersion { get; }
        public string Speech { get; }
        public LocalLlmGazeTarget? GazeTarget { get; }
        public LocalLlmExpression Expression { get; }
        public LocalLlmGesture Gesture { get; }
        public LocalLlmUrgency Urgency { get; }
    }

    public sealed class LocalLlmStreamEvent
    {
        internal LocalLlmStreamEvent(
            LocalLlmStreamEventType type,
            ulong sequence,
            string text,
            string detail)
        {
            Type = type;
            Sequence = sequence;
            Text = text;
            Detail = detail;
            IsTrustedExecutableOutput = false;
        }

        public LocalLlmStreamEventType Type { get; }
        public ulong Sequence { get; }
        public string Text { get; }
        public string Detail { get; }
        public bool IsTrustedExecutableOutput { get; }
    }

    public interface ILocalLlmStreamSink
    {
        ValueTask OnEventAsync(
            LocalLlmStreamEvent streamEvent,
            CancellationToken cancellationToken);
    }

    public sealed class LocalLlmGenerationMetrics
    {
        internal LocalLlmGenerationMetrics(
            ulong promptTokens,
            ulong generatedTokens,
            ulong startedMonotonicMicroseconds,
            ulong firstTextMonotonicMicroseconds,
            ulong finishedMonotonicMicroseconds,
            int contextTokens,
            int batchTokens,
            int threads,
            int batchThreads)
        {
            PromptTokens = promptTokens;
            GeneratedTokens = generatedTokens;
            StartedMonotonicMicroseconds = startedMonotonicMicroseconds;
            FirstTextMonotonicMicroseconds = firstTextMonotonicMicroseconds;
            FinishedMonotonicMicroseconds = finishedMonotonicMicroseconds;
            ContextTokens = contextTokens;
            BatchTokens = batchTokens;
            Threads = threads;
            BatchThreads = batchThreads;
        }

        public ulong PromptTokens { get; }
        public ulong GeneratedTokens { get; }
        public ulong StartedMonotonicMicroseconds { get; }
        public ulong FirstTextMonotonicMicroseconds { get; }
        public ulong FinishedMonotonicMicroseconds { get; }
        public int ContextTokens { get; }
        public int BatchTokens { get; }
        public int Threads { get; }
        public int BatchThreads { get; }
    }

    public sealed class LocalLlmGenerationResult
    {
        internal LocalLlmGenerationResult(
            LocalLlmGenerationStatus status,
            string requestId,
            ulong conversationEpoch,
            string detail,
            int nativeStatus,
            LocalLlmBehaviorIntent? intent,
            LocalLlmGenerationMetrics? metrics)
        {
            Status = status;
            RequestId = requestId;
            ConversationEpoch = conversationEpoch;
            Detail = BoundDiagnostic(detail);
            NativeStatus = nativeStatus;
            Intent = intent;
            Metrics = metrics;
        }

        public LocalLlmGenerationStatus Status { get; }
        public bool Succeeded => Status == LocalLlmGenerationStatus.Succeeded;
        public string RequestId { get; }
        public ulong ConversationEpoch { get; }
        public string Detail { get; }
        public int NativeStatus { get; }
        public LocalLlmBehaviorIntent? Intent { get; }
        public LocalLlmGenerationMetrics? Metrics { get; }

        internal static string BoundDiagnostic(string? detail)
        {
            string text = detail ?? string.Empty;
            if (text.Length <= LocalLlmProviderPolicy.MaximumDiagnosticCharacters)
            {
                return text;
            }
            return text.Substring(0, LocalLlmProviderPolicy.MaximumDiagnosticCharacters);
        }
    }

    public sealed class LocalLlmProviderCreationResult
    {
        internal LocalLlmProviderCreationResult(
            LocalLlmProviderCreationStatus status,
            string detail,
            int nativeStatus,
            LocalLlmProvider? provider)
        {
            Status = status;
            Detail = LocalLlmGenerationResult.BoundDiagnostic(detail);
            NativeStatus = nativeStatus;
            Provider = provider;
        }

        public LocalLlmProviderCreationStatus Status { get; }
        public bool Succeeded => Status == LocalLlmProviderCreationStatus.Created;
        public string Detail { get; }
        public int NativeStatus { get; }
        public LocalLlmProvider? Provider { get; }
    }

    public sealed class LocalLlmReloadResult
    {
        internal LocalLlmReloadResult(
            LocalLlmReloadStatus status,
            string detail,
            int nativeStatus)
        {
            Status = status;
            Detail = LocalLlmGenerationResult.BoundDiagnostic(detail);
            NativeStatus = nativeStatus;
        }

        public LocalLlmReloadStatus Status { get; }
        public bool Succeeded => Status == LocalLlmReloadStatus.Reloaded;
        public string Detail { get; }
        public int NativeStatus { get; }
    }
}

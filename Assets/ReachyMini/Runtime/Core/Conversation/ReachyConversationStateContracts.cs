#nullable enable

using System;
using System.Threading;

namespace ReachyMini.Conversation
{
    public enum ReachyConversationState
    {
        Idle = 0,
        Listening = 1,
        Transcribing = 2,
        Thinking = 3,
        PreparingSpeech = 4,
        Speaking = 5,
        Interrupted = 6,
        Unavailable = 7,
        Error = 8,
    }

    public enum ReachyConversationOperationKind
    {
        None = 0,
        AsrListening = 1,
        AsrTranscription = 2,
        LlmThinking = 3,
        TtsPreparation = 4,
        TtsPlayback = 5,
    }

    public enum ReachyBargeInPolicy
    {
        Disabled = 0,
        SpeakingOnly = 1,
        PreparingOrSpeaking = 2,
    }

    public sealed class ReachyConversationSnapshot
    {
        internal ReachyConversationSnapshot(
            ReachyConversationState state,
            ulong sessionId,
            ulong turnId,
            ulong operationEpoch,
            ReachyConversationOperationKind operationKind,
            string statusCode)
        {
            State = state;
            SessionId = sessionId;
            TurnId = turnId;
            OperationEpoch = operationEpoch;
            OperationKind = operationKind;
            StatusCode = RequireCode(statusCode, nameof(statusCode));
        }

        public ReachyConversationState State { get; }

        public ulong SessionId { get; }

        public ulong TurnId { get; }

        public ulong OperationEpoch { get; }

        public ReachyConversationOperationKind OperationKind { get; }

        public string StatusCode { get; }

        public bool HasActiveAsr =>
            State == ReachyConversationState.Listening ||
            State == ReachyConversationState.Transcribing;

        public bool HasActiveTts =>
            State == ReachyConversationState.PreparingSpeech ||
            State == ReachyConversationState.Speaking;

        internal static string RequireCode(string? value, string name)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96)
            {
                throw new ArgumentException(
                    "Conversation status codes must be bounded lowercase identifiers.",
                    name);
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' || character == '_' || character == '-';
                if (!valid)
                {
                    throw new ArgumentException(
                        "Conversation status codes must be bounded lowercase identifiers.",
                        name);
                }
            }
            return value;
        }
    }

    public sealed class ReachyConversationOperation
    {
        internal ReachyConversationOperation(
            ReachyConversationState expectedState,
            ReachyConversationOperationKind kind,
            ulong sessionId,
            ulong turnId,
            ulong operationEpoch,
            CancellationToken cancellationToken)
        {
            ExpectedState = expectedState;
            Kind = kind;
            SessionId = sessionId;
            TurnId = turnId;
            OperationEpoch = operationEpoch;
            CancellationToken = cancellationToken;
        }

        public ReachyConversationState ExpectedState { get; }

        public ReachyConversationOperationKind Kind { get; }

        public ulong SessionId { get; }

        public ulong TurnId { get; }

        public ulong OperationEpoch { get; }

        public CancellationToken CancellationToken { get; }
    }

    public sealed class ReachyStaleConversationCompletionException : InvalidOperationException
    {
        public ReachyStaleConversationCompletionException(string message)
            : base(message)
        {
        }
    }
}

#nullable enable

using System;
using System.Threading;

namespace ReachyMini.Conversation
{
    public sealed partial class ReachyConversationStateMachine : IDisposable
    {
        private readonly object sync = new object();
        private readonly ReachyBargeInPolicy bargeInPolicy;
        private ReachyConversationState state = ReachyConversationState.Idle;
        private ReachyConversationOperationKind operationKind =
            ReachyConversationOperationKind.None;
        private CancellationTokenSource? operationCancellation;
        private ulong sessionId = 1UL;
        private ulong turnId;
        private ulong operationEpoch;
        private string statusCode = "idle";
        private int disposed;

        public ReachyConversationStateMachine(
            ReachyBargeInPolicy bargeInPolicy = ReachyBargeInPolicy.SpeakingOnly)
        {
            if (!Enum.IsDefined(typeof(ReachyBargeInPolicy), bargeInPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(bargeInPolicy));
            }
            this.bargeInPolicy = bargeInPolicy;
        }

        public ReachyConversationSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    ThrowIfDisposed();
                    return Snapshot();
                }
            }
        }

        public ReachyConversationOperation BeginListening()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (state != ReachyConversationState.Idle &&
                    state != ReachyConversationState.Interrupted)
                {
                    throw InvalidTransition("begin-listening");
                }
                if (operationCancellation != null)
                {
                    throw new InvalidOperationException(
                        "Idle/interrupted conversation state retained an active operation.");
                }
                turnId = checked(turnId + 1UL);
                return StartOperation(
                    ReachyConversationState.Listening,
                    ReachyConversationOperationKind.AsrListening,
                    "listening");
            }
        }

        public ReachyConversationOperation MarkListeningComplete(
            ReachyConversationOperation completion)
        {
            lock (sync)
            {
                RequireCurrent(
                    completion,
                    ReachyConversationState.Listening,
                    ReachyConversationOperationKind.AsrListening);
                EndCompletedOperation();
                return StartOperation(
                    ReachyConversationState.Transcribing,
                    ReachyConversationOperationKind.AsrTranscription,
                    "transcribing");
            }
        }

        public ReachyConversationOperation MarkTranscriptionComplete(
            ReachyConversationOperation completion)
        {
            lock (sync)
            {
                RequireCurrent(
                    completion,
                    ReachyConversationState.Transcribing,
                    ReachyConversationOperationKind.AsrTranscription);
                EndCompletedOperation();
                return StartOperation(
                    ReachyConversationState.Thinking,
                    ReachyConversationOperationKind.LlmThinking,
                    "thinking");
            }
        }

        public ReachyConversationSnapshot MarkNoMatch(
            ReachyConversationOperation completion)
        {
            lock (sync)
            {
                RequireCurrent(
                    completion,
                    ReachyConversationState.Transcribing,
                    ReachyConversationOperationKind.AsrTranscription);
                EndCompletedOperation();
                SetPassiveState(ReachyConversationState.Idle, "no-match");
                return Snapshot();
            }
        }

        public ReachyConversationOperation MarkThinkingComplete(
            ReachyConversationOperation completion)
        {
            lock (sync)
            {
                RequireCurrent(
                    completion,
                    ReachyConversationState.Thinking,
                    ReachyConversationOperationKind.LlmThinking);
                EndCompletedOperation();
                return StartOperation(
                    ReachyConversationState.PreparingSpeech,
                    ReachyConversationOperationKind.TtsPreparation,
                    "preparing-speech");
            }
        }

        public ReachyConversationOperation MarkSpeechPrepared(
            ReachyConversationOperation completion)
        {
            lock (sync)
            {
                RequireCurrent(
                    completion,
                    ReachyConversationState.PreparingSpeech,
                    ReachyConversationOperationKind.TtsPreparation);
                EndCompletedOperation();
                return StartOperation(
                    ReachyConversationState.Speaking,
                    ReachyConversationOperationKind.TtsPlayback,
                    "speaking");
            }
        }

        public ReachyConversationSnapshot MarkSpeechComplete(
            ReachyConversationOperation completion)
        {
            lock (sync)
            {
                RequireCurrent(
                    completion,
                    ReachyConversationState.Speaking,
                    ReachyConversationOperationKind.TtsPlayback);
                EndCompletedOperation();
                SetPassiveState(ReachyConversationState.Idle, "idle");
                return Snapshot();
            }
        }

        public ReachyConversationSnapshot Interrupt(string reasonCode)
        {
            CancellationTokenSource? cancellation;
            ReachyConversationSnapshot snapshot;
            lock (sync)
            {
                ThrowIfDisposed();
                if (!IsInterruptible(state))
                {
                    throw InvalidTransition("interrupt");
                }
                string reason = RequireReasonCode(reasonCode, nameof(reasonCode));
                cancellation = DetachOperation();
                SetPassiveState(
                    ReachyConversationState.Interrupted,
                    "interrupted-" + reason);
                snapshot = Snapshot();
            }
            CancelAndDispose(cancellation);
            return snapshot;
        }

        public ReachyConversationSnapshot RequestBargeIn()
        {
            CancellationTokenSource? cancellation;
            ReachyConversationSnapshot snapshot;
            lock (sync)
            {
                ThrowIfDisposed();
                bool allowed = bargeInPolicy switch
                {
                    ReachyBargeInPolicy.Disabled => false,
                    ReachyBargeInPolicy.SpeakingOnly =>
                        state == ReachyConversationState.Speaking,
                    ReachyBargeInPolicy.PreparingOrSpeaking =>
                        state == ReachyConversationState.PreparingSpeech ||
                        state == ReachyConversationState.Speaking,
                    _ => false,
                };
                if (!allowed)
                {
                    throw InvalidTransition("barge-in");
                }
                cancellation = DetachOperation();
                SetPassiveState(
                    ReachyConversationState.Interrupted,
                    "interrupted-barge-in");
                snapshot = Snapshot();
            }
            CancelAndDispose(cancellation);
            return snapshot;
        }

        public ReachyConversationSnapshot MarkUnavailable(string reasonCode)
        {
            CancellationTokenSource? cancellation;
            ReachyConversationSnapshot snapshot;
            lock (sync)
            {
                ThrowIfDisposed();
                string reason = RequireReasonCode(reasonCode, nameof(reasonCode));
                cancellation = DetachOperation();
                SetPassiveState(
                    ReachyConversationState.Unavailable,
                    "unavailable-" + reason);
                snapshot = Snapshot();
            }
            CancelAndDispose(cancellation);
            return snapshot;
        }

        public ReachyConversationSnapshot RecoverFromUnavailable()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (state != ReachyConversationState.Unavailable)
                {
                    throw InvalidTransition("recover-unavailable");
                }
                SetPassiveState(ReachyConversationState.Idle, "idle");
                return Snapshot();
            }
        }

        public ReachyConversationSnapshot MarkError(string reasonCode)
        {
            CancellationTokenSource? cancellation;
            ReachyConversationSnapshot snapshot;
            lock (sync)
            {
                ThrowIfDisposed();
                string reason = RequireReasonCode(reasonCode, nameof(reasonCode));
                cancellation = DetachOperation();
                SetPassiveState(
                    ReachyConversationState.Error,
                    "error-" + reason);
                snapshot = Snapshot();
            }
            CancelAndDispose(cancellation);
            return snapshot;
        }

        public ReachyConversationSnapshot ResetAfterError()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (state != ReachyConversationState.Error)
                {
                    throw InvalidTransition("reset-error");
                }
                sessionId = checked(sessionId + 1UL);
                turnId = 0UL;
                operationEpoch = checked(operationEpoch + 1UL);
                SetPassiveState(ReachyConversationState.Idle, "idle");
                return Snapshot();
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? cancellation;
            lock (sync)
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }
                cancellation = DetachOperation();
            }
            CancelAndDispose(cancellation);
        }

        private ReachyConversationOperation StartOperation(
            ReachyConversationState nextState,
            ReachyConversationOperationKind nextKind,
            string nextStatusCode)
        {
            if (operationCancellation != null)
            {
                throw new InvalidOperationException(
                    "Conversation state machine attempted to overlap active operations.");
            }
            operationEpoch = checked(operationEpoch + 1UL);
            operationCancellation = new CancellationTokenSource();
            state = nextState;
            operationKind = nextKind;
            statusCode = nextStatusCode;
            ValidateNoConflictingAudioSessions();
            return new ReachyConversationOperation(
                state,
                operationKind,
                sessionId,
                turnId,
                operationEpoch,
                operationCancellation.Token);
        }

        private void EndCompletedOperation()
        {
            CancellationTokenSource? cancellation = DetachOperation();
            cancellation?.Dispose();
        }

        private CancellationTokenSource? DetachOperation()
        {
            CancellationTokenSource? cancellation = operationCancellation;
            operationCancellation = null;
            operationKind = ReachyConversationOperationKind.None;
            return cancellation;
        }

        private static void CancelAndDispose(CancellationTokenSource? cancellation)
        {
            if (cancellation == null)
            {
                return;
            }
            try
            {
                cancellation.Cancel();
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private static string RequireReasonCode(string? value, string name)
        {
            string validated = ReachyConversationSnapshot.RequireCode(value, name);
            if (validated.Length > 64)
            {
                throw new ArgumentException(
                    "Conversation reason codes cannot exceed 64 characters.",
                    name);
            }
            return validated;
        }

        private void SetPassiveState(
            ReachyConversationState nextState,
            string nextStatusCode)
        {
            if (operationCancellation != null)
            {
                throw new InvalidOperationException(
                    "Conversation passive state cannot retain an active operation.");
            }
            state = nextState;
            operationKind = ReachyConversationOperationKind.None;
            statusCode = nextStatusCode;
            ValidateNoConflictingAudioSessions();
        }

        private void RequireCurrent(
            ReachyConversationOperation completion,
            ReachyConversationState expectedState,
            ReachyConversationOperationKind expectedKind)
        {
            ThrowIfDisposed();
            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }
            if (completion.SessionId != sessionId ||
                completion.TurnId != turnId ||
                completion.OperationEpoch != operationEpoch ||
                completion.ExpectedState != expectedState ||
                completion.Kind != expectedKind ||
                state != expectedState ||
                operationKind != expectedKind ||
                operationCancellation == null)
            {
                throw new ReachyStaleConversationCompletionException(
                    "Conversation async completion was rejected because its session, turn, operation epoch, or expected state is stale.");
            }
        }

        private ReachyConversationSnapshot Snapshot()
        {
            ValidateNoConflictingAudioSessions();
            return new ReachyConversationSnapshot(
                state,
                sessionId,
                turnId,
                operationEpoch,
                operationKind,
                statusCode);
        }

        private void ValidateNoConflictingAudioSessions()
        {
            bool asr = state == ReachyConversationState.Listening ||
                state == ReachyConversationState.Transcribing;
            bool tts = state == ReachyConversationState.PreparingSpeech ||
                state == ReachyConversationState.Speaking;
            if (asr && tts)
            {
                throw new InvalidOperationException(
                    "Conversation state cannot own ASR and TTS sessions simultaneously.");
            }
        }

        private static bool IsInterruptible(ReachyConversationState value)
        {
            return value == ReachyConversationState.Listening ||
                value == ReachyConversationState.Transcribing ||
                value == ReachyConversationState.Thinking ||
                value == ReachyConversationState.PreparingSpeech ||
                value == ReachyConversationState.Speaking;
        }

        private InvalidOperationException InvalidTransition(string transition)
        {
            return new InvalidOperationException(
                $"Conversation transition '{transition}' is invalid from state {state}.");
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ReachyConversationStateMachine));
            }
        }
    }
}

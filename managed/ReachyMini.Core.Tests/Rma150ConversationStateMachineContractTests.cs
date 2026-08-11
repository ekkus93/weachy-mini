#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReachyMini.Conversation;

namespace ReachyMini.Core.Tests
{
    internal static class Rma150ConversationStateMachineContractTests
    {
        [ModuleInitializer]
        internal static void Run()
        {
            HappyPathIsDeterministic();
            NoMatchReturnsToIdle();
            StaleCompletionAfterInterruptIsRejected();
            SpeakingBargeInCancelsPlaybackBeforeNextTurn();
            ConflictingSessionStartIsRejected();
            ErrorResetChangesSessionIdentity();
        }

        private static void HappyPathIsDeterministic()
        {
            using var machine = new ReachyConversationStateMachine();
            Equal(ReachyConversationState.Idle, machine.Current.State, "initial state");

            ReachyConversationOperation listening = machine.BeginListening();
            AssertAudioOwnership(machine.Current, asr: true, tts: false, "listening ownership");
            ReachyConversationOperation transcribing = machine.MarkListeningComplete(listening);
            Equal(ReachyConversationState.Transcribing, machine.Current.State, "transcribing state");
            ReachyConversationOperation thinking = machine.MarkTranscriptionComplete(transcribing);
            AssertAudioOwnership(machine.Current, asr: false, tts: false, "thinking ownership");
            ReachyConversationOperation preparing = machine.MarkThinkingComplete(thinking);
            AssertAudioOwnership(machine.Current, asr: false, tts: true, "preparing ownership");
            ReachyConversationOperation speaking = machine.MarkSpeechPrepared(preparing);
            Equal(ReachyConversationState.Speaking, machine.Current.State, "speaking state");
            ReachyConversationSnapshot completed = machine.MarkSpeechComplete(speaking);
            Equal(ReachyConversationState.Idle, completed.State, "completed state");
            AssertAudioOwnership(completed, asr: false, tts: false, "completed ownership");
        }

        private static void NoMatchReturnsToIdle()
        {
            using var machine = new ReachyConversationStateMachine();
            ReachyConversationOperation listening = machine.BeginListening();
            ReachyConversationOperation transcribing = machine.MarkListeningComplete(listening);
            ReachyConversationSnapshot noMatch = machine.MarkNoMatch(transcribing);
            Equal(ReachyConversationState.Idle, noMatch.State, "no-match state");
            Equal("no-match", noMatch.StatusCode, "no-match status");
        }

        private static void StaleCompletionAfterInterruptIsRejected()
        {
            using var machine = new ReachyConversationStateMachine();
            ReachyConversationOperation listening = machine.BeginListening();
            ReachyConversationSnapshot interrupted = machine.Interrupt("user-cancelled");
            Equal(ReachyConversationState.Interrupted, interrupted.State, "interrupted state");
            Equal(true, listening.CancellationToken.IsCancellationRequested, "interrupted token cancellation");
            Throws<ReachyStaleConversationCompletionException>(
                () => machine.MarkListeningComplete(listening));
        }

        private static void SpeakingBargeInCancelsPlaybackBeforeNextTurn()
        {
            using var machine = new ReachyConversationStateMachine(
                ReachyBargeInPolicy.SpeakingOnly);
            ReachyConversationOperation listening = machine.BeginListening();
            ReachyConversationOperation transcribing = machine.MarkListeningComplete(listening);
            ReachyConversationOperation thinking = machine.MarkTranscriptionComplete(transcribing);
            Throws<InvalidOperationException>(() => machine.RequestBargeIn());
            ReachyConversationOperation preparing = machine.MarkThinkingComplete(thinking);
            ReachyConversationOperation speaking = machine.MarkSpeechPrepared(preparing);
            ulong firstTurn = speaking.TurnId;

            ReachyConversationSnapshot interrupted = machine.RequestBargeIn();
            Equal(ReachyConversationState.Interrupted, interrupted.State, "barge-in state");
            Equal(true, speaking.CancellationToken.IsCancellationRequested, "barge-in playback cancellation");
            ReachyConversationOperation replacement = machine.BeginListening();
            Equal(firstTurn + 1UL, replacement.TurnId, "barge-in replacement turn");
            AssertAudioOwnership(machine.Current, asr: true, tts: false, "barge-in replacement ownership");
        }

        private static void ConflictingSessionStartIsRejected()
        {
            using var machine = new ReachyConversationStateMachine();
            ReachyConversationOperation listening = machine.BeginListening();
            Throws<InvalidOperationException>(() => machine.BeginListening());
            Equal(false, listening.CancellationToken.IsCancellationRequested, "rejected duplicate start keeps current ASR alive");
        }

        private static void ErrorResetChangesSessionIdentity()
        {
            using var machine = new ReachyConversationStateMachine();
            ReachyConversationOperation listening = machine.BeginListening();
            ulong originalSession = listening.SessionId;
            machine.MarkError("provider-failure");
            Equal(true, listening.CancellationToken.IsCancellationRequested, "error cancellation");
            ReachyConversationSnapshot reset = machine.ResetAfterError();
            Equal(originalSession + 1UL, reset.SessionId, "reset session identity");
            Equal(0UL, reset.TurnId, "reset turn identity");
            Throws<ReachyStaleConversationCompletionException>(
                () => machine.MarkListeningComplete(listening));
        }

        private static void AssertAudioOwnership(
            ReachyConversationSnapshot snapshot,
            bool asr,
            bool tts,
            string description)
        {
            Equal(asr, snapshot.HasActiveAsr, description + " ASR");
            Equal(tts, snapshot.HasActiveTts, description + " TTS");
            if (snapshot.HasActiveAsr && snapshot.HasActiveTts)
            {
                throw new InvalidOperationException(
                    "RMA-150 contract observed simultaneous ASR and TTS ownership.");
            }
        }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"RMA-150 contract failed for {description}: expected={expected}; actual={actual}.");
            }
        }

        private static void Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"RMA-150 contract expected {typeof(TException).Name}.");
        }
    }
}

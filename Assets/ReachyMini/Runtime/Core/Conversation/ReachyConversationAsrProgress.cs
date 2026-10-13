#nullable enable

using System;
using ReachyMini.Speech;

namespace ReachyMini.Conversation
{
    /// <summary>
    /// Tracks recognizer startup separately from speech capture. Android's
    /// Started event means the microphone path is ready; it does not mean the
    /// user has finished speaking.
    /// </summary>
    public sealed class ReachyConversationAsrProgress
    {
        private readonly ReachyConversationStateMachine conversation;
        private readonly ReachyConversationOperation listening;

        public ReachyConversationAsrProgress(
            ReachyConversationStateMachine conversation,
            ReachyConversationOperation listening)
        {
            this.conversation = conversation ??
                throw new ArgumentNullException(nameof(conversation));
            this.listening = listening ??
                throw new ArgumentNullException(nameof(listening));
        }

        public bool RecognizerReady { get; private set; }

        public ReachyConversationOperation? Transcribing { get; private set; }

        public bool Observe(AsrEventKind eventKind)
        {
            if (eventKind == AsrEventKind.Started)
            {
                RecognizerReady = true;
                return false;
            }

            if (eventKind != AsrEventKind.PartialResult &&
                eventKind != AsrEventKind.FinalResult)
            {
                return false;
            }

            if (Transcribing != null)
            {
                return false;
            }

            Transcribing = conversation.MarkListeningComplete(listening);
            return true;
        }
    }
}

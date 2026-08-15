#nullable enable

using ReachyMini.AppState;

namespace ReachyMini.Speech
{
    public sealed partial class SpeechAudioFocusCoordinator :
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyApplicationInterruptionGate interruptionGate =
            new ReachyApplicationInterruptionGate();

        public bool IsApplicationPaused => interruptionGate.IsPaused;

        public void PauseForApplicationInterruption()
        {
            SpeechAudioSession? session;
            SpeechAudioInterruption interruption = new SpeechAudioInterruption(
                SpeechAudioInterruptionKind.ApplicationBackgrounded,
                "application_backgrounded",
                "Speech audio was interrupted because the application entered the background.");
            lock (sync)
            {
                ThrowIfDisposedLocked();
                if (interruptionGate.IsPaused)
                {
                    return;
                }
                session = activeSession;
            }

            interruptionGate.PauseForApplicationInterruption();
            lock (sync)
            {
                if (session != null && ReferenceEquals(activeSession, session))
                {
                    lastInterruption = interruption;
                    state = SpeechAudioState.Interrupted;
                }
            }

            if (session != null)
            {
                _ = session.TryInterrupt(interruption);
            }
        }

        public void ResumeAfterApplicationInterruption()
        {
            lock (sync)
            {
                ThrowIfDisposedLocked();
            }
            interruptionGate.ResumeAfterApplicationInterruption();
        }
    }
}

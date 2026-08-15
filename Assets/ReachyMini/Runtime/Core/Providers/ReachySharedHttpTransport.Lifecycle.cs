#nullable enable

using ReachyMini.AppState;

namespace ReachyMini.Providers
{
    public sealed partial class ReachySharedHttpTransport :
        IReachyApplicationInterruptionParticipant
    {
        private readonly ReachyApplicationInterruptionGate interruptionGate =
            new ReachyApplicationInterruptionGate();

        public bool IsApplicationPaused => interruptionGate.IsPaused;

        public void PauseForApplicationInterruption()
        {
            interruptionGate.PauseForApplicationInterruption();
        }

        public void ResumeAfterApplicationInterruption()
        {
            interruptionGate.ResumeAfterApplicationInterruption();
        }
    }
}

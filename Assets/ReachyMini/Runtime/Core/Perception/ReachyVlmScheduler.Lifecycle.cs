#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.AppState;

namespace ReachyMini.Perception
{
    public sealed partial class ReachyVlmScheduler :
        IReachyApplicationInterruptionParticipant
    {
        private bool lifecycleSuspended;

        public void PauseForApplicationInterruption()
        {
            var cancellationLeases = new List<VlmScheduleLease>();
            lock (sync)
            {
                ThrowIfDisposed();
                if (lifecycleSuspended)
                {
                    return;
                }
                lifecycleSuspended = true;
                foreach (ProviderState state in providers.Values)
                {
                    foreach (VlmScheduleLease lease in state.ActiveRequests.Values)
                    {
                        if (lease.MarkCancellationRequested())
                        {
                            cancellationLeases.Add(lease);
                        }
                    }
                }
                cancellationRequestCount = checked(
                    cancellationRequestCount + cancellationLeases.Count);
            }

            int callbackFailures = 0;
            for (int index = 0; index < cancellationLeases.Count; ++index)
            {
                if (cancellationLeases[index].DispatchCancellation())
                {
                    callbackFailures = checked(callbackFailures + 1);
                }
            }
            if (callbackFailures <= 0)
            {
                return;
            }

            lock (sync)
            {
                if (!disposed)
                {
                    cancellationCallbackFailureCount = checked(
                        cancellationCallbackFailureCount + callbackFailures);
                }
            }
        }

        public void ResumeAfterApplicationInterruption()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                lifecycleSuspended = false;
            }
        }
    }
}

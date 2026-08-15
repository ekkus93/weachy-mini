#nullable enable

using System.Threading;

namespace ReachyMini.Conversation
{
    public sealed partial class ReachyConversationStateMachine
    {
        private const string LifecyclePausedStatusCode =
            "unavailable-lifecycle-paused";

        private bool lifecycleSuspended;
        private bool lifecycleOwnsUnavailableState;

        public ReachyConversationSnapshot SuspendForApplicationInterruption()
        {
            CancellationTokenSource? cancellation = null;
            ReachyConversationSnapshot snapshot;
            lock (sync)
            {
                ThrowIfDisposed();
                if (lifecycleSuspended)
                {
                    return Snapshot();
                }

                lifecycleSuspended = true;
                lifecycleOwnsUnavailableState =
                    state != ReachyConversationState.Unavailable &&
                    state != ReachyConversationState.Error;
                if (lifecycleOwnsUnavailableState)
                {
                    cancellation = DetachOperation();
                    SetPassiveState(
                        ReachyConversationState.Unavailable,
                        LifecyclePausedStatusCode);
                }
                snapshot = Snapshot();
            }

            CancelAndDispose(cancellation);
            return snapshot;
        }

        public ReachyConversationSnapshot ResumeAfterApplicationInterruption()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (!lifecycleSuspended)
                {
                    return Snapshot();
                }

                lifecycleSuspended = false;
                if (lifecycleOwnsUnavailableState &&
                    state == ReachyConversationState.Unavailable &&
                    string.Equals(
                        statusCode,
                        LifecyclePausedStatusCode,
                        System.StringComparison.Ordinal))
                {
                    SetPassiveState(
                        ReachyConversationState.Idle,
                        "idle");
                }
                lifecycleOwnsUnavailableState = false;
                return Snapshot();
            }
        }
    }
}

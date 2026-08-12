#nullable enable

using System;
using System.Threading;

namespace ReachyMini.Perception
{
    public sealed class VlmScheduleLease
    {
        private readonly CancellationTokenSource cancellation;
        private readonly CancellationToken cancellationToken;
        private readonly object cancellationSync = new object();
        private bool cancellationRequested;
        private bool cancellationDispatchStarted;
        private bool cancellationDispatchCompleted;
        private bool cancellationDisposed;
        private bool cancellationDisposalRequested;
        private int cancellationDispatchThreadId;

        internal VlmScheduleLease(
            string requestId,
            VlmScheduleSignal signal,
            long scheduledTimestampNanoseconds,
            VlmDisclosureSnapshot disclosure,
            CancellationTokenSource cancellation)
        {
            RequestId = requestId;
            ProviderInstanceId = signal.ProviderInstanceId;
            Trigger = signal.Trigger;
            Operation = signal.Operation;
            TriggerSequence = signal.TriggerSequence;
            SceneRevision = signal.SceneRevision;
            QuestionRevision = signal.Operation == VlmSemanticOperation.VisualQuestion
                ? signal.QuestionRevision
                : 0UL;
            Prompt = signal.Prompt;
            ScheduledTimestampNanoseconds = scheduledTimestampNanoseconds;
            Disclosure = disclosure;
            this.cancellation = cancellation;
            cancellationToken = cancellation.Token;
        }

        public string RequestId { get; }

        public string ProviderInstanceId { get; }

        public VlmScheduleTrigger Trigger { get; }

        public VlmSemanticOperation Operation { get; }

        public ulong TriggerSequence { get; }

        public ulong SceneRevision { get; }

        public ulong QuestionRevision { get; }

        public string Prompt { get; }

        public long ScheduledTimestampNanoseconds { get; }

        public VlmDisclosureSnapshot Disclosure { get; }

        public CancellationToken CancellationToken => cancellationToken;

        public bool IsCancellationRequested
        {
            get
            {
                lock (cancellationSync)
                {
                    return cancellationRequested;
                }
            }
        }

        internal bool MarkCancellationRequested()
        {
            lock (cancellationSync)
            {
                if (cancellationDisposed || cancellationRequested)
                {
                    return false;
                }
                cancellationRequested = true;
                return true;
            }
        }

        internal bool DispatchCancellation()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            lock (cancellationSync)
            {
                if (cancellationDisposed ||
                    !cancellationRequested ||
                    cancellationDispatchStarted)
                {
                    return false;
                }
                cancellationDispatchStarted = true;
                cancellationDispatchThreadId = currentThreadId;
            }

            bool callbackFailure = false;
            try
            {
                cancellation.Cancel();
            }
            catch (AggregateException)
            {
                callbackFailure = true;
            }
            finally
            {
                lock (cancellationSync)
                {
                    cancellationDispatchCompleted = true;
                    cancellationDispatchThreadId = 0;
                    if (cancellationDisposalRequested && !cancellationDisposed)
                    {
                        cancellation.Dispose();
                        cancellationDisposed = true;
                    }
                    Monitor.PulseAll(cancellationSync);
                }
            }
            return callbackFailure;
        }

        internal void DisposeCancellation()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            lock (cancellationSync)
            {
                if (cancellationDisposed)
                {
                    return;
                }
                if (cancellationDispatchStarted && !cancellationDispatchCompleted)
                {
                    cancellationDisposalRequested = true;
                    if (cancellationDispatchThreadId == currentThreadId)
                    {
                        return;
                    }
                    while (!cancellationDispatchCompleted && !cancellationDisposed)
                    {
                        Monitor.Wait(cancellationSync);
                    }
                    if (cancellationDisposed)
                    {
                        return;
                    }
                }
                cancellation.Dispose();
                cancellationDisposed = true;
            }
        }
    }
}

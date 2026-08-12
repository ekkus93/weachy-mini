#nullable enable

using ReachyMini.Behavior;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalLlmProvider
    {
        private bool IsCurrentEpoch(ulong epoch)
        {
            lock (sync)
            {
                return conversationEpoch == epoch &&
                    state != LocalLlmProviderState.Disposed;
            }
        }

        private void MarkFaulted()
        {
            lock (sync)
            {
                if (state != LocalLlmProviderState.Disposed)
                {
                    state = LocalLlmProviderState.Faulted;
                }
            }
        }

        private void SetState(LocalLlmProviderState newState)
        {
            lock (sync)
            {
                if (state != LocalLlmProviderState.Disposed)
                {
                    state = newState;
                }
            }
        }

        private void SetModelHandle(ulong handle, LocalLlmProviderState newState)
        {
            lock (sync)
            {
                if (state != LocalLlmProviderState.Disposed)
                {
                    modelHandle = handle;
                    state = newState;
                }
            }
        }

        private LocalLlmGenerationResult CancellationResult(
            string requestId,
            ulong epoch)
        {
            bool current = IsCurrentEpoch(epoch);
            return Result(
                current
                    ? LocalLlmGenerationStatus.Cancelled
                    : LocalLlmGenerationStatus.Superseded,
                requestId,
                epoch,
                current
                    ? "The local LLM generation was cancelled."
                    : "The conversation reset superseded this generation.",
                ReachyLlamaNativeContract.StatusCancelled);
        }

        private static LocalLlmGenerationResult ResourceExhausted(
            string requestId,
            ulong epoch,
            string detail)
        {
            return Result(
                LocalLlmGenerationStatus.ResourceExhausted,
                requestId,
                epoch,
                detail.Length == 0
                    ? "The local LLM exhausted available memory."
                    : detail,
                ReachyLlamaNativeContract.StatusInternalError);
        }

        private static LocalLlmGenerationResult RuntimeFailure(
            string requestId,
            ulong epoch,
            int nativeStatus,
            string detail,
            LocalLlmGenerationMetrics? metrics = null)
        {
            return Result(
                LocalLlmGenerationStatus.RuntimeFailure,
                requestId,
                epoch,
                detail.Length == 0
                    ? "The local LLM runtime failed."
                    : detail,
                nativeStatus,
                null,
                metrics);
        }

        private static LocalLlmGenerationResult Result(
            LocalLlmGenerationStatus status,
            string requestId,
            ulong epoch,
            string detail,
            int nativeStatus = 0,
            ReachyBehaviorIntent? intent = null,
            LocalLlmGenerationMetrics? metrics = null)
        {
            return new LocalLlmGenerationResult(
                status,
                requestId,
                epoch,
                detail,
                nativeStatus,
                intent,
                metrics);
        }

        private static ulong NextEpoch(ulong current)
        {
            ulong next = unchecked(current + 1UL);
            return next == 0UL ? 1UL : next;
        }
    }
}

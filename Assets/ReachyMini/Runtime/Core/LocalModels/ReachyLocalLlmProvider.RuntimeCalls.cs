#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalLlmProvider
    {
        private async Task<bool> CancelDrainReleaseAsync(ulong generationHandle)
        {
            LocalLlmRuntimeCallResult cancel = SafeCancel(generationHandle);
            if (!cancel.Succeeded &&
                cancel.Status != ReachyLlamaNativeContract.StatusCancelled)
            {
                return false;
            }
            return await DrainAndReleaseAsync(generationHandle).ConfigureAwait(false);
        }

        private async Task<bool> DrainAndReleaseAsync(ulong generationHandle)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed <= CancellationDrainTimeout)
            {
                LocalLlmRuntimePollResult poll = SafePoll(generationHandle);
                if (!poll.Succeeded)
                {
                    return false;
                }
                if (poll.Kind == LocalLlmRuntimePollKind.Cancelled ||
                    poll.Kind == LocalLlmRuntimePollKind.Completed ||
                    poll.Kind == LocalLlmRuntimePollKind.Error)
                {
                    return SafeRelease(generationHandle).Succeeded;
                }
                await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
            }
            return false;
        }

        private LocalLlmRuntimePollResult SafePoll(ulong generationHandle)
        {
            try
            {
                return runtime.Poll(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimePollResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM poll threw: " + exception.Message,
                    LocalLlmRuntimePollKind.Error,
                    ReachyLlamaNativeContract.StatusInternalError,
                    0UL,
                    string.Empty);
            }
        }

        private LocalLlmRuntimeCallResult SafeCancel(ulong generationHandle)
        {
            try
            {
                return runtime.Cancel(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimeCallResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM cancel threw: " + exception.Message);
            }
        }

        private LocalLlmRuntimeMetricsResult SafeGetMetrics(ulong generationHandle)
        {
            try
            {
                return runtime.GetGenerationMetrics(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimeMetricsResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM metrics collection threw: " + exception.Message,
                    null);
            }
        }

        private LocalLlmRuntimeCallResult SafeRelease(ulong generationHandle)
        {
            try
            {
                return runtime.Release(generationHandle);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new LocalLlmRuntimeCallResult(
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Local LLM generation release threw: " + exception.Message);
            }
        }
    }
}

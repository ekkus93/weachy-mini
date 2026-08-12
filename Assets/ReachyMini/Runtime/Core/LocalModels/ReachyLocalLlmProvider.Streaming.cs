#nullable enable

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Interop;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalLlmProvider
    {
        private async Task<LocalLlmGenerationResult> RunStartedGenerationAsync(
            LocalLlmGenerationRequest request,
            ILocalLlmStreamSink sink,
            ulong epoch,
            ulong generationHandle,
            CancellationToken cancellationToken)
        {
            StringBuilder response = new StringBuilder();
            int responseUtf8Bytes = 0;
            bool cancellationIssued = false;
            bool outputLimit = false;
            bool consumerFailure = false;
            string consumerFailureDetail = string.Empty;
            bool sequenceSeen = false;
            ulong lastSequence = 0UL;
            Stopwatch? cancellationDrain = null;
            LocalLlmRuntimePollResult? terminal = null;
            bool generationReleased = false;

            try
            {
                while (terminal == null)
                {
                    bool superseded = !IsCurrentEpoch(epoch);
                    if ((cancellationToken.IsCancellationRequested || superseded ||
                        outputLimit || consumerFailure) && !cancellationIssued)
                    {
                        LocalLlmRuntimeCallResult cancel = SafeCancel(generationHandle);
                        cancellationIssued = true;
                        cancellationDrain = Stopwatch.StartNew();
                        if (!cancel.Succeeded &&
                            cancel.Status != ReachyLlamaNativeContract.StatusCancelled)
                        {
                            bool cleaned = await DrainAndReleaseAsync(
                                generationHandle).ConfigureAwait(false);
                            generationReleased = cleaned;
                            if (!cleaned)
                            {
                                MarkFaulted();
                            }
                            return RuntimeFailure(
                                request.RequestId,
                                epoch,
                                cancel.Status,
                                "Failed to cancel local LLM generation: " + cancel.Detail);
                        }
                    }

                    if (cancellationIssued && cancellationDrain != null &&
                        cancellationDrain.Elapsed > CancellationDrainTimeout)
                    {
                        MarkFaulted();
                        return RuntimeFailure(
                            request.RequestId,
                            epoch,
                            ReachyLlamaNativeContract.StatusInternalError,
                            "Timed out while draining a cancelled local LLM generation.");
                    }

                    LocalLlmRuntimePollResult poll = SafePoll(generationHandle);
                    if (!poll.Succeeded)
                    {
                        bool cleaned = await CancelDrainReleaseAsync(
                            generationHandle).ConfigureAwait(false);
                        generationReleased = cleaned;
                        if (!cleaned)
                        {
                            MarkFaulted();
                        }
                        return RuntimeFailure(
                            request.RequestId,
                            epoch,
                            poll.Status,
                            poll.Detail);
                    }

                    switch (poll.Kind)
                    {
                        case LocalLlmRuntimePollKind.None:
                            await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case LocalLlmRuntimePollKind.Text:
                            if (sequenceSeen && poll.Sequence <= lastSequence)
                            {
                                bool cleaned = await CancelDrainReleaseAsync(
                                    generationHandle).ConfigureAwait(false);
                                generationReleased = cleaned;
                                if (!cleaned)
                                {
                                    MarkFaulted();
                                }
                                return RuntimeFailure(
                                    request.RequestId,
                                    epoch,
                                    ReachyLlamaNativeContract.StatusInternalError,
                                    "reachy_llama emitted a non-monotonic stream sequence.");
                            }
                            sequenceSeen = true;
                            lastSequence = poll.Sequence;
                            if (cancellationIssued || cancellationToken.IsCancellationRequested ||
                                !IsCurrentEpoch(epoch) || outputLimit || consumerFailure)
                            {
                                break;
                            }

                            int fragmentBytes = Encoding.UTF8.GetByteCount(poll.Text);
                            if (fragmentBytes > profile.MaximumResponseUtf8Bytes - responseUtf8Bytes)
                            {
                                outputLimit = true;
                                break;
                            }
                            responseUtf8Bytes += fragmentBytes;
                            response.Append(poll.Text);
                            try
                            {
                                await sink.OnEventAsync(
                                    new LocalLlmStreamEvent(
                                        LocalLlmStreamEventType.Text,
                                        poll.Sequence,
                                        poll.Text,
                                        string.Empty),
                                    cancellationToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                                when (cancellationToken.IsCancellationRequested)
                            {
                            }
                            catch (OutOfMemoryException)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                consumerFailure = true;
                                consumerFailureDetail = exception.Message;
                            }
                            break;
                        case LocalLlmRuntimePollKind.Completed:
                        case LocalLlmRuntimePollKind.Cancelled:
                        case LocalLlmRuntimePollKind.Error:
                            terminal = poll;
                            break;
                        default:
                            bool releasedUnknown = await CancelDrainReleaseAsync(
                                generationHandle).ConfigureAwait(false);
                            generationReleased = releasedUnknown;
                            if (!releasedUnknown)
                            {
                                MarkFaulted();
                            }
                            return RuntimeFailure(
                                request.RequestId,
                                epoch,
                                ReachyLlamaNativeContract.StatusInternalError,
                                "The local LLM runtime returned an unknown poll event.");
                    }
                }

                LocalLlmRuntimeMetricsResult metricsResult = SafeGetMetrics(
                    generationHandle);
                LocalLlmRuntimeCallResult release = SafeRelease(generationHandle);
                generationReleased = release.Succeeded;
                if (!release.Succeeded)
                {
                    MarkFaulted();
                    return RuntimeFailure(
                        request.RequestId,
                        epoch,
                        release.Status,
                        "Failed to release local LLM generation: " + release.Detail);
                }
                if (!metricsResult.Succeeded)
                {
                    return RuntimeFailure(
                        request.RequestId,
                        epoch,
                        metricsResult.Status,
                        metricsResult.Detail);
                }

                if (consumerFailure)
                {
                    return Result(
                        LocalLlmGenerationStatus.ConsumerFailure,
                        request.RequestId,
                        epoch,
                        consumerFailureDetail.Length == 0
                            ? "The local LLM stream consumer failed."
                            : consumerFailureDetail,
                        terminal.EventStatus,
                        null,
                        metricsResult.Metrics);
                }
                if (outputLimit)
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Error,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.OutputLimit,
                            request.RequestId,
                            epoch,
                            "The local LLM response exceeded the managed output-byte limit.",
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (!IsCurrentEpoch(epoch))
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Superseded,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.Superseded,
                            request.RequestId,
                            epoch,
                            "The conversation reset superseded this generation.",
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (cancellationToken.IsCancellationRequested ||
                    terminal.Kind == LocalLlmRuntimePollKind.Cancelled)
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Cancelled,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.Cancelled,
                            request.RequestId,
                            epoch,
                            "The local LLM generation was cancelled.",
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (terminal.Kind == LocalLlmRuntimePollKind.Error)
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Error,
                        lastSequence,
                        RuntimeFailure(
                            request.RequestId,
                            epoch,
                            terminal.EventStatus,
                            terminal.Detail,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }
                if (terminal.Kind != LocalLlmRuntimePollKind.Completed)
                {
                    return RuntimeFailure(
                        request.RequestId,
                        epoch,
                        ReachyLlamaNativeContract.StatusInternalError,
                        "Local LLM generation terminated without a completed event.",
                        metricsResult.Metrics);
                }

                if (!LocalLlmBehaviorContract.TryParseIntent(
                        response.ToString(),
                        out ReachyBehaviorIntent? intent,
                        out string parseDetail) || intent == null)
                {
                    return await PublishTerminalResultAsync(
                        sink,
                        LocalLlmStreamEventType.Error,
                        lastSequence,
                        Result(
                            LocalLlmGenerationStatus.InvalidIntent,
                            request.RequestId,
                            epoch,
                            parseDetail,
                            terminal.EventStatus,
                            null,
                            metricsResult.Metrics)).ConfigureAwait(false);
                }

                return await PublishTerminalResultAsync(
                    sink,
                    LocalLlmStreamEventType.Completed,
                    lastSequence,
                    Result(
                        LocalLlmGenerationStatus.Succeeded,
                        request.RequestId,
                        epoch,
                        "Behavior intent validated.",
                        terminal.EventStatus,
                        intent,
                        metricsResult.Metrics)).ConfigureAwait(false);
            }
            catch (OutOfMemoryException exception)
            {
                bool cleaned = generationReleased;
                if (!generationReleased)
                {
                    try
                    {
                        cleaned = terminal == null
                            ? await CancelDrainReleaseAsync(
                                generationHandle).ConfigureAwait(false)
                            : SafeRelease(generationHandle).Succeeded;
                    }
                    catch (Exception)
                    {
                        cleaned = false;
                    }
                }
                MarkFaulted();
                return ResourceExhausted(
                    request.RequestId,
                    epoch,
                    cleaned
                        ? "Local LLM generation exhausted memory; the generation handle was cleaned and the provider was faulted pending explicit recovery: " + exception.Message
                        : "Local LLM generation exhausted memory and cleanup could not be proven complete; the provider was faulted: " + exception.Message);
            }
            catch (Exception exception)
            {
                bool cleaned = generationReleased;
                if (!generationReleased)
                {
                    cleaned = terminal == null
                        ? await CancelDrainReleaseAsync(
                            generationHandle).ConfigureAwait(false)
                        : SafeRelease(generationHandle).Succeeded;
                }
                if (!cleaned)
                {
                    MarkFaulted();
                }
                return RuntimeFailure(
                    request.RequestId,
                    epoch,
                    ReachyLlamaNativeContract.StatusInternalError,
                    "Unexpected local LLM post-start failure: " + exception.Message);
            }
        }

        private static async Task<LocalLlmGenerationResult> PublishTerminalResultAsync(
            ILocalLlmStreamSink sink,
            LocalLlmStreamEventType type,
            ulong sequence,
            LocalLlmGenerationResult baseResult)
        {
            try
            {
                await sink.OnEventAsync(
                    new LocalLlmStreamEvent(
                        type,
                        sequence,
                        string.Empty,
                        baseResult.Detail),
                    CancellationToken.None).ConfigureAwait(false);
                return baseResult;
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Result(
                    LocalLlmGenerationStatus.ConsumerFailure,
                    baseResult.RequestId,
                    baseResult.ConversationEpoch,
                    "Terminal stream notification failed after " +
                        baseResult.Status + ": " + exception.Message,
                    baseResult.NativeStatus,
                    null,
                    baseResult.Metrics);
            }
        }
    }
}

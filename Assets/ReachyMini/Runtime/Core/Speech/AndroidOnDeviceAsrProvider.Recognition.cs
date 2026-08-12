#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class AndroidOnDeviceAsrProvider
    {
        public async IAsyncEnumerable<AsrEvent> RecognizeAsync(
            AsrRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ThrowIfDisposed();
            SpeechProviderContract.ValidateProviderForOperation(
                Descriptor,
                request.Context);

            if (!TryAcquireOperation())
            {
                yield return Failed(
                    request.Context,
                    1UL,
                    SpeechErrorCategory.Busy,
                    "android_on_device_asr_busy",
                    "Android on-device ASR already has an active operation; the request was not queued.",
                    isRetryable: true);
                yield break;
            }

            TimeSpan effectiveTimeout =
                request.Context.Timeout <= Capabilities.MaximumUtteranceDuration
                    ? request.Context.Timeout
                    : Capabilities.MaximumUtteranceDuration;
            using var timeoutCancellation =
                new CancellationTokenSource(effectiveTimeout);
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token,
                    timeoutCancellation.Token);

            try
            {
                ReadinessEvaluation readinessEvaluation =
                    await EvaluateReadinessSafelyAsync(
                        request.Options,
                        operationCancellation.Token).ConfigureAwait(false);
                if (readinessEvaluation.Cancelled)
                {
                    if (timeoutCancellation.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested &&
                        !lifetimeCancellation.IsCancellationRequested)
                    {
                        yield return Failed(
                            request.Context,
                            1UL,
                            SpeechErrorCategory.Timeout,
                            "android_on_device_asr_operation_timeout",
                            "Android on-device ASR exceeded the selected utterance timeout during capability or language preflight.",
                            isRetryable: true);
                    }
                    else
                    {
                        yield return new AsrEvent(
                            Descriptor.InstanceId,
                            request.Context.RequestId,
                            1UL,
                            AsrEventKind.Cancelled);
                    }
                    yield break;
                }
                if (readinessEvaluation.Exception != null)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ServiceFailure,
                        "android_on_device_asr_readiness_exception",
                        "Android on-device ASR readiness evaluation failed with " +
                            readinessEvaluation.Exception.GetType().Name +
                            "; no alternate provider was attempted.",
                        isRetryable: true);
                    yield break;
                }

                Readiness readiness = readinessEvaluation.Value ??
                    throw new InvalidOperationException(
                        "Android on-device ASR readiness evaluation returned no result.");
                if (!readiness.Availability.IsAvailable)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        readiness.ErrorCategory,
                        readiness.ErrorCode,
                        readiness.Availability.Diagnostic,
                        readiness.IsRetryable);
                    yield break;
                }

                ulong sequence = 0UL;
                bool terminal = false;
                IAsyncEnumerable<AndroidOnDeviceAsrPlatformEvent> stream =
                    platform.RecognizeAsync(
                        request.Context.RequestId,
                        request.Options,
                        operationCancellation.Token);
                await using IAsyncEnumerator<AndroidOnDeviceAsrPlatformEvent> enumerator =
                    stream.GetAsyncEnumerator(operationCancellation.Token);

                while (!terminal)
                {
                    PlatformMoveNextResult move =
                        await MoveNextSafelyAsync(enumerator).ConfigureAwait(false);

                    if (move.Cancelled)
                    {
                        sequence = checked(sequence + 1UL);
                        if (timeoutCancellation.IsCancellationRequested &&
                            !cancellationToken.IsCancellationRequested &&
                            !lifetimeCancellation.IsCancellationRequested)
                        {
                            yield return Failed(
                                request.Context,
                                sequence,
                                SpeechErrorCategory.Timeout,
                                "android_on_device_asr_operation_timeout",
                                "Android on-device ASR exceeded the selected utterance timeout and was cancelled.",
                                isRetryable: true);
                        }
                        else
                        {
                            yield return new AsrEvent(
                                Descriptor.InstanceId,
                                request.Context.RequestId,
                                sequence,
                                AsrEventKind.Cancelled);
                        }

                        yield break;
                    }

                    if (move.Exception != null)
                    {
                        sequence = checked(sequence + 1UL);
                        yield return Failed(
                            request.Context,
                            sequence,
                            SpeechErrorCategory.ServiceFailure,
                            "android_on_device_asr_platform_exception",
                            "Android on-device ASR platform stream failed with " +
                                move.Exception.GetType().Name +
                                "; no alternate provider was attempted.",
                            isRetryable: true);
                        yield break;
                    }

                    if (!move.HasValue)
                    {
                        sequence = checked(sequence + 1UL);
                        yield return Failed(
                            request.Context,
                            sequence,
                            SpeechErrorCategory.ServiceFailure,
                            "android_on_device_asr_stream_ended_without_terminal_event",
                            "Android on-device ASR ended without a final, no-match, cancellation, or failure event.",
                            isRetryable: true);
                        yield break;
                    }

                    AndroidOnDeviceAsrPlatformEvent platformEvent = move.Value!;
                    if (!string.Equals(
                            platformEvent.RequestId,
                            request.Context.RequestId,
                            StringComparison.Ordinal))
                    {
                        operationCancellation.Cancel();
                        sequence = checked(sequence + 1UL);
                        yield return Failed(
                            request.Context,
                            sequence,
                            SpeechErrorCategory.ContractViolation,
                            "android_on_device_asr_request_identity_mismatch",
                            "Android on-device ASR returned an event for a different request; the active recognizer was cancelled.",
                            isRetryable: false);
                        yield break;
                    }

                    sequence = checked(sequence + 1UL);
                    AsrEvent providerEvent = MapPlatformEvent(
                        request.Context,
                        sequence,
                        platformEvent,
                        timeoutCancellation.IsCancellationRequested,
                        cancellationToken.IsCancellationRequested,
                        lifetimeCancellation.IsCancellationRequested);
                    terminal = platformEvent.IsTerminal;
                    yield return providerEvent;
                }
            }
            finally
            {
                ReleaseOperation();
            }
        }

        private AsrEvent MapPlatformEvent(
            SpeechOperationContext context,
            ulong sequence,
            AndroidOnDeviceAsrPlatformEvent platformEvent,
            bool timeoutRequested,
            bool callerCancellationRequested,
            bool lifetimeCancellationRequested)
        {
            switch (platformEvent.Kind)
            {
                case AndroidOnDeviceAsrPlatformEventKind.Started:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.Started);
                case AndroidOnDeviceAsrPlatformEventKind.PartialResult:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.PartialResult,
                        platformEvent.Transcript);
                case AndroidOnDeviceAsrPlatformEventKind.FinalResult:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.FinalResult,
                        platformEvent.Transcript);
                case AndroidOnDeviceAsrPlatformEventKind.NoMatch:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.NoMatch);
                case AndroidOnDeviceAsrPlatformEventKind.Cancelled:
                    if (timeoutRequested &&
                        !callerCancellationRequested &&
                        !lifetimeCancellationRequested)
                    {
                        return Failed(
                            context,
                            sequence,
                            SpeechErrorCategory.Timeout,
                            "android_on_device_asr_operation_timeout",
                            "Android on-device ASR exceeded the selected utterance timeout and was cancelled.",
                            isRetryable: true);
                    }

                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.Cancelled);
                case AndroidOnDeviceAsrPlatformEventKind.Failed:
                    AndroidOnDeviceAsrPlatformFailure failure =
                        platformEvent.Failure ??
                        throw new InvalidOperationException(
                            "A failed Android ASR platform event did not contain failure detail.");
                    // MapFailure is defined in
                    // AndroidOnDeviceAsrProvider.Readiness.cs — the one
                    // genuine cross-partial-file call dependency in this
                    // split.
                    FailureMapping mapping = MapFailure(failure.Kind);
                    return Failed(
                        context,
                        sequence,
                        mapping.Category,
                        failure.Code,
                        failure.Diagnostic,
                        mapping.IsRetryable);
                default:
                    throw new InvalidOperationException(
                        "Android on-device ASR returned an unsupported event kind.");
            }
        }

        private AsrEvent Failed(
            SpeechOperationContext context,
            ulong sequence,
            SpeechErrorCategory category,
            string code,
            string diagnostic,
            bool isRetryable)
        {
            return new AsrEvent(
                Descriptor.InstanceId,
                context.RequestId,
                sequence,
                AsrEventKind.Failed,
                error: new SpeechProviderError(
                    category,
                    Bound(code, SpeechProviderError.MaximumCodeCharacters),
                    Bound(
                        diagnostic,
                        SpeechProviderError.MaximumDiagnosticCharacters),
                    isRetryable));
        }

        private static async ValueTask<PlatformMoveNextResult> MoveNextSafelyAsync(
            IAsyncEnumerator<AndroidOnDeviceAsrPlatformEvent> enumerator)
        {
            try
            {
                bool hasValue = await enumerator.MoveNextAsync()
                    .ConfigureAwait(false);
                return hasValue
                    ? PlatformMoveNextResult.FromValue(enumerator.Current)
                    : PlatformMoveNextResult.Ended();
            }
            catch (OperationCanceledException)
            {
                return PlatformMoveNextResult.WasCancelled();
            }
            catch (Exception exception)
            {
                return PlatformMoveNextResult.Failed(exception);
            }
        }

        private sealed class PlatformMoveNextResult
        {
            private PlatformMoveNextResult(
                bool hasValue,
                bool cancelled,
                AndroidOnDeviceAsrPlatformEvent? value,
                Exception? exception)
            {
                HasValue = hasValue;
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool HasValue { get; }
            public bool Cancelled { get; }
            public AndroidOnDeviceAsrPlatformEvent? Value { get; }
            public Exception? Exception { get; }

            public static PlatformMoveNextResult FromValue(
                AndroidOnDeviceAsrPlatformEvent value)
            {
                return new PlatformMoveNextResult(
                    true,
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);
            }

            public static PlatformMoveNextResult Ended()
            {
                return new PlatformMoveNextResult(
                    false,
                    false,
                    null,
                    null);
            }

            public static PlatformMoveNextResult WasCancelled()
            {
                return new PlatformMoveNextResult(
                    false,
                    true,
                    null,
                    null);
            }

            public static PlatformMoveNextResult Failed(Exception exception)
            {
                return new PlatformMoveNextResult(
                    false,
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
            }
        }
    }
}

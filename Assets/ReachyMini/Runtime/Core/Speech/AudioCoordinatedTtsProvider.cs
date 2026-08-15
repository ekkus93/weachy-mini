#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Performance;

namespace ReachyMini.Speech
{
    public sealed class AudioCoordinatedTtsProvider : ITtsProvider
    {
        private readonly ITtsProvider inner;
        private readonly SpeechAudioFocusCoordinator audio;
        private readonly bool ownsInner;
        private int disposed;

        public AudioCoordinatedTtsProvider(
            ITtsProvider inner,
            SpeechAudioFocusCoordinator audio,
            bool ownsInner = true)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
            this.ownsInner = ownsInner;
        }

        public SpeechProviderDescriptor Descriptor => inner.Descriptor;
        public TtsCapabilities Capabilities => inner.Capabilities;

        public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken) =>
            inner.CheckAvailabilityAsync(cancellationToken);

        public ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(
            CancellationToken cancellationToken) =>
            inner.GetVoicesAsync(cancellationToken);

        public async IAsyncEnumerable<TtsEvent> SpeakAsync(
            TtsRequest request,
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

            using ReachyPerformanceMeasurement measurement =
                ReachyPerformanceTelemetry.Measure(
                    ReachyPerformanceWorkload.Audio);

            SpeechAudioAcquireAttempt acquisitionAttempt =
                await SpeechAudioSafeOperations.AcquireSafelyAsync(
                    audio,
                    SpeechAudioRole.Speaking,
                    cancellationToken).ConfigureAwait(false);
            if (acquisitionAttempt.Cancelled)
            {
                yield return new TtsEvent(
                    Descriptor.InstanceId,
                    request.Context.RequestId,
                    1UL,
                    TtsEventKind.Cancelled);
                yield break;
            }
            if (acquisitionAttempt.Exception != null)
            {
                yield return TtsFailure(
                    request,
                    1UL,
                    new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        "speech_audio_acquire_exception",
                        BoundDiagnostic(
                            "Speech audio coordination failed before speaking with " +
                            acquisitionAttempt.Exception.GetType().Name +
                            "; the selected TTS provider was not replaced or retried."),
                        isRetryable: false));
                yield break;
            }

            SpeechAudioAcquireResult acquisition = acquisitionAttempt.Result ??
                throw new InvalidOperationException(
                    "Speech audio acquisition returned no result.");
            if (!acquisition.IsGranted || acquisition.Lease == null)
            {
                yield return TtsFailure(
                    request,
                    1UL,
                    acquisition.Error ?? new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        "speech_audio_focus_denied",
                        "Speech audio focus was not granted for speaking.",
                        isRetryable: true));
                yield break;
            }

            SpeechAudioFocusLease lease = acquisition.Lease;
            bool released = false;
            try
            {
                using var linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        lease.InterruptionToken);
                await using IAsyncEnumerator<TtsEvent> enumerator =
                    inner.SpeakAsync(request, linkedCancellation.Token)
                        .GetAsyncEnumerator(linkedCancellation.Token);

                TtsEvent? terminal = null;
                ulong lastSequence = 0UL;
                while (terminal == null)
                {
                    MoveNextResult<TtsEvent> move =
                        await SpeechAudioEnumerator.MoveNextSafelyAsync(enumerator).ConfigureAwait(false);
                    if (move.Cancelled)
                    {
                        SpeechAudioInterruption? interruption = lease.Interruption;
                        terminal = interruption == null
                            ? new TtsEvent(
                                Descriptor.InstanceId,
                                request.Context.RequestId,
                                checked(lastSequence + 1UL),
                                TtsEventKind.Cancelled)
                            : TtsFailure(
                                request,
                                checked(lastSequence + 1UL),
                                interruption.ToProviderError());
                        break;
                    }
                    if (move.Exception != null)
                    {
                        terminal = TtsFailure(
                            request,
                            checked(lastSequence + 1UL),
                            new SpeechProviderError(
                                SpeechErrorCategory.ServiceFailure,
                                "audio_coordinated_tts_exception",
                                BoundDiagnostic(
                                    "The selected TTS provider failed while the speaker lease was active with " +
                                    move.Exception.GetType().Name +
                                    "; no alternate provider was attempted."),
                                isRetryable: true));
                        break;
                    }
                    if (!move.HasValue || move.Value == null)
                    {
                        terminal = TtsFailure(
                            request,
                            checked(lastSequence + 1UL),
                            new SpeechProviderError(
                                SpeechErrorCategory.ServiceFailure,
                                "audio_coordinated_tts_missing_terminal",
                                "The selected TTS provider ended without a terminal event while holding speech audio focus.",
                                isRetryable: true));
                        break;
                    }

                    TtsEvent value = move.Value;
                    SpeechProviderContract.ValidateEventOrigin(
                        request.Context,
                        value.ProviderInstanceId,
                        value.RequestId);
                    if (value.Sequence <= lastSequence)
                    {
                        terminal = TtsFailure(
                            request,
                            checked(lastSequence + 1UL),
                            new SpeechProviderError(
                                SpeechErrorCategory.ContractViolation,
                                "audio_coordinated_tts_sequence_regression",
                                "The selected TTS provider emitted a non-monotonic event sequence.",
                                isRetryable: false));
                        break;
                    }
                    lastSequence = value.Sequence;
                    if (IsTerminal(value.Kind))
                    {
                        terminal = value;
                    }
                    else
                    {
                        yield return value;
                    }
                }

                SpeechProviderError? releaseError =
                    await lease.ReleaseAsync().ConfigureAwait(false);
                released = true;
                if (releaseError != null)
                {
                    ulong sequence = terminal?.Sequence ?? checked(lastSequence + 1UL);
                    yield return TtsFailure(request, sequence, releaseError);
                    yield break;
                }

                SpeechAudioInterruption? finalInterruption = lease.Interruption;
                if (finalInterruption != null &&
                    terminal?.Kind == TtsEventKind.Cancelled)
                {
                    yield return TtsFailure(
                        request,
                        terminal.Sequence,
                        finalInterruption.ToProviderError());
                    yield break;
                }

                if (terminal != null)
                {
                    yield return terminal;
                }
            }
            finally
            {
                if (!released)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (ownsInner)
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AudioCoordinatedTtsProvider));
            }
        }

        private TtsEvent TtsFailure(
            TtsRequest request,
            ulong sequence,
            SpeechProviderError error) =>
            new TtsEvent(
                Descriptor.InstanceId,
                request.Context.RequestId,
                sequence,
                TtsEventKind.Failed,
                error);

        private static bool IsTerminal(TtsEventKind kind) =>
            kind == TtsEventKind.Completed ||
            kind == TtsEventKind.Cancelled ||
            kind == TtsEventKind.Failed;

        private static string BoundDiagnostic(string value) =>
            value.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? value
                : value.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
    }
}

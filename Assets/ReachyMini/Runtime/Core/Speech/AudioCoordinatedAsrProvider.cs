#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed class AudioCoordinatedAsrProvider : IAsrProvider
    {
        private readonly IAsrProvider inner;
        private readonly SpeechAudioFocusCoordinator audio;
        private readonly bool ownsInner;
        private int disposed;

        public AudioCoordinatedAsrProvider(
            IAsrProvider inner,
            SpeechAudioFocusCoordinator audio,
            bool ownsInner = true)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.audio = audio ?? throw new ArgumentNullException(nameof(audio));
            this.ownsInner = ownsInner;
        }

        public SpeechProviderDescriptor Descriptor => inner.Descriptor;
        public AsrCapabilities Capabilities => inner.Capabilities;

        public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            AsrOptions options,
            CancellationToken cancellationToken) =>
            inner.CheckAvailabilityAsync(options, cancellationToken);

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

            SpeechAudioAcquireAttempt acquisitionAttempt =
                await SpeechAudioSafeOperations.AcquireSafelyAsync(
                    audio,
                    SpeechAudioRole.Listening,
                    cancellationToken).ConfigureAwait(false);
            if (acquisitionAttempt.Cancelled)
            {
                yield return new AsrEvent(
                    Descriptor.InstanceId,
                    request.Context.RequestId,
                    1UL,
                    AsrEventKind.Cancelled);
                yield break;
            }
            if (acquisitionAttempt.Exception != null)
            {
                yield return AsrFailure(
                    request,
                    1UL,
                    new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        "speech_audio_acquire_exception",
                        BoundDiagnostic(
                            "Speech audio coordination failed before listening with " +
                            acquisitionAttempt.Exception.GetType().Name +
                            "; the selected ASR provider was not replaced or retried."),
                        isRetryable: false));
                yield break;
            }

            SpeechAudioAcquireResult acquisition = acquisitionAttempt.Result ??
                throw new InvalidOperationException(
                    "Speech audio acquisition returned no result.");
            if (!acquisition.IsGranted || acquisition.Lease == null)
            {
                yield return AsrFailure(
                    request,
                    1UL,
                    acquisition.Error ?? new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        "speech_audio_focus_denied",
                        "Speech audio focus was not granted for listening.",
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
                await using IAsyncEnumerator<AsrEvent> enumerator =
                    inner.RecognizeAsync(request, linkedCancellation.Token)
                        .GetAsyncEnumerator(linkedCancellation.Token);

                AsrEvent? terminal = null;
                ulong lastSequence = 0UL;
                while (terminal == null)
                {
                    MoveNextResult<AsrEvent> move =
                        await SpeechAudioEnumerator.MoveNextSafelyAsync(enumerator).ConfigureAwait(false);
                    if (move.Cancelled)
                    {
                        SpeechAudioInterruption? interruption = lease.Interruption;
                        terminal = interruption == null
                            ? new AsrEvent(
                                Descriptor.InstanceId,
                                request.Context.RequestId,
                                checked(lastSequence + 1UL),
                                AsrEventKind.Cancelled)
                            : AsrFailure(
                                request,
                                checked(lastSequence + 1UL),
                                interruption.ToProviderError());
                        break;
                    }
                    if (move.Exception != null)
                    {
                        terminal = AsrFailure(
                            request,
                            checked(lastSequence + 1UL),
                            new SpeechProviderError(
                                SpeechErrorCategory.ServiceFailure,
                                "audio_coordinated_asr_exception",
                                BoundDiagnostic(
                                    "The selected ASR provider failed while the microphone lease was active with " +
                                    move.Exception.GetType().Name +
                                    "; no alternate provider was attempted."),
                                isRetryable: true));
                        break;
                    }
                    if (!move.HasValue || move.Value == null)
                    {
                        terminal = AsrFailure(
                            request,
                            checked(lastSequence + 1UL),
                            new SpeechProviderError(
                                SpeechErrorCategory.ServiceFailure,
                                "audio_coordinated_asr_missing_terminal",
                                "The selected ASR provider ended without a terminal event while holding speech audio focus.",
                                isRetryable: true));
                        break;
                    }

                    AsrEvent value = move.Value;
                    SpeechProviderContract.ValidateEventOrigin(
                        request.Context,
                        value.ProviderInstanceId,
                        value.RequestId);
                    if (value.Sequence <= lastSequence)
                    {
                        terminal = AsrFailure(
                            request,
                            checked(lastSequence + 1UL),
                            new SpeechProviderError(
                                SpeechErrorCategory.ContractViolation,
                                "audio_coordinated_asr_sequence_regression",
                                "The selected ASR provider emitted a non-monotonic event sequence.",
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
                    yield return AsrFailure(request, sequence, releaseError);
                    yield break;
                }

                SpeechAudioInterruption? finalInterruption = lease.Interruption;
                if (finalInterruption != null &&
                    terminal?.Kind == AsrEventKind.Cancelled)
                {
                    yield return AsrFailure(
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
                throw new ObjectDisposedException(nameof(AudioCoordinatedAsrProvider));
            }
        }

        private AsrEvent AsrFailure(
            AsrRequest request,
            ulong sequence,
            SpeechProviderError error) =>
            new AsrEvent(
                Descriptor.InstanceId,
                request.Context.RequestId,
                sequence,
                AsrEventKind.Failed,
                error: error);

        private static bool IsTerminal(AsrEventKind kind) =>
            kind == AsrEventKind.FinalResult ||
            kind == AsrEventKind.NoMatch ||
            kind == AsrEventKind.Cancelled ||
            kind == AsrEventKind.Failed;

        private static string BoundDiagnostic(string value) =>
            value.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? value
                : value.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
    }
}

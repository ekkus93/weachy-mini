#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class SpeechAudioFocusCoordinator : IAsyncDisposable
    {
        private readonly object sync = new object();
        private readonly ISpeechAudioFocusPlatform platform;
        private SpeechAudioSession? activeSession;
        private SpeechAudioState state = SpeechAudioState.Idle;
        private SpeechAudioInterruption? lastInterruption;
        private SpeechProviderError? fault;
        private bool disposed;

        public SpeechAudioFocusCoordinator(ISpeechAudioFocusPlatform platform)
        {
            this.platform = platform ??
                throw new ArgumentNullException(nameof(platform));
            platform.Interrupted += OnPlatformInterrupted;
        }

        public SpeechAudioSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    return new SpeechAudioSnapshot(
                        state,
                        activeSession?.Role,
                        activeSession?.FocusHeld == true,
                        lastInterruption,
                        fault);
                }
            }
        }

        public async ValueTask<SpeechAudioAcquireResult> AcquireAsync(
            SpeechAudioRole role,
            CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(typeof(SpeechAudioRole), role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }
            cancellationToken.ThrowIfCancellationRequested();

            SpeechAudioSession session;
            lock (sync)
            {
                ThrowIfDisposedLocked();
                if (interruptionGate.IsPaused)
                {
                    return Failure(new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        "speech_audio_lifecycle_suspended",
                        "Speech audio is suspended while the application is backgrounded.",
                        isRetryable: true));
                }
                if (fault != null || state == SpeechAudioState.Faulted)
                {
                    return Failure(
                        fault ?? new SpeechProviderError(
                            SpeechErrorCategory.ServiceFailure,
                            "speech_audio_faulted",
                            "Speech audio coordination is faulted and must be recreated before another microphone or speaker operation.",
                            isRetryable: false));
                }
                if (activeSession != null || state != SpeechAudioState.Idle)
                {
                    return Failure(new SpeechProviderError(
                        SpeechErrorCategory.Busy,
                        "speech_audio_busy",
                        "The single speech-audio path is already active; listening and speaking are never queued or overlapped.",
                        isRetryable: true));
                }

                session = new SpeechAudioSession(
                    "speech-audio-" + Guid.NewGuid().ToString("N"),
                    role);
                activeSession = session;
                state = SpeechAudioState.Acquiring;
                lastInterruption = null;
            }

            using (CancellationTokenSource lifecycleCancellation =
                CreateLifecycleCancellation(session, cancellationToken))
            {
                SpeechAudioFocusRequestResult requestResult;
                try
                {
                    requestResult = await platform.RequestFocusAsync(
                        session.SessionId,
                        role,
                        lifecycleCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    interruptionGate.IsPaused)
                {
                    ClearCancelledAcquisition(session);
                    return Failure(new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        "speech_audio_lifecycle_suspended",
                        "Speech audio focus acquisition was cancelled because the application entered the background.",
                        isRetryable: true));
                }
                catch (OperationCanceledException)
                {
                    ClearCancelledAcquisition(session);
                    throw;
                }
                catch (Exception exception)
                {
                    SpeechProviderError error = FaultPlatform(
                        session,
                        "audio_focus_request_exception",
                        "Requesting Android speech audio focus failed with " +
                            exception.GetType().Name +
                            "; the coordinator was faulted instead of retrying or falling back.");
                    return Failure(error);
                }

                if (!requestResult.IsGranted)
                {
                    SpeechProviderError error = new SpeechProviderError(
                        SpeechErrorCategory.ServiceFailure,
                        BoundCode(requestResult.Code),
                        BoundDiagnostic(requestResult.Diagnostic),
                        isRetryable: true);
                    ClearDeniedAcquisition(session);
                    return Failure(error);
                }

                SpeechAudioInterruption? interruptedBeforeGrant;
                lock (sync)
                {
                    if (!ReferenceEquals(activeSession, session))
                    {
                        interruptedBeforeGrant = new SpeechAudioInterruption(
                            SpeechAudioInterruptionKind.PlatformFailure,
                            "audio_focus_session_replaced",
                            "The speech audio session changed while focus was being acquired; the granted focus was rejected.");
                    }
                    else
                    {
                        session.FocusHeld = true;
                        interruptedBeforeGrant = session.Interruption;
                        if (interruptedBeforeGrant == null)
                        {
                            state = role == SpeechAudioRole.Listening
                                ? SpeechAudioState.Listening
                                : SpeechAudioState.Speaking;
                            return new SpeechAudioAcquireResult(
                                new SpeechAudioFocusLease(this, session),
                                error: null);
                        }
                        state = SpeechAudioState.Interrupted;
                    }
                }

                SpeechProviderError? releaseError =
                    await ReleaseAsync(session).ConfigureAwait(false);
                SpeechAudioInterruption interruption = interruptedBeforeGrant ??
                    new SpeechAudioInterruption(
                        SpeechAudioInterruptionKind.PlatformFailure,
                        "audio_focus_interrupted_without_detail",
                        "Speech audio focus was interrupted during acquisition without platform detail.");
                return Failure(releaseError ?? interruption.ToProviderError());
            }
        }

        internal async ValueTask<SpeechProviderError?> ReleaseAsync(
            SpeechAudioSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            bool shouldRelease;
            lock (sync)
            {
                shouldRelease = ReferenceEquals(activeSession, session);
                if (!shouldRelease)
                {
                    session.Dispose();
                    return null;
                }
                if (state != SpeechAudioState.Faulted && state != SpeechAudioState.Disposed)
                {
                    state = SpeechAudioState.Releasing;
                }
            }

            SpeechProviderError? releaseError = null;
            try
            {
                await platform.ReleaseFocusAsync(
                    session.SessionId,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                releaseError = new SpeechProviderError(
                    SpeechErrorCategory.ServiceFailure,
                    "audio_focus_release_failed",
                    BoundDiagnostic(
                        "Releasing Android speech audio focus failed with " +
                        exception.GetType().Name +
                        "; the coordinator was faulted to prevent overlapping audio ownership."),
                    isRetryable: false);
            }

            lock (sync)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                }
                session.FocusHeld = false;
                if (!disposed)
                {
                    if (releaseError == null && fault == null)
                    {
                        state = SpeechAudioState.Idle;
                    }
                    else
                    {
                        fault ??= releaseError;
                        state = SpeechAudioState.Faulted;
                    }
                }
            }
            session.Dispose();
            return releaseError;
        }

        public async ValueTask DisposeAsync()
        {
            SpeechAudioSession? session;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                session = activeSession;
            }

            interruptionGate.Dispose();
            platform.Interrupted -= OnPlatformInterrupted;
            if (session != null)
            {
                _ = await ReleaseAsync(session).ConfigureAwait(false);
            }

            await platform.DisposeAsync().ConfigureAwait(false);
            lock (sync)
            {
                activeSession = null;
                state = SpeechAudioState.Disposed;
            }
            GC.SuppressFinalize(this);
        }

        private void OnPlatformInterrupted(
            object? sender,
            SpeechAudioPlatformInterruptionEventArgs eventArgs)
        {
            if (eventArgs == null)
            {
                return;
            }

            SpeechAudioSession session;
            lock (sync)
            {
                SpeechAudioSession? candidate = activeSession;
                if (disposed || candidate == null ||
                    !string.Equals(
                        candidate.SessionId,
                        eventArgs.SessionId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                session = candidate;
                lastInterruption = eventArgs.Interruption;
                state = SpeechAudioState.Interrupted;
            }

            _ = session.TryInterrupt(eventArgs.Interruption);
        }

        private CancellationTokenSource CreateLifecycleCancellation(
            SpeechAudioSession session,
            CancellationToken cancellationToken)
        {
            try
            {
                return interruptionGate.CreateLinkedTokenSource(cancellationToken);
            }
            catch
            {
                ClearCancelledAcquisition(session);
                throw;
            }
        }

        private void ClearCancelledAcquisition(SpeechAudioSession session)
        {
            lock (sync)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                    state = disposed
                        ? SpeechAudioState.Disposed
                        : SpeechAudioState.Idle;
                }
            }
            session.Dispose();
        }

        private void ClearDeniedAcquisition(SpeechAudioSession session)
        {
            lock (sync)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                    state = disposed
                        ? SpeechAudioState.Disposed
                        : SpeechAudioState.Idle;
                }
            }
            session.Dispose();
        }

        private SpeechProviderError FaultPlatform(
            SpeechAudioSession session,
            string code,
            string diagnostic)
        {
            var error = new SpeechProviderError(
                SpeechErrorCategory.ServiceFailure,
                code,
                BoundDiagnostic(diagnostic),
                isRetryable: false);
            lock (sync)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                }
                fault = error;
                state = disposed
                    ? SpeechAudioState.Disposed
                    : SpeechAudioState.Faulted;
            }
            session.Dispose();
            return error;
        }

        private static SpeechAudioAcquireResult Failure(SpeechProviderError error) =>
            new SpeechAudioAcquireResult(lease: null, error);

        private void ThrowIfDisposedLocked()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SpeechAudioFocusCoordinator));
            }
        }

        private static string BoundCode(string value)
        {
            string text = string.IsNullOrWhiteSpace(value)
                ? "audio_focus_denied"
                : value;
            return text.Length <= SpeechProviderError.MaximumCodeCharacters
                ? text
                : text.Substring(0, SpeechProviderError.MaximumCodeCharacters);
        }

        private static string BoundDiagnostic(string value)
        {
            string text = string.IsNullOrWhiteSpace(value)
                ? "Android speech audio focus operation failed without a diagnostic."
                : value;
            return text.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? text
                : text.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
        }
    }
}

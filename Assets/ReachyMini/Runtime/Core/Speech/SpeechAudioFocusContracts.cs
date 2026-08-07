#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum SpeechAudioRole
    {
        Listening = 0,
        Speaking = 1,
    }

    public enum SpeechAudioState
    {
        Idle = 0,
        Acquiring = 1,
        Listening = 2,
        Speaking = 3,
        Interrupted = 4,
        Releasing = 5,
        Faulted = 6,
        Disposed = 7,
    }

    public enum SpeechAudioInterruptionKind
    {
        PermanentFocusLoss = 0,
        TransientFocusLoss = 1,
        DuckRequested = 2,
        AudioRouteChanged = 3,
        BecomingNoisy = 4,
        PhoneOrCommunicationMode = 5,
        AlarmPlayback = 6,
        MicrophoneMuted = 7,
        PlatformFailure = 8,
    }

    public sealed class SpeechAudioInterruption
    {
        public SpeechAudioInterruption(
            SpeechAudioInterruptionKind kind,
            string code,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(SpeechAudioInterruptionKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Code = Bound(
                SpeechProviderDescriptor.RequireText(code, nameof(code)),
                SpeechProviderError.MaximumCodeCharacters);
            Diagnostic = Bound(
                SpeechProviderDescriptor.RequireText(diagnostic, nameof(diagnostic)),
                SpeechProviderError.MaximumDiagnosticCharacters);
        }

        public SpeechAudioInterruptionKind Kind { get; }
        public string Code { get; }
        public string Diagnostic { get; }

        internal SpeechProviderError ToProviderError() =>
            new SpeechProviderError(
                SpeechErrorCategory.ServiceFailure,
                Code,
                Diagnostic,
                isRetryable: true);

        private static string Bound(string value, int maximumCharacters) =>
            value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
    }

    public sealed class SpeechAudioFocusRequestResult
    {
        private SpeechAudioFocusRequestResult(
            bool granted,
            string code,
            string diagnostic)
        {
            IsGranted = granted;
            Code = SpeechProviderDescriptor.RequireText(code, nameof(code));
            Diagnostic = SpeechProviderDescriptor.RequireText(
                diagnostic,
                nameof(diagnostic));
        }

        public bool IsGranted { get; }
        public string Code { get; }
        public string Diagnostic { get; }

        public static SpeechAudioFocusRequestResult Granted(string diagnostic) =>
            new SpeechAudioFocusRequestResult(
                true,
                "audio_focus_granted",
                diagnostic);

        public static SpeechAudioFocusRequestResult Denied(
            string code,
            string diagnostic) =>
            new SpeechAudioFocusRequestResult(false, code, diagnostic);
    }

    public sealed class SpeechAudioPlatformInterruptionEventArgs : EventArgs
    {
        public SpeechAudioPlatformInterruptionEventArgs(
            string sessionId,
            SpeechAudioInterruption interruption)
        {
            SessionId = SpeechProviderDescriptor.RequireText(
                sessionId,
                nameof(sessionId));
            Interruption = interruption ??
                throw new ArgumentNullException(nameof(interruption));
        }

        public string SessionId { get; }
        public SpeechAudioInterruption Interruption { get; }
    }

    public interface ISpeechAudioFocusPlatform : IAsyncDisposable
    {
        event EventHandler<SpeechAudioPlatformInterruptionEventArgs>? Interrupted;

        ValueTask<SpeechAudioFocusRequestResult> RequestFocusAsync(
            string sessionId,
            SpeechAudioRole role,
            CancellationToken cancellationToken);

        ValueTask ReleaseFocusAsync(
            string sessionId,
            CancellationToken cancellationToken);
    }

    public sealed class SpeechAudioSnapshot
    {
        private readonly bool singleMicrophoneOnly;
        private readonly int maximumConcurrentMicrophoneCaptures;
        private readonly bool supportsSimultaneousListeningAndSpeaking;

        internal SpeechAudioSnapshot(
            SpeechAudioState state,
            SpeechAudioRole? activeRole,
            bool focusHeld,
            SpeechAudioInterruption? lastInterruption,
            SpeechProviderError? fault)
        {
            State = state;
            ActiveRole = activeRole;
            FocusHeld = focusHeld;
            LastInterruption = lastInterruption;
            Fault = fault;
            singleMicrophoneOnly = true;
            maximumConcurrentMicrophoneCaptures = 1;
            supportsSimultaneousListeningAndSpeaking = false;
        }

        public SpeechAudioState State { get; }
        public SpeechAudioRole? ActiveRole { get; }
        public bool FocusHeld { get; }
        public SpeechAudioInterruption? LastInterruption { get; }
        public SpeechProviderError? Fault { get; }
        public bool SingleMicrophoneOnly => singleMicrophoneOnly;
        public int MaximumConcurrentMicrophoneCaptures =>
            maximumConcurrentMicrophoneCaptures;
        public bool SupportsSimultaneousListeningAndSpeaking =>
            supportsSimultaneousListeningAndSpeaking;
    }

    public sealed class SpeechAudioAcquireResult
    {
        internal SpeechAudioAcquireResult(
            SpeechAudioFocusLease? lease,
            SpeechProviderError? error)
        {
            if ((lease == null) == (error == null))
            {
                throw new ArgumentException(
                    "An audio-focus acquisition must contain exactly one lease or error.");
            }

            Lease = lease;
            Error = error;
        }

        public bool IsGranted => Lease != null;
        public SpeechAudioFocusLease? Lease { get; }
        public SpeechProviderError? Error { get; }
    }

    public sealed class SpeechAudioFocusLease
    {
        private readonly SpeechAudioFocusCoordinator owner;
        private readonly SpeechAudioSession session;
        private int released;

        internal SpeechAudioFocusLease(
            SpeechAudioFocusCoordinator owner,
            SpeechAudioSession session)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public SpeechAudioRole Role => session.Role;
        public CancellationToken InterruptionToken => session.InterruptionToken;
        public SpeechAudioInterruption? Interruption => session.Interruption;

        public async ValueTask<SpeechProviderError?> ReleaseAsync()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return null;
            }

            return await owner.ReleaseAsync(session).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _ = await ReleaseAsync().ConfigureAwait(false);
        }
    }

    internal sealed class SpeechAudioSession : IDisposable
    {
        private readonly CancellationTokenSource interruptionCancellation =
            new CancellationTokenSource();
        private SpeechAudioInterruption? interruption;
        private int disposed;

        public SpeechAudioSession(string sessionId, SpeechAudioRole role)
        {
            SessionId = SpeechProviderDescriptor.RequireText(
                sessionId,
                nameof(sessionId));
            Role = role;
        }

        public string SessionId { get; }
        public SpeechAudioRole Role { get; }
        public bool FocusHeld { get; set; }
        public CancellationToken InterruptionToken => interruptionCancellation.Token;
        public SpeechAudioInterruption? Interruption =>
            Volatile.Read(ref interruption);

        public bool TryInterrupt(SpeechAudioInterruption value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (Interlocked.CompareExchange(ref interruption, value, null) != null)
            {
                return false;
            }

            try
            {
                interruptionCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                interruptionCancellation.Dispose();
            }
        }
    }
}

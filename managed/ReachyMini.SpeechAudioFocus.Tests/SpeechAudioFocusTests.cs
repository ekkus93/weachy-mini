#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class SpeechAudioFocusTests
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new (string, Func<Task>)[]
        {
            ("single-microphone-contract-is-explicit", SingleMicrophoneContractIsExplicit),
            ("listening-and-speaking-roles-reach-platform", RolesReachPlatform),
            ("focus-denial-stops-provider-start", FocusDenialStopsProviderStart),
            ("listening-and-speaking-never-overlap", ListeningAndSpeakingNeverOverlap),
            ("asr-terminal-waits-for-focus-release", AsrTerminalWaitsForRelease),
            ("tts-terminal-waits-for-focus-release", TtsTerminalWaitsForRelease),
            ("focus-loss-is-visible-and-cancels", FocusLossIsVisible),
            ("application-background-cancels-releases-and-blocks", ApplicationBackgroundCancelsReleasesAndBlocks),
            ("route-change-is-visible-and-cancels", RouteChangeIsVisible),
            ("phone-mode-is-visible-and-cancels", PhoneModeIsVisible),
            ("microphone-mute-is-visible-and-cancels", MicrophoneMuteIsVisible),
            ("stale-interruption-is-ignored", StaleInterruptionIsIgnored),
            ("release-failure-faults-coordinator", ReleaseFailureFaultsCoordinator),
            ("caller-cancellation-remains-cancellation", CallerCancellationRemainsCancellation),
            ("provider-mismatch-does-not-acquire-focus", ProviderMismatchDoesNotAcquire),
            ("java-focus-and-interruption-contracts-are-fail-closed", JavaSourceContracts),
            ("offline-default-and-permissions-remain-strict", OfflineDefaultAndPermissionContracts),
        };

    private static async Task SingleMicrophoneContractIsExplicit()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        SpeechAudioSnapshot snapshot = coordinator.Current;
        Require(snapshot.SingleMicrophoneOnly, "The single phone microphone limitation must be explicit.");
        Require(snapshot.MaximumConcurrentMicrophoneCaptures == 1, "Only one microphone capture may be active.");
        Require(!snapshot.SupportsSimultaneousListeningAndSpeaking, "Listening and speaking must never overlap.");
    }

    private static async Task RolesReachPlatform()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        SpeechAudioAcquireResult listening = await coordinator.AcquireAsync(
            SpeechAudioRole.Listening,
            CancellationToken.None).ConfigureAwait(false);
        Require(listening.IsGranted && listening.Lease != null, "Listening focus should be granted by the fake platform.");
        Require(platform.LastRole == SpeechAudioRole.Listening, "Listening role must reach the platform unchanged.");
        _ = await listening.Lease!.ReleaseAsync().ConfigureAwait(false);

        SpeechAudioAcquireResult speaking = await coordinator.AcquireAsync(
            SpeechAudioRole.Speaking,
            CancellationToken.None).ConfigureAwait(false);
        Require(speaking.IsGranted && speaking.Lease != null, "Speaking focus should be granted by the fake platform.");
        Require(platform.LastRole == SpeechAudioRole.Speaking, "Speaking role must reach the platform unchanged.");
        _ = await speaking.Lease!.ReleaseAsync().ConfigureAwait(false);
    }

    private static async Task FocusDenialStopsProviderStart()
    {
        await using var platform = new FakeAudioPlatform { DenyFocus = true };
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var rawAsr = new FakeAsrProvider();
        await using var rawTts = new FakeTtsProvider();
        await using var asr = new AudioCoordinatedAsrProvider(rawAsr, coordinator, ownsInner: false);
        await using var tts = new AudioCoordinatedTtsProvider(rawTts, coordinator, ownsInner: false);

        List<AsrEvent> asrEvents = await CollectAsync(asr.RecognizeAsync(
            CreateAsrRequest(asr, "asr-denied"),
            CancellationToken.None)).ConfigureAwait(false);
        List<TtsEvent> ttsEvents = await CollectAsync(tts.SpeakAsync(
            CreateTtsRequest(tts, "tts-denied"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(asrEvents, SpeechErrorCategory.ServiceFailure, "audio_focus_denied");
        RequireSingleFailure(ttsEvents, SpeechErrorCategory.ServiceFailure, "audio_focus_denied");
        Require(rawAsr.RecognizeCalls == 0 && rawTts.SpeakCalls == 0, "Provider work must not start after focus denial.");
    }

    private static async Task ListeningAndSpeakingNeverOverlap()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var rawAsr = new FakeAsrProvider { BlockUntilCancellation = true };
        await using var rawTts = new FakeTtsProvider();
        await using var asr = new AudioCoordinatedAsrProvider(rawAsr, coordinator, ownsInner: false);
        await using var tts = new AudioCoordinatedTtsProvider(rawTts, coordinator, ownsInner: false);
        using var cancellation = new CancellationTokenSource();

        Task<List<AsrEvent>> active = CollectAsync(asr.RecognizeAsync(
            CreateAsrRequest(asr, "listening-active", TimeSpan.FromSeconds(5)),
            cancellation.Token));
        await rawAsr.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        List<TtsEvent> competing = await CollectAsync(tts.SpeakAsync(
            CreateTtsRequest(tts, "speaking-competing"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(competing, SpeechErrorCategory.Busy, "speech_audio_busy");
        Require(rawTts.SpeakCalls == 0, "TTS must not start while ASR owns the single audio lease.");
        Require(platform.RequestCalls == 1, "Busy rejection must occur before a second Android focus request.");
        cancellation.Cancel();
        _ = await active.ConfigureAwait(false);
    }

    private static async Task AsrTerminalWaitsForRelease()
    {
        await using var platform = new FakeAudioPlatform { BlockRelease = true };
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeAsrProvider();
        await using var provider = new AudioCoordinatedAsrProvider(raw, coordinator, ownsInner: false);
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateAsrRequest(provider, "asr-release-order"),
            CancellationToken.None));
        await platform.ReleaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Require(!active.IsCompleted, "ASR terminal output must be withheld until the microphone lease is released.");
        platform.AllowRelease.TrySetResult(null);
        List<AsrEvent> events = await active.ConfigureAwait(false);
        Require(events[^1].Kind == AsrEventKind.FinalResult, "Final ASR output should appear only after release.");
        Require(coordinator.Current.State == SpeechAudioState.Idle, "Coordinator must be idle before final ASR result is exposed.");
    }

    private static async Task TtsTerminalWaitsForRelease()
    {
        await using var platform = new FakeAudioPlatform { BlockRelease = true };
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeTtsProvider();
        await using var provider = new AudioCoordinatedTtsProvider(raw, coordinator, ownsInner: false);
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateTtsRequest(provider, "tts-release-order"),
            CancellationToken.None));
        await platform.ReleaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Require(!active.IsCompleted, "TTS terminal output must be withheld until speaker focus is released.");
        platform.AllowRelease.TrySetResult(null);
        List<TtsEvent> events = await active.ConfigureAwait(false);
        Require(events[^1].Kind == TtsEventKind.Completed, "TTS completion should appear only after release.");
        Require(coordinator.Current.State == SpeechAudioState.Idle, "Coordinator must be idle before TTS completion is exposed.");
    }

    private static async Task ApplicationBackgroundCancelsReleasesAndBlocks()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeAsrProvider { BlockUntilCancellation = true };
        await using var provider = new AudioCoordinatedAsrProvider(raw, coordinator, ownsInner: false);
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateAsrRequest(provider, "application-background", TimeSpan.FromSeconds(5)),
            CancellationToken.None));
        await raw.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        coordinator.PauseForApplicationInterruption();
        List<AsrEvent> events = await active.ConfigureAwait(false);
        Require(
            events[^1].Kind == AsrEventKind.Failed &&
            events[^1].Error?.Code == "application_backgrounded",
            "Backgrounding must cancel active ASR with an explicit lifecycle failure.");
        Require(platform.ReleaseCalls == 1, "Backgrounded ASR must release focus exactly once.");
        Require(coordinator.IsApplicationPaused, "Coordinator must reject new audio while backgrounded.");

        int requestsBeforeBlockedAcquire = platform.RequestCalls;
        SpeechAudioAcquireResult blocked = await coordinator.AcquireAsync(
            SpeechAudioRole.Listening,
            CancellationToken.None).ConfigureAwait(false);
        Require(
            !blocked.IsGranted &&
            blocked.Error?.Code == "speech_audio_lifecycle_suspended",
            "Backgrounded speech audio must fail closed without touching the platform.");
        Require(
            platform.RequestCalls == requestsBeforeBlockedAcquire,
            "Backgrounded speech audio must not start another focus request.");

        coordinator.ResumeAfterApplicationInterruption();
        SpeechAudioAcquireResult resumed = await coordinator.AcquireAsync(
            SpeechAudioRole.Listening,
            CancellationToken.None).ConfigureAwait(false);
        Require(resumed.IsGranted && resumed.Lease != null, "Foreground resume must permit a new audio session.");
        _ = await resumed.Lease!.ReleaseAsync().ConfigureAwait(false);
    }

    private static Task FocusLossIsVisible() =>
        AssertAsrInterruptionAsync(
            SpeechAudioInterruptionKind.TransientFocusLoss,
            "audio_focus_loss_transient");

    private static Task PhoneModeIsVisible() =>
        AssertAsrInterruptionAsync(
            SpeechAudioInterruptionKind.PhoneOrCommunicationMode,
            "phone_or_communication_audio_mode");

    private static Task MicrophoneMuteIsVisible() =>
        AssertAsrInterruptionAsync(
            SpeechAudioInterruptionKind.MicrophoneMuted,
            "microphone_muted");

    private static async Task RouteChangeIsVisible()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeTtsProvider { BlockUntilCancellation = true };
        await using var provider = new AudioCoordinatedTtsProvider(raw, coordinator, ownsInner: false);
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateTtsRequest(provider, "route-change", TimeSpan.FromSeconds(5)),
            CancellationToken.None));
        await raw.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        platform.Interrupt(SpeechAudioInterruptionKind.AudioRouteChanged, "audio_route_removed");
        List<TtsEvent> events = await active.ConfigureAwait(false);
        Require(events[^1].Kind == TtsEventKind.Failed && events[^1].Error?.Code == "audio_route_removed",
            "Route change must become a visible terminal failure.");
        Require(platform.ReleaseCalls == 1, "Interrupted TTS must release focus exactly once.");
    }

    private static async Task StaleInterruptionIsIgnored()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        SpeechAudioAcquireResult first = await coordinator.AcquireAsync(
            SpeechAudioRole.Listening,
            CancellationToken.None).ConfigureAwait(false);
        Require(first.Lease != null, "Expected first lease.");
        string firstId = platform.ActiveSessionId ?? throw new InvalidOperationException("Expected fake session ID.");
        _ = await first.Lease!.ReleaseAsync().ConfigureAwait(false);

        SpeechAudioAcquireResult second = await coordinator.AcquireAsync(
            SpeechAudioRole.Speaking,
            CancellationToken.None).ConfigureAwait(false);
        Require(second.Lease != null, "Expected second lease.");
        platform.InterruptFor(
            firstId,
            SpeechAudioInterruptionKind.AudioRouteChanged,
            "stale_route_change");
        Require(coordinator.Current.State == SpeechAudioState.Speaking, "Stale callbacks must not cancel a newer session.");
        _ = await second.Lease!.ReleaseAsync().ConfigureAwait(false);
    }

    private static async Task ReleaseFailureFaultsCoordinator()
    {
        await using var platform = new FakeAudioPlatform { FailRelease = true };
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeTtsProvider();
        await using var provider = new AudioCoordinatedTtsProvider(raw, coordinator, ownsInner: false);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateTtsRequest(provider, "release-failure"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ServiceFailure, "audio_focus_release_failed");
        Require(coordinator.Current.State == SpeechAudioState.Faulted, "Release failure must fault the coordinator.");
        SpeechAudioAcquireResult next = await coordinator.AcquireAsync(
            SpeechAudioRole.Listening,
            CancellationToken.None).ConfigureAwait(false);
        Require(!next.IsGranted && next.Error != null && !next.Error.IsRetryable,
            "A faulted coordinator must fail closed until recreated.");
    }

    private static async Task CallerCancellationRemainsCancellation()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeAsrProvider { BlockUntilCancellation = true };
        await using var provider = new AudioCoordinatedAsrProvider(raw, coordinator, ownsInner: false);
        using var cancellation = new CancellationTokenSource();
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateAsrRequest(provider, "caller-cancel", TimeSpan.FromSeconds(5)),
            cancellation.Token));
        await raw.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        cancellation.Cancel();
        List<AsrEvent> events = await active.ConfigureAwait(false);
        Require(events[^1].Kind == AsrEventKind.Cancelled, "Caller cancellation must remain cancellation without platform interruption.");
        Require(platform.ReleaseCalls == 1, "Caller cancellation must release focus.");
    }

    private static async Task ProviderMismatchDoesNotAcquire()
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeAsrProvider();
        await using var provider = new AudioCoordinatedAsrProvider(raw, coordinator, ownsInner: false);
        var other = new SpeechProviderDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "other-asr",
            "other-instance",
            "Other ASR",
            "1",
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None);
        var request = new AsrRequest(
            new SpeechOperationContext(
                "mismatch",
                new SpeechProviderSelection(other).Current,
                TimeSpan.FromSeconds(1)),
            new AsrOptions("en-US", requestPartialResults: false));
        await AssertThrowsAsync<InvalidOperationException>(
            () => CollectAsync(provider.RecognizeAsync(request, CancellationToken.None))).ConfigureAwait(false);
        Require(platform.RequestCalls == 0, "Provider identity mismatch must fail before microphone focus acquisition.");
    }

    private static Task JavaSourceContracts()
    {
        string bridge = ReadRepositoryFile(
            "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachySpeechAudioFocusBridge.java");
        string monitor = ReadRepositoryFile(
            "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachySpeechAudioInterruptionMonitor.java");
        string source = bridge + "\n" + monitor;
        Require(bridge.Contains("AUDIOFOCUS_GAIN_TRANSIENT_EXCLUSIVE", StringComparison.Ordinal), "Listening must request transient-exclusive focus.");
        Require(bridge.Contains("setAcceptsDelayedFocusGain(false)", StringComparison.Ordinal), "Delayed focus must not be queued.");
        Require(bridge.Contains("setWillPauseWhenDucked(true)", StringComparison.Ordinal), "Ducking must not continue speech silently.");
        Require(bridge.Contains("abandonAudioFocusRequest", StringComparison.Ordinal), "Exact modern focus request must be abandoned.");
        Require(source.Contains("AudioDeviceCallback", StringComparison.Ordinal), "Audio-route changes must be observed.");
        Require(source.Contains("ACTION_AUDIO_BECOMING_NOISY", StringComparison.Ordinal), "Noisy headphone/Bluetooth transition must be observed.");
        Require(source.Contains("ACTION_MICROPHONE_MUTE_CHANGED", StringComparison.Ordinal), "Microphone mute must be observed.");
        Require(source.Contains("MODE_IN_CALL", StringComparison.Ordinal) && source.Contains("MODE_IN_COMMUNICATION", StringComparison.Ordinal),
            "Phone/communication audio modes must be observed where Android exposes them.");
        Require(source.Contains("AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK", StringComparison.Ordinal), "Duck-class focus loss must terminate speech explicitly.");
        Require(!source.Contains("startForegroundService", StringComparison.Ordinal), "RMA-125 must not silently start a foreground service.");
        Require(!source.Contains("TelephonyManager", StringComparison.Ordinal), "RMA-125 must not add phone-state surveillance.");
        return Task.CompletedTask;
    }

    private static Task OfflineDefaultAndPermissionContracts()
    {
        string stack = ReadRepositoryFile(
            "Assets/ReachyMini/Runtime/Application/ReachyAndroidSpeechAudioStack.cs");
        Require(stack.Contains("ReachyAndroidOnDeviceAsrProviderFactory.Create", StringComparison.Ordinal),
            "Offline default must use RMA-121 explicit on-device ASR.");
        Require(stack.Contains("ReachyAndroidOfflineTtsProviderFactory.Create", StringComparison.Ordinal),
            "Offline default must use RMA-123 offline TTS.");
        Require(!stack.Contains("ReachyAndroidSystemAsrProviderFactory.Create", StringComparison.Ordinal),
            "Offline default must not substitute system/network ASR.");
        Require(!stack.Contains("ReachyAndroidSystemTtsProviderFactory.Create", StringComparison.Ordinal),
            "Offline default must not substitute system/network TTS.");
        string manifest = ReadRepositoryFile(
            "Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/AndroidManifest.xml");
        Require(manifest.Contains("android.permission.RECORD_AUDIO", StringComparison.Ordinal), "Microphone permission must remain explicit.");
        Require(!manifest.Contains("READ_PHONE_STATE", StringComparison.Ordinal) && !manifest.Contains("READ_CALL_LOG", StringComparison.Ordinal),
            "RMA-125 must not add phone-state or call-log permissions.");
        return Task.CompletedTask;
    }

    private static async Task AssertAsrInterruptionAsync(
        SpeechAudioInterruptionKind kind,
        string code)
    {
        await using var platform = new FakeAudioPlatform();
        await using var coordinator = new SpeechAudioFocusCoordinator(platform);
        await using var raw = new FakeAsrProvider { BlockUntilCancellation = true };
        await using var provider = new AudioCoordinatedAsrProvider(raw, coordinator, ownsInner: false);
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateAsrRequest(provider, code, TimeSpan.FromSeconds(5)),
            CancellationToken.None));
        await raw.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        platform.Interrupt(kind, code);
        List<AsrEvent> events = await active.ConfigureAwait(false);
        Require(events[^1].Kind == AsrEventKind.Failed && events[^1].Error?.Code == code,
            "Platform interruption must become the exact visible failure code.");
        Require(platform.ReleaseCalls == 1, "Interrupted ASR must release focus exactly once.");
    }

    private static AsrRequest CreateAsrRequest(
        AudioCoordinatedAsrProvider provider,
        string requestId,
        TimeSpan? timeout = null) =>
        new AsrRequest(
            new SpeechOperationContext(
                requestId,
                new SpeechProviderSelection(provider.Descriptor).Current,
                timeout ?? TimeSpan.FromSeconds(2)),
            new AsrOptions("en-US", requestPartialResults: true));

    private static TtsRequest CreateTtsRequest(
        AudioCoordinatedTtsProvider provider,
        string requestId,
        TimeSpan? timeout = null) =>
        new TtsRequest(
            new SpeechOperationContext(
                requestId,
                new SpeechProviderSelection(provider.Descriptor).Current,
                timeout ?? TimeSpan.FromSeconds(2)),
            "hello",
            "offline-en");

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> stream)
    {
        var result = new List<T>();
        await foreach (T value in stream.ConfigureAwait(false))
        {
            result.Add(value);
        }
        return result;
    }

    private static void RequireSingleFailure(
        List<AsrEvent> events,
        SpeechErrorCategory category,
        string code)
    {
        int failureCount = 0;
        foreach (AsrEvent value in events)
        {
            if (value.Kind == AsrEventKind.Failed)
            {
                failureCount++;
            }
        }

        SpeechProviderError? error = events.Count > 0 ? events[^1].Error : null;
        Require(events.Count > 0 && failureCount == 1 &&
                events[^1].Kind == AsrEventKind.Failed &&
                error?.Category == category && error.Code == code,
            "Expected exactly one terminal ASR failure: " + category + "/" + code + ".");
    }

    private static void RequireSingleFailure(
        List<TtsEvent> events,
        SpeechErrorCategory category,
        string code)
    {
        int failureCount = 0;
        foreach (TtsEvent value in events)
        {
            if (value.Kind == TtsEventKind.Failed)
            {
                failureCount++;
            }
        }

        SpeechProviderError? error = events.Count > 0 ? events[^1].Error : null;
        Require(events.Count > 0 && failureCount == 1 &&
                events[^1].Kind == TtsEventKind.Failed &&
                error?.Category == category && error.Code == code,
            "Expected exactly one terminal TTS failure: " + category + "/" + code + ".");
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(
                current.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            current = current.Parent;
        }
        throw new FileNotFoundException("Could not locate repository source file.", relativePath);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeAudioPlatform : ISpeechAudioFocusPlatform
    {
        private string? activeSessionId;
        private bool disposed;

        public event EventHandler<SpeechAudioPlatformInterruptionEventArgs>? Interrupted;
        public bool DenyFocus { get; set; }
        public bool FailRelease { get; set; }
        public bool BlockRelease { get; set; }
        public int RequestCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public SpeechAudioRole? LastRole { get; private set; }
        public string? ActiveSessionId => activeSessionId;
        public TaskCompletionSource<object?> ReleaseStarted { get; } = NewSignal();
        public TaskCompletionSource<object?> AllowRelease { get; } = NewSignal();

        public ValueTask<SpeechAudioFocusRequestResult> RequestFocusAsync(
            string sessionId,
            SpeechAudioRole role,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            RequestCalls++;
            LastRole = role;
            if (DenyFocus)
            {
                return new ValueTask<SpeechAudioFocusRequestResult>(
                    SpeechAudioFocusRequestResult.Denied("audio_focus_denied", "Fake platform denied focus."));
            }
            if (activeSessionId != null)
            {
                return new ValueTask<SpeechAudioFocusRequestResult>(
                    SpeechAudioFocusRequestResult.Denied("audio_focus_busy", "Fake platform already has a session."));
            }
            activeSessionId = sessionId;
            return new ValueTask<SpeechAudioFocusRequestResult>(
                SpeechAudioFocusRequestResult.Granted("Fake platform granted focus."));
        }

        public async ValueTask ReleaseFocusAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCalls++;
            ReleaseStarted.TrySetResult(null);
            if (BlockRelease)
            {
                await AllowRelease.Task.ConfigureAwait(false);
            }
            if (FailRelease)
            {
                throw new InvalidOperationException("Fake release failure.");
            }
            if (activeSessionId != null && !string.Equals(activeSessionId, sessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Fake release identity mismatch.");
            }
            activeSessionId = null;
        }

        public void Interrupt(SpeechAudioInterruptionKind kind, string code)
        {
            string id = activeSessionId ?? throw new InvalidOperationException("No active fake audio session.");
            InterruptFor(id, kind, code);
        }

        public void InterruptFor(string sessionId, SpeechAudioInterruptionKind kind, string code) =>
            Interrupted?.Invoke(
                this,
                new SpeechAudioPlatformInterruptionEventArgs(
                    sessionId,
                    new SpeechAudioInterruption(kind, code, "Fake interruption: " + code)));

        public ValueTask DisposeAsync()
        {
            disposed = true;
            activeSessionId = null;
            return default;
        }
    }

    private sealed class FakeAsrProvider : IAsrProvider
    {
        private static readonly string[] SupportedLanguages = { "en-US" };

        public FakeAsrProvider()
        {
            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                "fake-asr",
                "fake-asr-instance",
                "Fake ASR",
                "1",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            Capabilities = new AsrCapabilities(
                SupportedLanguages,
                supportsPartialResults: true,
                supportsCancellation: true,
                TimeSpan.FromMinutes(1));
        }

        public SpeechProviderDescriptor Descriptor { get; }
        public AsrCapabilities Capabilities { get; }
        public bool BlockUntilCancellation { get; set; }
        public int RecognizeCalls { get; private set; }
        public TaskCompletionSource<object?> Started { get; } = NewSignal();

        public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            AsrOptions options,
            CancellationToken cancellationToken) =>
            new ValueTask<SpeechProviderAvailability>(
                new SpeechProviderAvailability(SpeechAvailabilityState.Available, "Fake ASR available."));

        public async IAsyncEnumerable<AsrEvent> RecognizeAsync(
            AsrRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RecognizeCalls++;
            yield return new AsrEvent(Descriptor.InstanceId, request.Context.RequestId, 1UL, AsrEventKind.Started);
            Started.TrySetResult(null);
            if (BlockUntilCancellation)
            {
                bool cancelled = false;
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                if (cancelled)
                {
                    yield return new AsrEvent(Descriptor.InstanceId, request.Context.RequestId, 2UL, AsrEventKind.Cancelled);
                    yield break;
                }
            }
            yield return new AsrEvent(Descriptor.InstanceId, request.Context.RequestId, 2UL, AsrEventKind.FinalResult, "hello");
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class FakeTtsProvider : ITtsProvider
    {
        public FakeTtsProvider()
        {
            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.TextToSpeech,
                "fake-tts",
                "fake-tts-instance",
                "Fake TTS",
                "1",
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            Capabilities = new TtsCapabilities(supportsCancellation: true, maximumInputCharacters: 1000);
        }

        public SpeechProviderDescriptor Descriptor { get; }
        public TtsCapabilities Capabilities { get; }
        public bool BlockUntilCancellation { get; set; }
        public int SpeakCalls { get; private set; }
        public TaskCompletionSource<object?> Started { get; } = NewSignal();

        public ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
            new ValueTask<SpeechProviderAvailability>(
                new SpeechProviderAvailability(SpeechAvailabilityState.Available, "Fake TTS available."));

        public ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken cancellationToken) =>
            new ValueTask<IReadOnlyList<TtsVoice>>(
                new TtsVoice[]
                {
                    new TtsVoice("offline-en", "Offline English", "en-US", SpeechNetworkRequirement.None, isInstalled: true),
                });

        public async IAsyncEnumerable<TtsEvent> SpeakAsync(
            TtsRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            SpeakCalls++;
            yield return new TtsEvent(Descriptor.InstanceId, request.Context.RequestId, 1UL, TtsEventKind.Started);
            Started.TrySetResult(null);
            if (BlockUntilCancellation)
            {
                bool cancelled = false;
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                if (cancelled)
                {
                    yield return new TtsEvent(Descriptor.InstanceId, request.Context.RequestId, 2UL, TtsEventKind.Cancelled);
                    yield break;
                }
            }
            yield return new TtsEvent(Descriptor.InstanceId, request.Context.RequestId, 2UL, TtsEventKind.Completed);
        }

        public ValueTask DisposeAsync() => default;
    }

    private static TaskCompletionSource<object?> NewSignal() =>
        new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
}

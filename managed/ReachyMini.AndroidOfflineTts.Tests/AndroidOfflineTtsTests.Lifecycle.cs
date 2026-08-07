#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOfflineTtsTests
{
    private static async Task ConcurrentOperationIsBusy()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            BlockSpeechUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform);
        using var cancellation = new CancellationTokenSource();
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "busy-active", timeout: TimeSpan.FromSeconds(5)),
            cancellation.Token));
        await platform.SpeechStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);

        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Busy,
            "Concurrent RMA-123 availability must fail busy rather than queue.");

        List<TtsEvent> competing = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "busy-second"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(competing, SpeechErrorCategory.Busy);
        Require(platform.SpeakCalls == 1,
            "Busy operation must not queue or start a second synthesis.");

        cancellation.Cancel();
        List<TtsEvent> activeEvents = await active.ConfigureAwait(false);
        Require(activeEvents.Count == 1 && activeEvents[0].Kind == TtsEventKind.Cancelled,
            "Cancelling the active utterance must terminate it explicitly.");
    }

    private static async Task CallerCancellationReachesPlatform()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            BlockSpeechUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform);
        using var cancellation = new CancellationTokenSource();
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "caller-cancel", timeout: TimeSpan.FromSeconds(5)),
            cancellation.Token));
        await platform.SpeechStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        cancellation.Cancel();
        List<TtsEvent> events = await active.ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == TtsEventKind.Cancelled,
            "Caller cancellation must surface as cancelled.");
        Require(platform.CancellationObservations == 1,
            "Caller cancellation must reach the platform token.");
    }

    private static async Task OperationTimeoutReachesPlatform()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            BlockSpeechUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "timeout", timeout: TimeSpan.FromMilliseconds(40)),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.Timeout);
        Require(platform.CancellationObservations == 1,
            "RMA-123 operation timeout must cancel the platform token.");
    }

    private static async Task RequestIdentityMismatchFailsClosed()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetEvents(Event(
            "different-request",
            AndroidOfflineTtsPlatformEventKind.Completed));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "expected-request"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
    }

    private static async Task ProviderSelectionCannotRedirect()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        await using var provider = CreateProvider(platform);
        var other = new SpeechProviderDescriptor(
            SpeechProviderKind.TextToSpeech,
            "other-tts",
            "other-instance",
            "Other TTS",
            "1",
            SpeechProviderLocation.DeviceService,
            SpeechNetworkRequirement.None);
        var selection = new SpeechProviderSelection(other);
        var context = new SpeechOperationContext(
            "redirect",
            selection.Current,
            TimeSpan.FromSeconds(1));
        var request = new TtsRequest(context, "hello", "offline-en");
        await AssertThrowsAsync<InvalidOperationException>(
            () => CollectAsync(provider.SpeakAsync(request, CancellationToken.None)))
            .ConfigureAwait(false);
        Require(platform.ProbeCalls == 0 &&
                platform.VoiceCalls == 0 &&
                platform.SpeakCalls == 0,
            "Provider identity mismatch must fail before touching Android TTS.");
    }

    private static async Task PlatformStreamExceptionIsVisible()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            StreamException = new InvalidOperationException("stream failure"),
        };
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "stream-exception"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ServiceFailure);
    }

    private static async Task MissingTerminalCallbackIsVisible()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            EndWithoutTerminal = true,
        };
        platform.SetEvents(Event(
            "unterminated",
            AndroidOfflineTtsPlatformEventKind.Started));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "unterminated"),
            CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 2 && events[1].Kind == TtsEventKind.Failed,
            "A platform stream ending without terminal callback must fail visibly.");
        Require(events[1].Error?.Category == SpeechErrorCategory.ServiceFailure,
            "Missing terminal callback must map to service failure.");
    }

    private static async Task FailedUtteranceIsNotRetried()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetEvents(FailureEvent(
            "no-retry",
            AndroidOfflineTtsFailureKind.ServiceFailure,
            "service_failure"));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "no-retry"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ServiceFailure);
        Require(platform.SpeakCalls == 1,
            "RMA-123 must not retry a failed utterance automatically.");
    }

    private static async Task DisposalCancelsAndReleasesPlatform()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            BlockSpeechUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform);
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "dispose-active", timeout: TimeSpan.FromSeconds(5)),
            CancellationToken.None));
        await platform.SpeechStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
        List<TtsEvent> events = await active.ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == TtsEventKind.Cancelled,
            "Provider disposal must cancel the active utterance.");
        Require(platform.CancellationObservations == 1,
            "Provider disposal must cancel the platform token.");
        Require(platform.DisposeCalls == 1,
            "Provider disposal must release the platform exactly once.");
    }

    private static async Task DisposedProviderRejectsAvailability()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        await using var provider = CreateProvider(platform);
        await provider.DisposeAsync().ConfigureAwait(false);
        await AssertThrowsAsync<ObjectDisposedException>(async () =>
        {
            _ = await provider.CheckAvailabilityAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static AndroidOfflineTtsProvider CreateProvider(
        FakeAndroidOfflineTtsPlatform platform) =>
        new AndroidOfflineTtsProvider(
            platform,
            "android-offline-default",
            Language);

    private static TtsRequest CreateRequest(
        AndroidOfflineTtsProvider provider,
        string requestId,
        string text = "hello from RMA-123",
        string voiceId = "offline-en",
        TimeSpan? timeout = null)
    {
        var selection = new SpeechProviderSelection(provider.Descriptor);
        var context = new SpeechOperationContext(
            requestId,
            selection.Current,
            timeout ?? TimeSpan.FromSeconds(2));
        return new TtsRequest(context, text, voiceId);
    }

    private static AndroidOfflineTtsPlatformEvent Event(
        string requestId,
        AndroidOfflineTtsPlatformEventKind kind) =>
        new AndroidOfflineTtsPlatformEvent(requestId, kind);

    private static AndroidOfflineTtsPlatformEvent FailureEvent(
        string requestId,
        AndroidOfflineTtsFailureKind kind,
        string code) =>
        new AndroidOfflineTtsPlatformEvent(
            requestId,
            AndroidOfflineTtsPlatformEventKind.Failed,
            new AndroidOfflineTtsPlatformFailure(
                kind,
                code,
                "Synthetic RMA-123 failure."));

    private static async Task<List<TtsEvent>> CollectAsync(
        IAsyncEnumerable<TtsEvent> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<TtsEvent>();
        await foreach (TtsEvent value in source.ConfigureAwait(false))
        {
            result.Add(value);
        }
        return result;
    }

    private static void RequireSingleFailure(
        List<TtsEvent> events,
        SpeechErrorCategory category)
    {
        ArgumentNullException.ThrowIfNull(events);
        Require(events.Count == 1, "Expected exactly one terminal TTS failure event.");
        Require(events[0].Kind == TtsEventKind.Failed,
            "Expected a failed TTS event.");
        Require(events[0].Error?.Category == category,
            "TTS failure category did not match the expected RMA-123 contract.");
    }


    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(
            "Expected exception " + typeof(TException).Name + ".");
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(
            "Expected exception " + typeof(TException).Name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

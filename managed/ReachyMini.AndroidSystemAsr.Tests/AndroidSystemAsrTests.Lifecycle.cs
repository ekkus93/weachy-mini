#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidSystemAsrTests
{
    private static async Task ConcurrentOperationIsBusy()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            BlockRecognitionUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "busy-active", timeout: TimeSpan.FromSeconds(5)),
            cancellation.Token));
        await platform.RecognitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);

        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            new AsrOptions(Language, false),
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Busy,
            "A concurrent provider operation must fail busy rather than queue.");

        cancellation.Cancel();
        List<AsrEvent> activeEvents = await active.ConfigureAwait(false);
        Require(activeEvents.Count == 1 && activeEvents[0].Kind == AsrEventKind.Cancelled,
            "Cancelling the active utterance must terminate it explicitly.");
    }

    private static async Task CallerCancellationReachesPlatform()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            BlockRecognitionUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "caller-cancel", timeout: TimeSpan.FromSeconds(5)),
            cancellation.Token));
        await platform.RecognitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        cancellation.Cancel();
        List<AsrEvent> events = await active.ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == AsrEventKind.Cancelled,
            "Caller cancellation must be surfaced as cancelled.");
        Require(platform.CancellationObservations == 1,
            "Caller cancellation must reach the platform token.");
    }

    private static async Task OperationTimeoutReachesPlatform()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            BlockRecognitionUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform, TimeSpan.FromSeconds(5));
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "timeout", timeout: TimeSpan.FromMilliseconds(40)),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.Timeout);
        Require(platform.CancellationObservations == 1,
            "Operation timeout must cancel the platform token.");
    }

    private static async Task MaximumUtteranceCapsTimeout()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            BlockRecognitionUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform, TimeSpan.FromMilliseconds(40));
        var stopwatch = Stopwatch.StartNew();
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "max-duration", timeout: TimeSpan.FromSeconds(3)),
            CancellationToken.None)).ConfigureAwait(false);
        stopwatch.Stop();
        RequireSingleFailure(events, SpeechErrorCategory.Timeout);
        Require(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            "Maximum utterance duration must cap a longer operation timeout.");
    }

    private static async Task RequestIdentityMismatchFailsClosed()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        platform.SetEvents(Event(
            "different-request",
            AndroidSystemAsrPlatformEventKind.FinalResult,
            "wrong"));
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "expected-request"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
    }

    private static async Task ProviderSelectionCannotRedirect()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        await using var provider = CreateProvider(platform);
        var other = new SpeechProviderDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            "other-asr",
            "other-instance",
            "Other ASR",
            "1",
            SpeechProviderLocation.DeviceService,
            SpeechNetworkRequirement.ProviderControlled);
        var selection = new SpeechProviderSelection(other);
        var context = new SpeechOperationContext(
            "redirect",
            selection.Current,
            TimeSpan.FromSeconds(1));
        var request = new AsrRequest(context, new AsrOptions(Language, false));
        await AssertThrowsAsync<InvalidOperationException>(
            () => CollectAsync(provider.RecognizeAsync(request, CancellationToken.None)))
            .ConfigureAwait(false);
        Require(platform.ProbeCalls == 0 && platform.RecognizeCalls == 0,
            "Provider identity mismatch must fail before touching the platform.");
    }

    private static async Task PlatformStreamExceptionIsVisible()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            StreamException = new InvalidOperationException("stream failure"),
        };
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "stream-exception"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ServiceFailure);
    }

    private static async Task MissingTerminalCallbackIsVisible()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            EndWithoutTerminal = true,
        };
        platform.SetEvents(Event("unterminated", AndroidSystemAsrPlatformEventKind.Started));
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "unterminated"),
            CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 2 && events[1].Kind == AsrEventKind.Failed,
            "A stream ending without a terminal callback must fail visibly.");
        Require(events[1].Error?.Category == SpeechErrorCategory.ServiceFailure,
            "Missing terminal callback must map to service failure.");
    }

    private static async Task FailedUtteranceIsNotRetried()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        platform.SetEvents(FailureEvent(
            "no-retry",
            AndroidSystemAsrFailureKind.NetworkFailure,
            "network_failure"));
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "no-retry"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.Network);
        Require(platform.RecognizeCalls == 1,
            "RMA-122 provider must not retry a failed utterance automatically.");
    }

    private static async Task DisposalCancelsAndDestroysPlatform()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            BlockRecognitionUntilCancellation = true,
        };
        await using var provider = CreateProvider(platform, TimeSpan.FromSeconds(5));
        Task<List<AsrEvent>> active = CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "dispose-active", timeout: TimeSpan.FromSeconds(5)),
            CancellationToken.None));
        await platform.RecognitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
        List<AsrEvent> events = await active.ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == AsrEventKind.Cancelled,
            "Disposal must cancel the active utterance.");
        Require(platform.CancellationObservations == 1,
            "Disposal must cancel the platform token.");
        Require(platform.DisposeCalls == 1,
            "Disposal must destroy the platform exactly once.");
    }

    private static async Task DisposedProviderRejectsAvailability()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        var provider = CreateProvider(platform);
        await provider.DisposeAsync().ConfigureAwait(false);
        await AssertThrowsAsync<ObjectDisposedException>(
            async () =>
            {
                _ = await provider.CheckAvailabilityAsync(
                    new AsrOptions(Language, false),
                    CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static async Task VerifyFailureMappingAsync(
        AndroidSystemAsrFailureKind kind,
        string code,
        SpeechErrorCategory category,
        bool expectedRetryable)
    {
        string requestId = "failure-" + code;
        await using var platform = new FakeAndroidSystemAsrPlatform();
        platform.SetEvents(FailureEvent(requestId, kind, code));
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, requestId),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, category);
        Require(events[0].Error?.IsRetryable == expectedRetryable,
            "Failure retryability classification did not match the RMA-122 contract.");
    }

    private static AndroidSystemAsrProvider CreateProvider(
        FakeAndroidSystemAsrPlatform platform,
        TimeSpan? maximumUtteranceDuration = null)
    {
        return new AndroidSystemAsrProvider(
            platform,
            "android-system-default",
            Language,
            maximumUtteranceDuration ?? TimeSpan.FromSeconds(30));
    }

    private static AsrRequest CreateRequest(
        AndroidSystemAsrProvider provider,
        string requestId,
        bool partialResults = false,
        TimeSpan? timeout = null)
    {
        var selection = new SpeechProviderSelection(provider.Descriptor);
        var context = new SpeechOperationContext(
            requestId,
            selection.Current,
            timeout ?? TimeSpan.FromSeconds(2));
        return new AsrRequest(
            context,
            new AsrOptions(Language, partialResults));
    }

    private static AndroidSystemAsrPlatformEvent Event(
        string requestId,
        AndroidSystemAsrPlatformEventKind kind,
        string? transcript = null)
    {
        return new AndroidSystemAsrPlatformEvent(requestId, kind, transcript);
    }

    private static AndroidSystemAsrPlatformEvent FailureEvent(
        string requestId,
        AndroidSystemAsrFailureKind kind,
        string code)
    {
        return new AndroidSystemAsrPlatformEvent(
            requestId,
            AndroidSystemAsrPlatformEventKind.Failed,
            failure: new AndroidSystemAsrPlatformFailure(
                kind,
                code,
                "Synthetic RMA-122 failure."));
    }

    private static async Task<List<AsrEvent>> CollectAsync(
        IAsyncEnumerable<AsrEvent> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<AsrEvent>();
        await foreach (AsrEvent value in source.ConfigureAwait(false))
        {
            result.Add(value);
        }
        return result;
    }

    private static void RequireSingleFailure(
        List<AsrEvent> events,
        SpeechErrorCategory category)
    {
        Require(events.Count == 1, "Expected exactly one terminal failure event.");
        Require(events[0].Kind == AsrEventKind.Failed,
            "Expected a failed ASR event.");
        Require(events[0].Error?.Category == category,
            "Failure category did not match the expected contract.");
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

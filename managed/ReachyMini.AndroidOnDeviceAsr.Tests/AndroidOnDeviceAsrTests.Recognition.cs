#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOnDeviceAsrTests
{
    private static async Task PartialAndFinalResults()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            requestId => Event(requestId, AndroidOnDeviceAsrPlatformEventKind.Started),
            requestId => Event(
                requestId,
                AndroidOnDeviceAsrPlatformEventKind.PartialResult,
                "hello"),
            requestId => Event(
                requestId,
                AndroidOnDeviceAsrPlatformEventKind.FinalResult,
                "hello world"));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);

        AssertEqual(3, events.Count, "event count");
        AssertEqual(AsrEventKind.Started, events[0].Kind, "start event");
        AssertEqual(AsrEventKind.PartialResult, events[1].Kind, "partial event");
        AssertEqual("hello", events[1].Transcript, "partial transcript");
        AssertEqual(AsrEventKind.FinalResult, events[2].Kind, "final event");
        AssertEqual("hello world", events[2].Transcript, "final transcript");
        AssertEqual(1UL, events[0].Sequence, "start sequence");
        AssertEqual(3UL, events[2].Sequence, "final sequence");
    }

    private static async Task NoMatch()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            requestId => Event(requestId, AndroidOnDeviceAsrPlatformEventKind.NoMatch));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);

        AssertEqual(1, events.Count, "no-match event count");
        AssertEqual(AsrEventKind.NoMatch, events[0].Kind, "no-match kind");
    }

    private static async Task BusyWithoutQueue()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            BlockUntilCancelled = true,
        };
        await using var provider = CreateProvider(platform);
        using var firstCancellation = new CancellationTokenSource();
        Task<IReadOnlyList<AsrEvent>> first =
            CollectAsync(
                provider.RecognizeAsync(
                    Request(provider, "first"),
                    firstCancellation.Token));

        await platform.RecognitionStarted.Task.ConfigureAwait(false);

        IReadOnlyList<AsrEvent> second =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider, "second"),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(
            second,
            SpeechErrorCategory.Busy,
            "android_on_device_asr_busy");
        AssertEqual(1, platform.RecognitionStartCount, "recognizer start count");

        firstCancellation.Cancel();
        IReadOnlyList<AsrEvent> firstEvents = await first.ConfigureAwait(false);
        AssertEqual(
            AsrEventKind.Cancelled,
            firstEvents[firstEvents.Count - 1].Kind,
            "first cancellation");
    }
}

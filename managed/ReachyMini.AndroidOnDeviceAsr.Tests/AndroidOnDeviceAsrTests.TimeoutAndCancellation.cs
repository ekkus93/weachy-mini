#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOnDeviceAsrTests
{
    private static async Task PlatformSpeechTimeout()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            requestId => FailedPlatform(
                requestId,
                AndroidOnDeviceAsrFailureKind.Timeout,
                "speech_timeout",
                "speech timeout"));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(
            events,
            SpeechErrorCategory.Timeout,
            "speech_timeout");
    }

    private static async Task OperationTimeoutCancels()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            BlockUntilCancelled = true,
        };
        await using var provider = CreateProvider(
            platform,
            TimeSpan.FromSeconds(1));

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(
                        provider,
                        timeout: TimeSpan.FromMilliseconds(30)),
                    CancellationToken.None)).ConfigureAwait(false);

        AssertSingleFailure(
            events,
            SpeechErrorCategory.Timeout,
            "android_on_device_asr_operation_timeout");
        Assert(platform.CancellationObserved, "platform cancellation must be observed");
    }

    private static async Task CallerCancellationReachesPlatform()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            BlockUntilCancelled = true,
        };
        await using var provider = CreateProvider(platform);
        using var cancellation = new CancellationTokenSource();
        Task<IReadOnlyList<AsrEvent>> collection =
            CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    cancellation.Token));

        await platform.RecognitionStarted.Task.ConfigureAwait(false);
        cancellation.Cancel();
        IReadOnlyList<AsrEvent> events =
            await collection.ConfigureAwait(false);

        AssertEqual(
            AsrEventKind.Cancelled,
            events[events.Count - 1].Kind,
            "cancel event");
        Assert(platform.CancellationObserved, "platform cancellation must be observed");
    }

    private static async Task MaximumUtteranceCapsTimeout()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            BlockUntilCancelled = true,
        };
        await using var provider = CreateProvider(
            platform,
            TimeSpan.FromMilliseconds(35));

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(
                        provider,
                        timeout: TimeSpan.FromSeconds(2)),
                    CancellationToken.None)).ConfigureAwait(false);

        AssertSingleFailure(
            events,
            SpeechErrorCategory.Timeout,
            "android_on_device_asr_operation_timeout");
        Assert(platform.CancellationObserved, "maximum utterance timeout reaches platform");
    }
}

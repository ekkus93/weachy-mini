#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOnDeviceAsrTests
{
    private static async Task ProviderRedirectRejected()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);
        SpeechProviderDescriptor other = new SpeechProviderDescriptor(
            SpeechProviderKind.AutomaticSpeechRecognition,
            AndroidOnDeviceAsrProvider.ProviderId,
            "other-instance",
            "Other explicit local ASR",
            AndroidOnDeviceAsrProvider.ProviderVersion,
            SpeechProviderLocation.OnDevice,
            SpeechNetworkRequirement.None);
        var selection = new SpeechProviderSelection(other);
        var request = new AsrRequest(
            new SpeechOperationContext(
                "redirect",
                selection.Current,
                TimeSpan.FromSeconds(1)),
            Options());

        await ExpectThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await CollectAsync(
                    provider.RecognizeAsync(
                        request,
                        CancellationToken.None)).ConfigureAwait(false);
            }).ConfigureAwait(false);
        AssertEqual(0, platform.RecognitionStartCount, "redirected start count");
    }

    private static async Task NoAutomaticRetry()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            requestId => FailedPlatform(
                requestId,
                AndroidOnDeviceAsrFailureKind.ServiceFailure,
                "service_failure",
                "service failure"));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);

        AssertSingleFailure(
            events,
            SpeechErrorCategory.ServiceFailure,
            "service_failure");
        AssertEqual(1, platform.RecognitionStartCount, "recognizer starts");
    }

    private static async Task DisposeCancelsActive()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            BlockUntilCancelled = true,
        };
        await using var provider = CreateProvider(platform);
        Task<IReadOnlyList<AsrEvent>> collection =
            CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None));

        await platform.RecognitionStarted.Task.ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
        IReadOnlyList<AsrEvent> events =
            await collection.ConfigureAwait(false);

        AssertEqual(
            AsrEventKind.Cancelled,
            events[events.Count - 1].Kind,
            "dispose cancellation");
        Assert(platform.CancellationObserved, "dispose cancellation reaches platform");
        AssertEqual(1, platform.DisposeCount, "platform dispose count");
    }

    private static async Task DisposedProviderRejectsUse()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);
        await provider.DisposeAsync().ConfigureAwait(false);

        await ExpectThrowsAsync<ObjectDisposedException>(
            async () =>
            {
                _ = await provider.CheckAvailabilityAsync(
                    Options(),
                    CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }
}

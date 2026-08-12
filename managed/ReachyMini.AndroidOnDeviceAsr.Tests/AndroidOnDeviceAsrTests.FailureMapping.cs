#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOnDeviceAsrTests
{
    private static async Task ServiceDisconnected()
    {
        await AssertFailureMapping(
            AndroidOnDeviceAsrFailureKind.ServiceDisconnected,
            "service_disconnected",
            SpeechErrorCategory.ServiceFailure).ConfigureAwait(false);
    }

    private static async Task LanguageModelUnavailable()
    {
        await AssertFailureMapping(
            AndroidOnDeviceAsrFailureKind.LanguageModelUnavailable,
            "language_model_unavailable",
            SpeechErrorCategory.UnsupportedLanguage).ConfigureAwait(false);
    }

    private static async Task RuntimeUnsupportedLanguage()
    {
        await AssertFailureMapping(
            AndroidOnDeviceAsrFailureKind.LanguageNotSupported,
            "language_not_supported",
            SpeechErrorCategory.UnsupportedLanguage).ConfigureAwait(false);
    }

    private static async Task NetworkFailureIsContractViolation()
    {
        await AssertFailureMapping(
            AndroidOnDeviceAsrFailureKind.UnexpectedNetworkFailure,
            "unexpected_network_error",
            SpeechErrorCategory.ContractViolation).ConfigureAwait(false);
    }

    private static async Task CallbackIdentityMismatch()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            _ => Event(
                "different-request",
                AndroidOnDeviceAsrPlatformEventKind.FinalResult,
                "wrong request"));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(
            events,
            SpeechErrorCategory.ContractViolation,
            "android_on_device_asr_request_identity_mismatch");
    }

    private static async Task StreamEndsWithoutTerminal()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            requestId => Event(
                requestId,
                AndroidOnDeviceAsrPlatformEventKind.Started));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertEqual(2, events.Count, "stream-ended event count");
        AssertEqual(AsrEventKind.Started, events[0].Kind, "started event");
        AssertFailure(
            events[1],
            SpeechErrorCategory.ServiceFailure,
            "android_on_device_asr_stream_ended_without_terminal_event");
    }

    private static async Task PlatformExceptionVisible()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            ThrowRecognitionException = true,
        };
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(
            events,
            SpeechErrorCategory.ServiceFailure,
            "android_on_device_asr_platform_exception");
    }

    private static async Task AssertFailureMapping(
        AndroidOnDeviceAsrFailureKind platformKind,
        string code,
        SpeechErrorCategory expectedCategory)
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        platform.SetEvents(
            requestId => FailedPlatform(
                requestId,
                platformKind,
                code,
                "mapped failure"));
        await using var provider = CreateProvider(platform);

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(events, expectedCategory, code);
    }
}

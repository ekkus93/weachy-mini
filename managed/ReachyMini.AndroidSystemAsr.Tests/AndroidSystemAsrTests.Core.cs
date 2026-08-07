#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidSystemAsrTests
{
    private const string Language = "en-US";

    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new List<(string, Func<Task>)>
        {
            ("Descriptor labels system ASR as provider-controlled network-capable", DescriptorIsExplicit),
            ("System and explicit on-device provider identities remain distinct", ProviderIdentityIsDistinct),
            ("API 26 system recognizer can be available", Api26CanBeAvailable),
            ("Missing system recognition service is unavailable", MissingServiceIsUnavailable),
            ("Microphone permission is required", MicrophonePermissionIsRequired),
            ("Configured language is enforced before platform probing", ConfiguredLanguageIsEnforced),
            ("Probe failures remain visible", ProbeFailureIsVisible),
            ("Partial and final results preserve order", PartialAndFinalResultsPreserveOrder),
            ("No-match remains explicit", NoMatchIsExplicit),
            ("Network failure remains a network failure", NetworkFailureIsVisible),
            ("Network timeout remains a timeout", NetworkTimeoutIsVisible),
            ("Speech timeout remains a timeout", SpeechTimeoutIsVisible),
            ("Service disconnect remains visible", ServiceDisconnectIsVisible),
            ("Unsupported language remains visible", UnsupportedLanguageIsVisible),
            ("Unavailable language remains visible", UnavailableLanguageIsVisible),
            ("Concurrent operations fail busy without queueing", ConcurrentOperationIsBusy),
            ("Caller cancellation reaches the Android system platform", CallerCancellationReachesPlatform),
            ("Operation timeout cancels the Android system platform", OperationTimeoutReachesPlatform),
            ("Maximum utterance duration caps request timeout", MaximumUtteranceCapsTimeout),
            ("Mismatched callback request identity fails closed", RequestIdentityMismatchFailsClosed),
            ("Provider selection cannot redirect to another ASR instance", ProviderSelectionCannotRedirect),
            ("Platform stream exception is visible", PlatformStreamExceptionIsVisible),
            ("Missing terminal callback is visible", MissingTerminalCallbackIsVisible),
            ("Failed utterance is not retried", FailedUtteranceIsNotRetried),
            ("Disposal cancels active recognition and destroys platform", DisposalCancelsAndDestroysPlatform),
            ("Disposed provider rejects availability checks", DisposedProviderRejectsAvailability),
            ("Java bridge uses only the system recognizer factory", Rma122SourceContracts.JavaBridgeUsesSystemRecognizerOnly),
            ("Unity bridge marshals system callbacks without fallback", Rma122SourceContracts.UnityBridgeMarshalsCallbacksWithoutFallback),
            ("Speech manifest still declares required permission and service visibility", Rma122SourceContracts.ManifestDeclaresSpeechRequirements),
        };

    private static async Task DescriptorIsExplicit()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        await using var provider = CreateProvider(platform);
        Require(provider.Descriptor.Location == SpeechProviderLocation.DeviceService,
            "System ASR must be labeled as a device service.");
        Require(provider.Descriptor.NetworkRequirement == SpeechNetworkRequirement.ProviderControlled,
            "System ASR must disclose provider-controlled network behavior.");
        Require(provider.Descriptor.MayUseNetwork,
            "System ASR must report that it may use networking.");
        Require(provider.Descriptor.RequiresNetworkDisclosure,
            "System ASR must require network disclosure.");
        Require(provider.Descriptor.DisplayName.Contains("may use network", StringComparison.OrdinalIgnoreCase),
            "System ASR display name must make possible network use visible.");
    }

    private static Task ProviderIdentityIsDistinct()
    {
        Require(!string.Equals(
                AndroidSystemAsrProvider.ProviderId,
                AndroidOnDeviceAsrProvider.ProviderId,
                StringComparison.Ordinal),
            "RMA-122 must not alias the RMA-121 provider identity.");
        return Task.CompletedTask;
    }

    private static async Task Api26CanBeAvailable()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            Probe = new AndroidSystemAsrProbe(26, true, true),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            new AsrOptions(Language, true),
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Available,
            "A valid API-26 system recognizer must not be rejected by the API-31 on-device boundary.");
        Require(availability.Diagnostic.Contains("may use the network", StringComparison.OrdinalIgnoreCase),
            "Availability must disclose potential network use.");
    }

    private static async Task MissingServiceIsUnavailable()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            Probe = new AndroidSystemAsrProbe(26, true, false),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            new AsrOptions(Language, false),
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Unavailable,
            "Missing system service must be unavailable.");
    }

    private static async Task MicrophonePermissionIsRequired()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            Probe = new AndroidSystemAsrProbe(26, false, true),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            new AsrOptions(Language, false),
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.PermissionRequired,
            "Microphone permission must be required.");

        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "permission"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.Permission);
        Require(platform.RecognizeCalls == 0,
            "Recognition must not start while microphone permission is absent.");
    }

    private static async Task ConfiguredLanguageIsEnforced()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            new AsrOptions("fr-FR", false),
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Unavailable,
            "A language outside the configured provider must be unavailable.");
        Require(platform.ProbeCalls == 0,
            "Language mismatch must fail before touching the Android platform.");
    }

    private static async Task ProbeFailureIsVisible()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform
        {
            ProbeException = new InvalidOperationException("probe failed"),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            new AsrOptions(Language, false),
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Faulted,
            "Probe failure must become a faulted availability state.");
    }

    private static async Task PartialAndFinalResultsPreserveOrder()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        platform.SetEvents(
            Event("ordered", AndroidSystemAsrPlatformEventKind.Started),
            Event("ordered", AndroidSystemAsrPlatformEventKind.PartialResult, "hello"),
            Event("ordered", AndroidSystemAsrPlatformEventKind.FinalResult, "hello world"));
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "ordered", partialResults: true),
            CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 3, "Expected started, partial, and final events.");
        Require(events[0].Kind == AsrEventKind.Started, "First event must be started.");
        Require(events[1].Kind == AsrEventKind.PartialResult && events[1].Transcript == "hello",
            "Second event must preserve the partial transcript.");
        Require(events[2].Kind == AsrEventKind.FinalResult && events[2].Transcript == "hello world",
            "Third event must preserve the final transcript.");
        Require(events[0].Sequence == 1UL && events[1].Sequence == 2UL && events[2].Sequence == 3UL,
            "Provider event sequence must be monotonic.");
    }

    private static async Task NoMatchIsExplicit()
    {
        await using var platform = new FakeAndroidSystemAsrPlatform();
        platform.SetEvents(Event("nomatch", AndroidSystemAsrPlatformEventKind.NoMatch));
        await using var provider = CreateProvider(platform);
        List<AsrEvent> events = await CollectAsync(provider.RecognizeAsync(
            CreateRequest(provider, "nomatch"),
            CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == AsrEventKind.NoMatch,
            "No-match must not be fabricated as a transcript or failure.");
    }

    private static Task NetworkFailureIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidSystemAsrFailureKind.NetworkFailure,
            "network_failure",
            SpeechErrorCategory.Network,
            expectedRetryable: true);

    private static Task NetworkTimeoutIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidSystemAsrFailureKind.NetworkTimeout,
            "network_timeout",
            SpeechErrorCategory.Timeout,
            expectedRetryable: true);

    private static Task SpeechTimeoutIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidSystemAsrFailureKind.SpeechTimeout,
            "speech_timeout",
            SpeechErrorCategory.Timeout,
            expectedRetryable: true);

    private static Task ServiceDisconnectIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidSystemAsrFailureKind.ServiceDisconnected,
            "service_disconnected",
            SpeechErrorCategory.ServiceFailure,
            expectedRetryable: true);

    private static Task UnsupportedLanguageIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidSystemAsrFailureKind.LanguageNotSupported,
            "language_not_supported",
            SpeechErrorCategory.UnsupportedLanguage,
            expectedRetryable: false);

    private static Task UnavailableLanguageIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidSystemAsrFailureKind.LanguageUnavailable,
            "language_unavailable",
            SpeechErrorCategory.UnsupportedLanguage,
            expectedRetryable: false);

}

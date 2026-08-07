#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static partial class AndroidOfflineTtsTests
{
    private const string Language = "en-US";

    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new List<(string, Func<Task>)>
        {
            ("Descriptor is device-service offline TTS", DescriptorIsOffline),
            ("Installed exact-locale offline voice makes provider available", InstalledOfflineVoiceIsAvailable),
            ("Engine initialization failure requires setup", EngineUnavailableRequiresSetup),
            ("Missing language data requires setup guidance", MissingLanguageDataRequiresSetup),
            ("Network-only locale does not satisfy offline availability", NetworkOnlyVoiceIsRejected),
            ("Unsupported locale remains unavailable", UnsupportedLocaleIsUnavailable),
            ("Probe failure remains visible", ProbeFailureIsVisible),
            ("Voice enumeration filters network and other locales", VoiceEnumerationFiltersNetworkAndLocale),
            ("Voice enumeration preserves missing-data state", VoiceEnumerationPreservesInstallState),
            ("Default voice selection is deterministic", DefaultVoiceSelectionIsDeterministic),
            ("Preferred installed offline voice is selected exactly", PreferredVoiceIsSelectedExactly),
            ("Unavailable preferred voice does not silently substitute", UnavailablePreferenceDoesNotSubstitute),
            ("Duplicate preferred voice identity fails closed", DuplicatePreferenceFailsClosed),
            ("Start and done callbacks preserve order", StartAndDonePreserveOrder),
            ("Android stop maps to cancelled", StopMapsToCancelled),
            ("Missing voice data failure remains explicit", MissingVoiceDataFailureIsVisible),
            ("Service failure remains visible", ServiceFailureIsVisible),
            ("Synthesis failure remains visible", SynthesisFailureIsVisible),
            ("Android network failure violates offline contract", NetworkFailureIsContractViolation),
            ("Android network timeout violates offline contract", NetworkTimeoutIsContractViolation),
            ("Direct network voice request is rejected before synthesis", NetworkVoiceRequestIsRejected),
            ("Wrong-locale voice request is rejected before synthesis", WrongLocaleVoiceIsRejected),
            ("Uninstalled voice request is rejected before synthesis", UninstalledVoiceIsRejected),
            ("Duplicate requested voice identity fails closed", DuplicateVoiceIdentityFailsClosed),
            ("Platform input limit is enforced before synthesis", PlatformInputLimitIsEnforced),
            ("Concurrent operations fail busy without queueing", ConcurrentOperationIsBusy),
            ("Caller cancellation reaches Android offline TTS platform", CallerCancellationReachesPlatform),
            ("Operation timeout cancels Android offline TTS platform", OperationTimeoutReachesPlatform),
            ("Mismatched callback request identity fails closed", RequestIdentityMismatchFailsClosed),
            ("Provider selection cannot redirect to another TTS instance", ProviderSelectionCannotRedirect),
            ("Platform stream exception is visible", PlatformStreamExceptionIsVisible),
            ("Missing terminal callback is visible", MissingTerminalCallbackIsVisible),
            ("Failed utterance is not retried", FailedUtteranceIsNotRetried),
            ("Disposal cancels active speech and releases platform", DisposalCancelsAndReleasesPlatform),
            ("Disposed provider rejects availability checks", DisposedProviderRejectsAvailability),
            ("Java bridge enforces exact installed offline voices", Rma123SourceContracts.JavaBridgeEnforcesOfflineVoiceOnly),
            ("Unity bridge marshals callbacks without fallback", Rma123SourceContracts.UnityBridgeMarshalsWithoutFallback),
            ("Speech manifests declare TTS service visibility", Rma123SourceContracts.ManifestsDeclareTtsServiceVisibility),
        };

    private static async Task DescriptorIsOffline()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        await using var provider = CreateProvider(platform);
        Require(provider.Descriptor.Kind == SpeechProviderKind.TextToSpeech,
            "RMA-123 must be a TTS provider.");
        Require(provider.Descriptor.Location == SpeechProviderLocation.DeviceService,
            "Android TextToSpeech must remain represented as a device service.");
        Require(provider.Descriptor.NetworkRequirement == SpeechNetworkRequirement.None,
            "RMA-123 must declare no network requirement.");
        Require(!provider.Descriptor.MayUseNetwork,
            "RMA-123 descriptor must not advertise network use.");
        Require(provider.Capabilities.SupportsCancellation,
            "RMA-123 must support cancellation.");
    }

    private static async Task InstalledOfflineVoiceIsAvailable()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Available,
            "Installed exact-locale offline voice should make RMA-123 available.");
        Require(availability.Diagnostic.Contains("no network", StringComparison.OrdinalIgnoreCase),
            "Availability should make the offline voice constraint explicit.");
    }

    private static async Task EngineUnavailableRequiresSetup()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            Probe = Probe(
                engineInitialized: false,
                AndroidOfflineTtsLanguageStatus.Unknown,
                offline: 0,
                installed: 0,
                network: 0),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.SetupRequired,
            "Unavailable TextToSpeech engine must require setup.");
    }

    private static async Task MissingLanguageDataRequiresSetup()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            Probe = Probe(
                engineInitialized: true,
                AndroidOfflineTtsLanguageStatus.MissingData,
                offline: 0,
                installed: 0,
                network: 0),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.SetupRequired,
            "Missing TTS language data must require setup.");
        Require(availability.Diagnostic.Contains(
                "android.speech.tts.engine.INSTALL_TTS_DATA",
                StringComparison.Ordinal),
            "Missing voice data must surface deterministic Android installation guidance.");
    }

    private static async Task NetworkOnlyVoiceIsRejected()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            Probe = Probe(
                engineInitialized: true,
                AndroidOfflineTtsLanguageStatus.ExactAvailable,
                offline: 0,
                installed: 0,
                network: 2),
        };
        platform.SetVoices(FakeAndroidOfflineTtsPlatform.NetworkVoice(
            "network-only",
            "Network Only"));
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.SetupRequired,
            "Network-only voices must not satisfy RMA-123 offline availability.");
        Require(availability.Diagnostic.Contains("prohibited", StringComparison.OrdinalIgnoreCase),
            "Network-only availability must explicitly say those voices are prohibited.");
    }

    private static async Task UnsupportedLocaleIsUnavailable()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            Probe = Probe(
                engineInitialized: true,
                AndroidOfflineTtsLanguageStatus.NotSupported,
                offline: 0,
                installed: 0,
                network: 0),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Unavailable,
            "Unsupported TTS locale must remain unavailable.");
    }

    private static async Task ProbeFailureIsVisible()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            ProbeException = new InvalidOperationException("probe failed"),
        };
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability availability = await provider.CheckAvailabilityAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(availability.State == SpeechAvailabilityState.Faulted,
            "Probe exception must become a faulted availability state.");
    }

    private static async Task VoiceEnumerationFiltersNetworkAndLocale()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetVoices(
            FakeAndroidOfflineTtsPlatform.OfflineVoice("zeta", "Zulu", true),
            FakeAndroidOfflineTtsPlatform.NetworkVoice("network", "Network"),
            FakeAndroidOfflineTtsPlatform.OfflineVoice("alpha", "Alpha", true),
            FakeAndroidOfflineTtsPlatform.OfflineVoice("french", "French", true, "fr-FR"));
        await using var provider = CreateProvider(platform);
        IReadOnlyList<TtsVoice> voices = await provider.GetVoicesAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(voices.Count == 2, "Only exact-locale non-network voices may be exposed.");
        Require(voices[0].VoiceId == "alpha" && voices[1].VoiceId == "zeta",
            "Offline voices must be returned in deterministic display-name order.");
        Require(!voices[0].MayUseNetwork && !voices[1].MayUseNetwork,
            "Every RMA-123 exposed voice must declare no networking.");
    }

    private static async Task VoiceEnumerationPreservesInstallState()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetVoices(FakeAndroidOfflineTtsPlatform.OfflineVoice(
            "needs-data",
            "Needs Data",
            installed: false));
        await using var provider = CreateProvider(platform);
        IReadOnlyList<TtsVoice> voices = await provider.GetVoicesAsync(
            CancellationToken.None).ConfigureAwait(false);
        Require(voices.Count == 1 && !voices[0].IsInstalled,
            "Offline voice enumeration must preserve missing-data state for setup UI.");
    }

    private static Task DefaultVoiceSelectionIsDeterministic()
    {
        var voices = new List<TtsVoice>
        {
            Voice("z", "Zulu", Language, installed: true),
            Voice("a", "Alpha", Language, installed: true),
            Voice("network", "A Network", Language, installed: true,
                SpeechNetworkRequirement.Required),
            Voice("fr", "French", "fr-FR", installed: true),
        };
        TtsVoice? selected = AndroidOfflineTtsProvider.SelectVoice(
            voices,
            Language,
            preferredVoiceId: null);
        Require(selected?.VoiceId == "a",
            "Default selection must deterministically choose the first usable offline voice.");
        return Task.CompletedTask;
    }

    private static Task PreferredVoiceIsSelectedExactly()
    {
        var voices = new List<TtsVoice>
        {
            Voice("a", "Alpha", Language, installed: true),
            Voice("b", "Beta", Language, installed: true),
        };
        TtsVoice? selected = AndroidOfflineTtsProvider.SelectVoice(
            voices,
            Language,
            "b");
        Require(selected?.VoiceId == "b",
            "Explicit user preference must select that exact installed offline voice.");
        return Task.CompletedTask;
    }

    private static Task UnavailablePreferenceDoesNotSubstitute()
    {
        var voices = new List<TtsVoice>
        {
            Voice("a", "Alpha", Language, installed: true),
            Voice("network", "Network", Language, installed: true,
                SpeechNetworkRequirement.Required),
        };
        TtsVoice? missing = AndroidOfflineTtsProvider.SelectVoice(
            voices,
            Language,
            "missing");
        TtsVoice? network = AndroidOfflineTtsProvider.SelectVoice(
            voices,
            Language,
            "network");
        Require(missing == null && network == null,
            "Unavailable/network user preference must not silently substitute another voice.");
        return Task.CompletedTask;
    }

    private static Task DuplicatePreferenceFailsClosed()
    {
        var voices = new List<TtsVoice>
        {
            Voice("dup", "One", Language, installed: true),
            Voice("dup", "Two", Language, installed: true),
        };
        AssertThrows<InvalidOperationException>(() =>
            _ = AndroidOfflineTtsProvider.SelectVoice(voices, Language, "dup"));
        return Task.CompletedTask;
    }

    private static async Task StartAndDonePreserveOrder()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetEvents(
            Event("ordered", AndroidOfflineTtsPlatformEventKind.Started),
            Event("ordered", AndroidOfflineTtsPlatformEventKind.Completed));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "ordered"),
            CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 2, "Expected started and completed TTS events.");
        Require(events[0].Kind == TtsEventKind.Started && events[0].Sequence == 1UL,
            "First TTS event must be started with sequence 1.");
        Require(events[1].Kind == TtsEventKind.Completed && events[1].Sequence == 2UL,
            "Second TTS event must be completed with sequence 2.");
        Require(platform.SpeakCalls == 1 && platform.LastVoiceId == "offline-en",
            "The exact requested offline voice must be passed to the platform once.");
    }

    private static async Task StopMapsToCancelled()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetEvents(Event("stopped", AndroidOfflineTtsPlatformEventKind.Cancelled));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "stopped"),
            CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == TtsEventKind.Cancelled,
            "Android onStop must remain a cancelled TTS event.");
    }

    private static Task MissingVoiceDataFailureIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidOfflineTtsFailureKind.MissingVoiceData,
            "missing_voice_data",
            SpeechErrorCategory.MissingVoiceData,
            expectedRetryable: false);

    private static Task ServiceFailureIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidOfflineTtsFailureKind.ServiceFailure,
            "service_failure",
            SpeechErrorCategory.ServiceFailure,
            expectedRetryable: true);

    private static Task SynthesisFailureIsVisible() =>
        VerifyFailureMappingAsync(
            AndroidOfflineTtsFailureKind.SynthesisFailure,
            "synthesis_failure",
            SpeechErrorCategory.ServiceFailure,
            expectedRetryable: false);

    private static Task NetworkFailureIsContractViolation() =>
        VerifyFailureMappingAsync(
            AndroidOfflineTtsFailureKind.NetworkFailure,
            "network_failure",
            SpeechErrorCategory.ContractViolation,
            expectedRetryable: false);

    private static Task NetworkTimeoutIsContractViolation() =>
        VerifyFailureMappingAsync(
            AndroidOfflineTtsFailureKind.NetworkTimeout,
            "network_timeout",
            SpeechErrorCategory.ContractViolation,
            expectedRetryable: false);

    private static async Task NetworkVoiceRequestIsRejected()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetVoices(
            FakeAndroidOfflineTtsPlatform.OfflineVoice("offline-en", "Offline", true),
            FakeAndroidOfflineTtsPlatform.NetworkVoice("network-en", "Network"));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "network-voice", voiceId: "network-en"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
        Require(platform.SpeakCalls == 0,
            "Network-required voice must be rejected before platform synthesis.");
    }

    private static async Task WrongLocaleVoiceIsRejected()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetVoices(
            FakeAndroidOfflineTtsPlatform.OfflineVoice("offline-en", "Offline", true),
            FakeAndroidOfflineTtsPlatform.OfflineVoice("fr", "French", true, "fr-FR"));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "wrong-locale", voiceId: "fr"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.UnsupportedLanguage);
        Require(platform.SpeakCalls == 0,
            "Wrong-locale voice must be rejected before platform synthesis.");
    }

    private static async Task UninstalledVoiceIsRejected()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetVoices(
            FakeAndroidOfflineTtsPlatform.OfflineVoice("offline-en", "Offline", true),
            FakeAndroidOfflineTtsPlatform.OfflineVoice("needs-data", "Needs Data", false));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "needs-data", voiceId: "needs-data"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.MissingVoiceData);
        Require(events[0].Error?.Diagnostic.Contains(
                "INSTALL_TTS_DATA",
                StringComparison.Ordinal) == true,
            "Uninstalled voice failure must include setup guidance.");
        Require(platform.SpeakCalls == 0,
            "Uninstalled voice must be rejected before platform synthesis.");
    }

    private static async Task DuplicateVoiceIdentityFailsClosed()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetVoices(
            FakeAndroidOfflineTtsPlatform.OfflineVoice("offline-en", "One", true),
            FakeAndroidOfflineTtsPlatform.OfflineVoice("offline-en", "Two", true));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "duplicate"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
        Require(platform.SpeakCalls == 0,
            "Duplicate voice identity must fail before platform synthesis.");
    }

    private static async Task PlatformInputLimitIsEnforced()
    {
        await using var platform = new FakeAndroidOfflineTtsPlatform
        {
            Probe = new AndroidOfflineTtsProbe(
                26,
                true,
                AndroidOfflineTtsLanguageStatus.ExactAvailable,
                1,
                1,
                0,
                3,
                "Synthetic three-character limit."),
        };
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "too-long", text: "four"),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
        Require(platform.VoiceCalls == 0 && platform.SpeakCalls == 0,
            "Input length must fail before voice enumeration or synthesis.");
    }

    private static async Task VerifyFailureMappingAsync(
        AndroidOfflineTtsFailureKind kind,
        string code,
        SpeechErrorCategory category,
        bool expectedRetryable)
    {
        string requestId = "failure-" + code;
        await using var platform = new FakeAndroidOfflineTtsPlatform();
        platform.SetEvents(FailureEvent(requestId, kind, code));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, requestId),
            CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, category);
        Require(events[0].Error?.IsRetryable == expectedRetryable,
            "Failure retryability classification did not match the RMA-123 contract.");
    }

    private static AndroidOfflineTtsProbe Probe(
        bool engineInitialized,
        AndroidOfflineTtsLanguageStatus languageStatus,
        int offline,
        int installed,
        int network) =>
        new AndroidOfflineTtsProbe(
            26,
            engineInitialized,
            languageStatus,
            offline,
            installed,
            network,
            AndroidOfflineTtsProvider.DefaultMaximumInputCharacters,
            "Synthetic RMA-123 probe.");

    private static TtsVoice Voice(
        string voiceId,
        string displayName,
        string languageTag,
        bool installed,
        SpeechNetworkRequirement networkRequirement = SpeechNetworkRequirement.None) =>
        new TtsVoice(
            voiceId,
            displayName,
            languageTag,
            networkRequirement,
            installed);
}

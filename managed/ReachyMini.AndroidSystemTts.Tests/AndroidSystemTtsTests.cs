#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class AndroidSystemTtsTests
{
    private const string Language = "en-US";

    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new (string, Func<Task>)[]
        {
            ("descriptor-discloses-provider-controlled-networking", DescriptorDisclosesNetworking),
            ("voice-catalog-preserves-network-required-status", VoiceCatalogPreservesNetworkStatus),
            ("automatic-selection-prefers-installed-offline-voice", AutomaticSelectionPrefersOffline),
            ("automatic-selection-never-selects-network-only-voice", AutomaticSelectionNeverUsesNetworkOnly),
            ("preferred-network-voice-requires-explicit-selection", PreferredNetworkVoiceRequiresExplicitSelection),
            ("preferred-network-voice-allowed-after-explicit-selection", PreferredNetworkVoiceAllowedAfterExplicitSelection),
            ("network-only-availability-requires-explicit-selection", NetworkOnlyAvailabilityRequiresExplicitSelection),
            ("explicit-network-selection-makes-provider-available", ExplicitNetworkSelectionMakesAvailable),
            ("network-request-without-explicit-selection-fails-closed", NetworkRequestWithoutExplicitSelectionFails),
            ("explicit-network-request-passes-approval-bit", ExplicitNetworkRequestPassesApprovalBit),
            ("offline-request-never-passes-network-approval", OfflineRequestDoesNotApproveNetwork),
            ("uninstalled-offline-voice-fails-before-synthesis", UninstalledOfflineVoiceFails),
            ("wrong-locale-voice-fails-before-synthesis", WrongLocaleVoiceFails),
            ("duplicate-voice-id-fails-closed", DuplicateVoiceIdFails),
            ("start-and-done-callbacks-map-to-tts-events", StartAndDoneCallbacksMap),
            ("network-error-maps-to-network-category", NetworkErrorMapsToNetworkCategory),
            ("callback-request-identity-mismatch-fails-closed", CallbackIdentityMismatchFails),
            ("missing-terminal-callback-is-visible", MissingTerminalCallbackIsVisible),
            ("caller-cancellation-reaches-platform", CallerCancellationReachesPlatform),
            ("operation-timeout-reaches-platform", OperationTimeoutReachesPlatform),
            ("concurrent-operation-is-busy-not-queued", ConcurrentOperationIsBusy),
            ("provider-selection-cannot-redirect", ProviderSelectionCannotRedirect),
            ("failed-utterance-is-not-retried", FailedUtteranceIsNotRetried),
            ("disposal-cancels-and-releases-platform", DisposalCancelsAndReleasesPlatform),
            ("java-bridge-enforces-explicit-network-approval", JavaBridgeSourceContract),
            ("unity-bridge-marshals-network-approval-without-fallback", UnityBridgeSourceContract),
            ("rma123-offline-provider-remains-network-prohibiting", Rma123BoundaryRemainsStrict),
        };

    private static async Task DescriptorDisclosesNetworking()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform);
        Require(provider.Descriptor.ProviderId == AndroidSystemTtsProvider.ProviderId,
            "RMA-124 provider identity must be explicit.");
        Require(provider.Descriptor.Location == SpeechProviderLocation.DeviceService,
            "RMA-124 must remain a device-service provider.");
        Require(provider.Descriptor.NetworkRequirement == SpeechNetworkRequirement.ProviderControlled,
            "RMA-124 must disclose provider-controlled networking.");
        Require(provider.Descriptor.DisplayName.Contains("may use network", StringComparison.Ordinal),
            "RMA-124 display name must disclose possible network use.");
    }

    private static async Task VoiceCatalogPreservesNetworkStatus()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform);
        IReadOnlyList<TtsVoice> voices = await provider.GetVoicesAsync(CancellationToken.None)
            .ConfigureAwait(false);
        Require(voices.Count == 2, "Expected both exact-locale voices.");
        Require(voices[0].VoiceId == "offline-en" && !voices[0].MayUseNetwork,
            "Offline voice must be labeled with no network requirement.");
        Require(voices[1].VoiceId == "network-en" &&
                voices[1].NetworkRequirement == SpeechNetworkRequirement.Required,
            "Network voice must be labeled as network-required.");
    }

    private static Task AutomaticSelectionPrefersOffline()
    {
        TtsVoice? selected = AndroidSystemTtsProvider.SelectVoice(Catalog(), Language, null, "network-en");
        Require(selected?.VoiceId == "offline-en", "Automatic selection must prefer installed offline TTS.");
        return Task.CompletedTask;
    }

    private static Task AutomaticSelectionNeverUsesNetworkOnly()
    {
        TtsVoice? selected = AndroidSystemTtsProvider.SelectVoice(
            new[] { new TtsVoice("network-en", "Network English", Language, SpeechNetworkRequirement.Required, true) },
            Language,
            null,
            "network-en");
        Require(selected == null, "Network-required TTS must never be selected automatically.");
        return Task.CompletedTask;
    }

    private static Task PreferredNetworkVoiceRequiresExplicitSelection()
    {
        Require(AndroidSystemTtsProvider.SelectVoice(Catalog(), Language, "network-en", null) == null,
            "A network voice requires explicit selection for the provider instance.");
        return Task.CompletedTask;
    }

    private static Task PreferredNetworkVoiceAllowedAfterExplicitSelection()
    {
        TtsVoice? selected = AndroidSystemTtsProvider.SelectVoice(Catalog(), Language, "network-en", "network-en");
        Require(selected?.VoiceId == "network-en" && selected.MayUseNetwork,
            "The exact explicitly selected network voice should be selectable.");
        return Task.CompletedTask;
    }

    private static async Task NetworkOnlyAvailabilityRequiresExplicitSelection()
    {
        await using var platform = NetworkOnlyPlatform();
        await using var provider = CreateProvider(platform);
        SpeechProviderAvailability value = await provider.CheckAvailabilityAsync(CancellationToken.None).ConfigureAwait(false);
        Require(value.State == SpeechAvailabilityState.SetupRequired,
            "Network-only TTS must require explicit network-voice selection.");
    }

    private static async Task ExplicitNetworkSelectionMakesAvailable()
    {
        await using var platform = NetworkOnlyPlatform();
        await using var provider = CreateProvider(platform, "network-en");
        SpeechProviderAvailability value = await provider.CheckAvailabilityAsync(CancellationToken.None).ConfigureAwait(false);
        Require(value.State == SpeechAvailabilityState.Available,
            "Explicit network selection may make RMA-124 available.");
    }

    private static async Task NetworkRequestWithoutExplicitSelectionFails()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "network-not-approved", voiceId: "network-en"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
        Require(platform.SpeakCalls == 0, "Unapproved network voice must fail before Android synthesis starts.");
    }

    private static async Task ExplicitNetworkRequestPassesApprovalBit()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform, "network-en");
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "network-approved", voiceId: "network-en"), CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 2 && events[1].Kind == TtsEventKind.Completed,
            "Explicit network voice should synthesize through RMA-124.");
        Require(platform.LastNetworkVoiceApproved == true,
            "Managed provider must pass explicit network approval to Android.");
    }

    private static async Task OfflineRequestDoesNotApproveNetwork()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform, "network-en");
        _ = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "offline-selected"), CancellationToken.None)).ConfigureAwait(false);
        Require(platform.LastNetworkVoiceApproved == false, "Offline synthesis must not carry network approval.");
    }

    private static async Task UninstalledOfflineVoiceFails()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        platform.SetVoices(FakeAndroidSystemTtsPlatform.OfflineVoice("offline-en", "Offline English", false));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "offline-missing"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.MissingVoiceData);
        Require(platform.SpeakCalls == 0, "Missing offline data must not trigger network synthesis.");
    }

    private static async Task WrongLocaleVoiceFails()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        platform.SetVoices(FakeAndroidSystemTtsPlatform.OfflineVoice("offline-en", "Offline English", true, "fr-FR"));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "wrong-locale"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.UnsupportedLanguage);
        Require(platform.SpeakCalls == 0, "Wrong-locale voice must fail before synthesis.");
    }

    private static async Task DuplicateVoiceIdFails()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        platform.SetVoices(
            FakeAndroidSystemTtsPlatform.OfflineVoice("duplicate", "A", true),
            FakeAndroidSystemTtsPlatform.NetworkVoice("duplicate", "B"));
        await using var provider = CreateProvider(platform, "duplicate");
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "duplicate", voiceId: "duplicate"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
        Require(platform.SpeakCalls == 0, "Duplicate voice identity must fail before synthesis.");
    }

    private static async Task StartAndDoneCallbacksMap()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "callbacks"), CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 2 && events[0].Kind == TtsEventKind.Started && events[1].Kind == TtsEventKind.Completed,
            "Android start/done callbacks must map deterministically.");
    }

    private static async Task NetworkErrorMapsToNetworkCategory()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        platform.SetEvents(FailureEvent("network-failure", AndroidSystemTtsFailureKind.NetworkFailure, "network_failure"));
        await using var provider = CreateProvider(platform, "network-en");
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "network-failure", voiceId: "network-en"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.Network);
    }

    private static async Task CallbackIdentityMismatchFails()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        platform.SetEvents(new AndroidSystemTtsPlatformEvent("different-request", AndroidSystemTtsPlatformEventKind.Completed));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "expected-request"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ContractViolation);
    }

    private static async Task MissingTerminalCallbackIsVisible()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform { EndWithoutTerminal = true };
        platform.SetEvents(new AndroidSystemTtsPlatformEvent("unterminated", AndroidSystemTtsPlatformEventKind.Started));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "unterminated"), CancellationToken.None)).ConfigureAwait(false);
        Require(events.Count == 2 && events[1].Kind == TtsEventKind.Failed &&
                events[1].Error?.Category == SpeechErrorCategory.ServiceFailure,
            "Missing terminal callback must become a visible service failure.");
    }

    private static async Task CallerCancellationReachesPlatform()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform { BlockSpeechUntilCancellation = true };
        await using var provider = CreateProvider(platform);
        using var cancellation = new CancellationTokenSource();
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "caller-cancel", timeout: TimeSpan.FromSeconds(5)), cancellation.Token));
        await platform.SpeechStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        cancellation.Cancel();
        List<TtsEvent> events = await active.ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == TtsEventKind.Cancelled && platform.CancellationObservations == 1,
            "Caller cancellation must reach the platform and surface explicitly.");
    }

    private static async Task OperationTimeoutReachesPlatform()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform { BlockSpeechUntilCancellation = true };
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "timeout", timeout: TimeSpan.FromMilliseconds(40)), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.Timeout);
        Require(platform.CancellationObservations == 1, "RMA-124 timeout must cancel the platform token.");
    }

    private static async Task ConcurrentOperationIsBusy()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform { BlockSpeechUntilCancellation = true };
        await using var provider = CreateProvider(platform);
        using var cancellation = new CancellationTokenSource();
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "busy-active", timeout: TimeSpan.FromSeconds(5)), cancellation.Token));
        await platform.SpeechStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        List<TtsEvent> competing = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "busy-second"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(competing, SpeechErrorCategory.Busy);
        Require(platform.SpeakCalls == 1, "RMA-124 must not queue a second utterance.");
        cancellation.Cancel();
        _ = await active.ConfigureAwait(false);
    }

    private static async Task ProviderSelectionCannotRedirect()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        await using var provider = CreateProvider(platform);
        var other = new SpeechProviderDescriptor(
            SpeechProviderKind.TextToSpeech, "other-tts", "other-instance", "Other TTS", "1",
            SpeechProviderLocation.DeviceService, SpeechNetworkRequirement.ProviderControlled);
        var request = new TtsRequest(
            new SpeechOperationContext("redirect", new SpeechProviderSelection(other).Current, TimeSpan.FromSeconds(1)),
            "hello", "offline-en");
        await AssertThrowsAsync<InvalidOperationException>(
            () => CollectAsync(provider.SpeakAsync(request, CancellationToken.None))).ConfigureAwait(false);
        Require(platform.ProbeCalls == 0 && platform.VoiceCalls == 0 && platform.SpeakCalls == 0,
            "Provider identity mismatch must fail before Android TTS.");
    }

    private static async Task FailedUtteranceIsNotRetried()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform();
        platform.SetEvents(FailureEvent("no-retry", AndroidSystemTtsFailureKind.ServiceFailure, "service_failure"));
        await using var provider = CreateProvider(platform);
        List<TtsEvent> events = await CollectAsync(provider.SpeakAsync(CreateRequest(provider, "no-retry"), CancellationToken.None)).ConfigureAwait(false);
        RequireSingleFailure(events, SpeechErrorCategory.ServiceFailure);
        Require(platform.SpeakCalls == 1, "RMA-124 must not retry failed utterances automatically.");
    }

    private static async Task DisposalCancelsAndReleasesPlatform()
    {
        await using var platform = new FakeAndroidSystemTtsPlatform { BlockSpeechUntilCancellation = true };
        await using var provider = CreateProvider(platform);
        Task<List<TtsEvent>> active = CollectAsync(provider.SpeakAsync(
            CreateRequest(provider, "dispose-active", timeout: TimeSpan.FromSeconds(5)), CancellationToken.None));
        await platform.SpeechStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
        List<TtsEvent> events = await active.ConfigureAwait(false);
        Require(events.Count == 1 && events[0].Kind == TtsEventKind.Cancelled,
            "Provider disposal must cancel active synthesis.");
        Require(platform.CancellationObservations == 1 && platform.DisposeCalls == 1,
            "Provider disposal must cancel and release its Android platform exactly once.");
    }

    private static Task JavaBridgeSourceContract()
    {
        string source = Read("Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachySystemTtsBridge.java");
        RequireSource(source, "voice.isNetworkConnectionRequired()", "per-voice network classification");
        RequireSource(source, "boolean networkVoiceApproved", "explicit network approval input");
        RequireSource(source, "networkRequired && !networkVoiceApproved", "network approval enforcement");
        RequireSource(source, "tts.setVoice(voice)", "exact Android voice selection");
        RequireSource(source, "Voice selected = tts.getVoice()", "post-selection voice verification");
        RequireSource(source, "TextToSpeech.QUEUE_ADD", "non-replacing queue mode");
        RequireSource(source, "pendingInitialization.removeIf", "pre-init cancellation");
        RequireSource(source, "tts.shutdown();", "engine teardown");
        RejectSource(source, "setLanguage(", "closest-match language selection");
        RejectSource(source, "QUEUE_FLUSH", "implicit speech replacement");
        RejectSource(source, "OpenAI", "cloud fallback");
        RejectSource(source, "http://", "hard-coded network transport");
        RejectSource(source, "https://", "hard-coded network transport");
        return Task.CompletedTask;
    }

    private static Task UnityBridgeSourceContract()
    {
        string source = Read("Assets/ReachyMini/Runtime/Application/ReachyAndroidSystemTtsPlatform.cs");
        RequireSource(source, "ReachySystemTtsBridge", "dedicated Java bridge");
        RequireSource(source, "networkVoiceApproved", "network approval marshalling");
        RequireSource(source, "callback_queue_overflow", "visible callback queue overflow");
        RequireSource(source, "activeBridge.Call(\"cancel\", requestId)", "explicit cancellation");
        RequireSource(source, "value.Call(\"close\")", "explicit teardown");
        RejectSource(source, "OpenAI", "cloud fallback");
        RejectSource(source, "HttpClient", "independent network transport");
        return Task.CompletedTask;
    }

    private static Task Rma123BoundaryRemainsStrict()
    {
        string source = Read("Assets/Plugins/Android/ReachyOnDeviceAsr.androidlib/src/main/java/com/ekkus93/weachy/speech/ReachyOfflineTtsBridge.java");
        RequireSource(source, "voice.isNetworkConnectionRequired()", "RMA-123 network check");
        RequireSource(source, "prohibited by RMA-123", "RMA-123 network rejection");
        return Task.CompletedTask;
    }

    private static FakeAndroidSystemTtsPlatform NetworkOnlyPlatform() =>
        new FakeAndroidSystemTtsPlatform
        {
            Probe = new AndroidSystemTtsProbe(
                26, true, 1, 0, 1,
                AndroidSystemTtsProvider.DefaultMaximumInputCharacters,
                "Network only."),
        };

    private static AndroidSystemTtsProvider CreateProvider(
        FakeAndroidSystemTtsPlatform platform,
        string? explicitlySelectedNetworkVoiceId = null) =>
        new AndroidSystemTtsProvider(platform, "android-system-default", Language, explicitlySelectedNetworkVoiceId);

    private static TtsRequest CreateRequest(
        AndroidSystemTtsProvider provider,
        string requestId,
        string text = "hello from RMA-124",
        string voiceId = "offline-en",
        TimeSpan? timeout = null)
    {
        var selection = new SpeechProviderSelection(provider.Descriptor);
        var context = new SpeechOperationContext(requestId, selection.Current, timeout ?? TimeSpan.FromSeconds(2));
        return new TtsRequest(context, text, voiceId);
    }

    private static TtsVoice[] Catalog() =>
        new[]
        {
            new TtsVoice("offline-en", "Offline English", Language, SpeechNetworkRequirement.None, true),
            new TtsVoice("network-en", "Network English", Language, SpeechNetworkRequirement.Required, true),
        };

    private static AndroidSystemTtsPlatformEvent FailureEvent(
        string requestId,
        AndroidSystemTtsFailureKind kind,
        string code) =>
        new AndroidSystemTtsPlatformEvent(
            requestId,
            AndroidSystemTtsPlatformEventKind.Failed,
            new AndroidSystemTtsPlatformFailure(kind, code, "Synthetic RMA-124 failure."));

    private static async Task<List<TtsEvent>> CollectAsync(IAsyncEnumerable<TtsEvent> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<TtsEvent>();
        await foreach (TtsEvent value in source.ConfigureAwait(false))
        {
            result.Add(value);
        }
        return result;
    }

    private static void RequireSingleFailure(List<TtsEvent> events, SpeechErrorCategory category)
    {
        ArgumentNullException.ThrowIfNull(events);
        Require(events.Count == 1 && events[0].Kind == TtsEventKind.Failed,
            "Expected exactly one terminal TTS failure event.");
        Require(events[0].Error?.Category == category,
            "TTS failure category did not match the expected contract.");
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
        throw new InvalidOperationException("Expected exception " + typeof(TException).Name + ".");
    }

    private static string Read(string relativePath)
    {
        string root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectSettings", "ProjectVersion.txt")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root for RMA-124 source contracts.");
    }

    private static void RequireSource(string source, string expected, string description)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RMA-124 source contract is missing " + description + ".");
        }
    }

    private static void RejectSource(string source, string rejected, string description)
    {
        if (source.Contains(rejected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RMA-124 source contract found prohibited " + description + ".");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

internal static class AndroidOnDeviceAsrTests
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
        new (string, Func<Task>)[]
        {
            ("Descriptor is explicit on-device with no network", DescriptorIsExplicitOnDevice),
            ("API 30 reports explicit provider unavailable", Api30Unavailable),
            ("API 31 missing explicit recognizer reports unavailable", Api31RecognizerUnavailable),
            ("Microphone permission is required before support or start", PermissionRequired),
            ("API 31 skips unavailable language preflight without faking support", Api31PreflightUnavailable),
            ("API 33 installed language is available", InstalledLanguageAvailable),
            ("Missing language model requires setup", ModelDownloadRequired),
            ("Pending language model requires setup", ModelDownloadPending),
            ("Unsupported language is unavailable", UnsupportedLanguage),
            ("Recognizer support preflight inability stays visible", PreflightUnavailableStillAvailable),
            ("Support discovery fault is faulted", SupportFaulted),
            ("Configured language is enforced", ConfiguredLanguageEnforced),
            ("Partial and final results preserve order", PartialAndFinalResults),
            ("No-match remains explicit", NoMatch),
            ("Concurrent utterances fail busy without queueing", BusyWithoutQueue),
            ("Speech timeout maps to typed timeout", PlatformSpeechTimeout),
            ("Operation timeout cancels the Android platform", OperationTimeoutCancels),
            ("Caller cancellation reaches the Android platform", CallerCancellationReachesPlatform),
            ("Service disconnect is visible", ServiceDisconnected),
            ("Language model absence is visible", LanguageModelUnavailable),
            ("Runtime unsupported language is visible", RuntimeUnsupportedLanguage),
            ("Unexpected network failure is a locality contract violation", NetworkFailureIsContractViolation),
            ("Mismatched callback request identity fails closed", CallbackIdentityMismatch),
            ("Missing terminal callback fails visibly", StreamEndsWithoutTerminal),
            ("Platform stream exception fails visibly", PlatformExceptionVisible),
            ("Provider selection cannot redirect to another instance", ProviderRedirectRejected),
            ("Provider does not retry a failed utterance", NoAutomaticRetry),
            ("Disposal cancels an active utterance and destroys platform", DisposeCancelsActive),
            ("Disposed provider rejects new availability checks", DisposedProviderRejectsUse),
            ("Maximum utterance duration caps a longer operation timeout", MaximumUtteranceCapsTimeout),
            ("Java bridge uses only explicit on-device recognizer", Rma121SourceContracts.JavaBridgeUsesOnlyExplicitOnDeviceRecognizer),
            ("ASR Android manifest declares audio and service visibility", Rma121SourceContracts.ManifestDeclaresAudioAndRecognitionServiceVisibility),
            ("Unity bridge marshals callbacks without fallback", Rma121SourceContracts.UnityBridgeMarshalsCallbacksWithoutFallback),
        };

    private static async Task DescriptorIsExplicitOnDevice()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);

        AssertEqual(
            SpeechProviderLocation.OnDevice,
            provider.Descriptor.Location,
            "provider location");
        AssertEqual(
            SpeechNetworkRequirement.None,
            provider.Descriptor.NetworkRequirement,
            "network requirement");
        Assert(
            !provider.Descriptor.MayUseNetwork,
            "explicit on-device provider must not advertise network use");
        AssertEqual(
            AndroidOnDeviceAsrProvider.ProviderId,
            provider.Descriptor.ProviderId,
            "provider id");
    }

    private static async Task Api30Unavailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(30, true, false, false),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "API30 availability");
        AssertEqual(0, platform.SupportCheckCount, "support checks");
        AssertEqual(0, platform.RecognitionStartCount, "recognizer starts");
    }

    private static async Task Api31RecognizerUnavailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(31, true, false, false),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "API31 unavailable recognizer");
        AssertEqual(0, platform.SupportCheckCount, "support checks");
    }

    private static async Task PermissionRequired()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(33, false, true, true),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.PermissionRequired,
            availability.State,
            "permission state");
        AssertEqual(0, platform.SupportCheckCount, "support checks before permission");

        IReadOnlyList<AsrEvent> events =
            await CollectAsync(
                provider.RecognizeAsync(
                    Request(provider),
                    CancellationToken.None)).ConfigureAwait(false);
        AssertSingleFailure(
            events,
            SpeechErrorCategory.Permission,
            "android_on_device_asr_microphone_permission_required");
        AssertEqual(0, platform.RecognitionStartCount, "recognizer starts before permission");
    }

    private static async Task Api31PreflightUnavailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Probe = new AndroidOnDeviceAsrProbe(31, true, true, false),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(
            SpeechAvailabilityState.Available,
            availability.State,
            "API31 explicit recognizer");
        Assert(
            availability.Diagnostic.Contains(
                "cannot preflight",
                StringComparison.OrdinalIgnoreCase),
            "API31 availability must disclose the missing language preflight");
        AssertEqual(0, platform.SupportCheckCount, "support preflight calls");
    }

    private static async Task InstalledLanguageAvailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);

        AssertEqual(SpeechAvailabilityState.Available, availability.State, "availability");
        AssertEqual(1, platform.SupportCheckCount, "support checks");
    }

    private static async Task ModelDownloadRequired()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.ModelDownloadRequired,
                "model must be installed"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.SetupRequired,
            availability.State,
            "setup state");
    }

    private static async Task ModelDownloadPending()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.ModelDownloadPending,
                "model download pending"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.SetupRequired,
            availability.State,
            "pending model state");
    }

    private static async Task UnsupportedLanguage()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.UnsupportedLanguage,
                "unsupported language"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "unsupported language state");
    }

    private static async Task PreflightUnavailableStillAvailable()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.PreflightUnavailable,
                "cannot check support"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Available,
            availability.State,
            "preflight unavailable state");
        Assert(
            availability.Diagnostic.Contains(
                "no fallback",
                StringComparison.OrdinalIgnoreCase),
            "availability must retain the no-fallback boundary");
    }

    private static async Task SupportFaulted()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform
        {
            Support = Support(
                AndroidOnDeviceAsrSupportState.Faulted,
                "support service faulted"),
        };
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                Options(),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Faulted,
            availability.State,
            "support fault");
    }

    private static async Task ConfiguredLanguageEnforced()
    {
        await using var platform = new FakeAndroidOnDeviceAsrPlatform();
        await using var provider = CreateProvider(platform);

        SpeechProviderAvailability availability =
            await provider.CheckAvailabilityAsync(
                new AsrOptions("fr-FR", true),
                CancellationToken.None).ConfigureAwait(false);
        AssertEqual(
            SpeechAvailabilityState.Unavailable,
            availability.State,
            "configured language");
        AssertEqual(0, platform.ProbeCount, "probe count for mismatched language");
        AssertEqual(0, platform.RecognitionStartCount, "start count");
    }

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

    private static AndroidOnDeviceAsrProvider CreateProvider(
        FakeAndroidOnDeviceAsrPlatform platform,
        TimeSpan? maximumUtteranceDuration = null)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return new AndroidOnDeviceAsrProvider(
            platform,
            "android-on-device-test-instance",
            "en-US",
            maximumUtteranceDuration ?? TimeSpan.FromSeconds(30));
    }

    private static AsrOptions Options()
    {
        return new AsrOptions("en-US", true);
    }

    private static AsrRequest Request(
        AndroidOnDeviceAsrProvider provider,
        string requestId = "request-1",
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var selection = new SpeechProviderSelection(provider.Descriptor);
        return new AsrRequest(
            new SpeechOperationContext(
                requestId,
                selection.Current,
                timeout ?? TimeSpan.FromSeconds(1)),
            Options());
    }

    private static AndroidOnDeviceAsrSupportResult Support(
        AndroidOnDeviceAsrSupportState state,
        string diagnostic)
    {
        return new AndroidOnDeviceAsrSupportResult(state, diagnostic);
    }

    private static AndroidOnDeviceAsrPlatformEvent Event(
        string requestId,
        AndroidOnDeviceAsrPlatformEventKind kind,
        string? transcript = null)
    {
        return new AndroidOnDeviceAsrPlatformEvent(
            requestId,
            kind,
            transcript);
    }

    private static AndroidOnDeviceAsrPlatformEvent FailedPlatform(
        string requestId,
        AndroidOnDeviceAsrFailureKind kind,
        string code,
        string diagnostic)
    {
        return new AndroidOnDeviceAsrPlatformEvent(
            requestId,
            AndroidOnDeviceAsrPlatformEventKind.Failed,
            failure: new AndroidOnDeviceAsrPlatformFailure(
                kind,
                code,
                diagnostic));
    }

    private static async Task<IReadOnlyList<AsrEvent>> CollectAsync(
        IAsyncEnumerable<AsrEvent> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var events = new List<AsrEvent>();
        await foreach (AsrEvent value in stream.ConfigureAwait(false))
        {
            events.Add(value);
        }
        return events;
    }

    private static void AssertSingleFailure(
        IReadOnlyList<AsrEvent> events,
        SpeechErrorCategory category,
        string code)
    {
        AssertEqual(1, events.Count, "failure event count");
        AssertFailure(events[0], category, code);
    }

    private static void AssertFailure(
        AsrEvent value,
        SpeechErrorCategory category,
        string code)
    {
        AssertEqual(AsrEventKind.Failed, value.Kind, "failure kind");
        SpeechProviderError error = value.Error ??
            throw new InvalidOperationException("Expected a structured provider error.");
        AssertEqual(category, error.Category, "error category");
        AssertEqual(code, error.Code, "error code");
    }

    private static async Task ExpectThrowsAsync<TException>(
        Func<Task> action)
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                message + ": expected=" + expected + ", actual=" + actual + ".");
        }
    }
}

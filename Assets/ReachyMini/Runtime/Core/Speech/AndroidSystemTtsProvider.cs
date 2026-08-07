#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed class AndroidSystemTtsProvider : ITtsProvider
    {
        public const string ProviderId = "android-system-tts";
        public const string ProviderVersion = "rma-124-v1";
        public const int DefaultMaximumInputCharacters = 4000;

        private readonly IAndroidSystemTtsPlatform platform;
        private readonly string configuredLanguageTag;
        private readonly string? explicitlySelectedNetworkVoiceId;
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private int operationInFlight;
        private int disposed;

        public AndroidSystemTtsProvider(
            IAndroidSystemTtsPlatform platform,
            string instanceId,
            string configuredLanguageTag,
            string? explicitlySelectedNetworkVoiceId = null)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.configuredLanguageTag = SpeechProviderDescriptor.RequireText(
                configuredLanguageTag,
                nameof(configuredLanguageTag));
            this.explicitlySelectedNetworkVoiceId = string.IsNullOrWhiteSpace(
                explicitlySelectedNetworkVoiceId)
                ? null
                : SpeechProviderDescriptor.RequireText(
                    explicitlySelectedNetworkVoiceId,
                    nameof(explicitlySelectedNetworkVoiceId));

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.TextToSpeech,
                ProviderId,
                instanceId,
                "Android system/network TTS (may use network)",
                ProviderVersion,
                SpeechProviderLocation.DeviceService,
                SpeechNetworkRequirement.ProviderControlled);
            Capabilities = new TtsCapabilities(
                supportsCancellation: true,
                DefaultMaximumInputCharacters);
        }

        public SpeechProviderDescriptor Descriptor { get; }
        public TtsCapabilities Capabilities { get; }
        public string ConfiguredLanguageTag => configuredLanguageTag;
        public string? ExplicitlySelectedNetworkVoiceId => explicitlySelectedNetworkVoiceId;

        public async ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!TryAcquireOperation())
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.Busy,
                    "Android system/network TTS is busy with another provider operation; requests are not queued.");
            }

            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            try
            {
                AndroidSystemTtsProbe probe = await platform.ProbeAsync(
                        configuredLanguageTag,
                        operationCancellation.Token)
                    .ConfigureAwait(false);
                return AvailabilityFromProbe(probe);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.Faulted,
                    Bound(
                        "Android system/network TTS availability probing failed with " +
                        exception.GetType().Name + ".",
                        SpeechProviderError.MaximumDiagnosticCharacters));
            }
            finally
            {
                ReleaseOperation();
            }
        }

        public async ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!TryAcquireOperation())
            {
                throw new InvalidOperationException(
                    "Android system/network TTS is busy; voice enumeration is not queued.");
            }

            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            try
            {
                IReadOnlyList<AndroidSystemTtsPlatformVoice> voices =
                    await platform.GetVoicesAsync(
                        configuredLanguageTag,
                        operationCancellation.Token).ConfigureAwait(false);
                return BuildVoiceList(voices);
            }
            finally
            {
                ReleaseOperation();
            }
        }

        public async IAsyncEnumerable<TtsEvent> SpeakAsync(
            TtsRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ThrowIfDisposed();
            SpeechProviderContract.ValidateProviderForOperation(Descriptor, request.Context);

            if (!TryAcquireOperation())
            {
                yield return Failed(
                    request.Context,
                    1UL,
                    SpeechErrorCategory.Busy,
                    "android_system_tts_busy",
                    "Android system/network TTS already has an active operation; the request was not queued.",
                    isRetryable: true);
                yield break;
            }

            using var timeoutCancellation = new CancellationTokenSource(request.Context.Timeout);
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token,
                timeoutCancellation.Token);

            try
            {
                ProbeEvaluation probeEvaluation =
                    await ProbeSafelyAsync(operationCancellation.Token).ConfigureAwait(false);
                if (probeEvaluation.Cancelled)
                {
                    yield return CancellationOrTimeout(
                        request.Context,
                        1UL,
                        timeoutCancellation,
                        cancellationToken);
                    yield break;
                }
                if (probeEvaluation.Exception != null)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ServiceFailure,
                        "android_system_tts_probe_failed",
                        "Android system/network TTS capability probing failed with " +
                            probeEvaluation.Exception.GetType().Name + ".",
                        isRetryable: true);
                    yield break;
                }

                AndroidSystemTtsProbe probe = probeEvaluation.Value ??
                    throw new InvalidOperationException(
                        "Android system/network TTS probing completed without a result.");
                SpeechProviderAvailability availability = AvailabilityFromProbe(probe);
                if (!probe.EngineInitialized || probe.MatchingVoiceCount <= 0)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ProviderUnavailable,
                        "android_system_tts_unavailable",
                        availability.Diagnostic,
                        isRetryable: false);
                    yield break;
                }

                int effectiveMaximumCharacters = Math.Min(
                    Capabilities.MaximumInputCharacters,
                    probe.MaximumInputCharacters);
                if (request.Text.Length > effectiveMaximumCharacters)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ContractViolation,
                        "android_system_tts_input_too_long",
                        "TTS input exceeds the Android system synthesis limit for this provider.",
                        isRetryable: false);
                    yield break;
                }

                VoiceCatalogEvaluation catalogEvaluation =
                    await LoadVoiceCatalogSafelyAsync(operationCancellation.Token)
                        .ConfigureAwait(false);
                if (catalogEvaluation.Cancelled)
                {
                    yield return CancellationOrTimeout(
                        request.Context,
                        1UL,
                        timeoutCancellation,
                        cancellationToken);
                    yield break;
                }
                if (catalogEvaluation.Exception != null)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ServiceFailure,
                        "android_system_tts_voice_catalog_failed",
                        "Android system/network TTS voice enumeration failed with " +
                            catalogEvaluation.Exception.GetType().Name + ".",
                        isRetryable: true);
                    yield break;
                }

                IReadOnlyList<AndroidSystemTtsPlatformVoice> platformVoices =
                    catalogEvaluation.Value ??
                    throw new InvalidOperationException(
                        "Android system/network TTS voice enumeration completed without a result.");
                VoiceValidation validation = ValidateRequestedVoice(
                    platformVoices,
                    request.VoiceId);
                if (!validation.Allowed)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        validation.Category,
                        validation.Code,
                        validation.Diagnostic,
                        validation.IsRetryable);
                    yield break;
                }

                ulong sequence = 0UL;
                bool terminal = false;
                IAsyncEnumerator<AndroidSystemTtsPlatformEvent> enumerator =
                    platform.SpeakAsync(
                            request.Context.RequestId,
                            request.Text,
                            configuredLanguageTag,
                            request.VoiceId,
                            validation.NetworkVoiceApproved,
                            operationCancellation.Token)
                        .GetAsyncEnumerator(operationCancellation.Token);
                await using (enumerator.ConfigureAwait(false))
                {
                    while (!terminal)
                    {
                        PlatformMoveNextResult moveNext =
                            await MoveNextSafelyAsync(enumerator).ConfigureAwait(false);
                        if (moveNext.Cancelled)
                        {
                            yield return CancellationOrTimeout(
                                request.Context,
                                checked(sequence + 1UL),
                                timeoutCancellation,
                                cancellationToken);
                            yield break;
                        }
                        if (moveNext.Exception != null)
                        {
                            yield return Failed(
                                request.Context,
                                checked(sequence + 1UL),
                                SpeechErrorCategory.ServiceFailure,
                                "android_system_tts_stream_failed",
                                "Android system/network TTS event streaming failed with " +
                                    moveNext.Exception.GetType().Name + ".",
                                isRetryable: true);
                            yield break;
                        }
                        if (!moveNext.HasValue)
                        {
                            yield return Failed(
                                request.Context,
                                checked(sequence + 1UL),
                                SpeechErrorCategory.ServiceFailure,
                                "android_system_tts_missing_terminal_event",
                                "Android system/network TTS ended without a terminal callback.",
                                isRetryable: true);
                            yield break;
                        }

                        AndroidSystemTtsPlatformEvent value = moveNext.Value ??
                            throw new InvalidOperationException(
                                "Android system/network TTS produced an empty platform event.");
                        sequence = checked(sequence + 1UL);
                        if (!string.Equals(
                            value.RequestId,
                            request.Context.RequestId,
                            StringComparison.Ordinal))
                        {
                            yield return Failed(
                                request.Context,
                                sequence,
                                SpeechErrorCategory.ContractViolation,
                                "android_system_tts_callback_request_identity_mismatch",
                                "Android system/network TTS callback returned a different request identifier.",
                                isRetryable: false);
                            yield break;
                        }

                        terminal = value.IsTerminal;
                        yield return MapPlatformEvent(request.Context, sequence, value);
                    }
                }
            }
            finally
            {
                ReleaseOperation();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                GC.SuppressFinalize(this);
                return;
            }

            lifetimeCancellation.Cancel();
            try
            {
                await platform.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                lifetimeCancellation.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        public static TtsVoice? SelectVoice(
            IReadOnlyList<TtsVoice> voices,
            string languageTag,
            string? preferredVoiceId,
            string? explicitlySelectedNetworkVoiceId)
        {
            if (voices == null)
            {
                throw new ArgumentNullException(nameof(voices));
            }

            string requiredLanguage = SpeechProviderDescriptor.RequireText(
                languageTag,
                nameof(languageTag));
            if (!string.IsNullOrWhiteSpace(preferredVoiceId))
            {
                TtsVoice? match = null;
                foreach (TtsVoice voice in voices)
                {
                    if (!string.Equals(
                            voice.LanguageTag,
                            requiredLanguage,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            voice.VoiceId,
                            preferredVoiceId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            "Android system TTS voice selection is ambiguous because the preferred voice identifier is duplicated.");
                    }
                    match = voice;
                }

                if (match == null)
                {
                    return null;
                }
                if (match.NetworkRequirement == SpeechNetworkRequirement.Required)
                {
                    return string.Equals(
                        match.VoiceId,
                        explicitlySelectedNetworkVoiceId,
                        StringComparison.Ordinal)
                        ? match
                        : null;
                }
                return match.IsInstalled ? match : null;
            }

            TtsVoice? selected = null;
            foreach (TtsVoice voice in voices)
            {
                if (!voice.IsInstalled ||
                    voice.NetworkRequirement != SpeechNetworkRequirement.None ||
                    !string.Equals(
                        voice.LanguageTag,
                        requiredLanguage,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (selected == null || CompareVoice(voice, selected) < 0)
                {
                    selected = voice;
                }
            }
            return selected;
        }

        private SpeechProviderAvailability AvailabilityFromProbe(AndroidSystemTtsProbe probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }
            if (!probe.EngineInitialized)
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.SetupRequired,
                    "Android TextToSpeech is not initialized. Configure or install a system TTS engine; no alternate TTS provider will be selected automatically.");
            }
            if (probe.MatchingVoiceCount <= 0)
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.SetupRequired,
                    "No exact-locale Android TTS voice is available. Configure voice data explicitly; no alternate provider will be selected automatically.");
            }
            if (probe.MatchingOfflineVoiceCount <= 0 &&
                probe.MatchingNetworkVoiceCount > 0 &&
                explicitlySelectedNetworkVoiceId == null)
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.SetupRequired,
                    "Only network-required Android TTS voices are available. Select one explicitly before synthesis; network TTS is never selected automatically.");
            }

            return new SpeechProviderAvailability(
                SpeechAvailabilityState.Available,
                probe.MatchingNetworkVoiceCount > 0
                    ? "Android system TTS is available. The voice catalog includes network-required voices; each is labeled and requires explicit selection before use."
                    : "Android system TTS is available with exact-locale offline voices.");
        }

        private async ValueTask<ProbeEvaluation> ProbeSafelyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                AndroidSystemTtsProbe probe = await platform.ProbeAsync(
                        configuredLanguageTag,
                        cancellationToken)
                    .ConfigureAwait(false);
                return ProbeEvaluation.FromValue(probe);
            }
            catch (OperationCanceledException)
            {
                return ProbeEvaluation.WasCancelled();
            }
            catch (Exception exception)
            {
                return ProbeEvaluation.Failed(exception);
            }
        }

        private async ValueTask<VoiceCatalogEvaluation> LoadVoiceCatalogSafelyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<AndroidSystemTtsPlatformVoice> voices =
                    await platform.GetVoicesAsync(
                        configuredLanguageTag,
                        cancellationToken).ConfigureAwait(false);
                return VoiceCatalogEvaluation.FromValue(voices);
            }
            catch (OperationCanceledException)
            {
                return VoiceCatalogEvaluation.WasCancelled();
            }
            catch (Exception exception)
            {
                return VoiceCatalogEvaluation.Failed(exception);
            }
        }

        private ReadOnlyCollection<TtsVoice> BuildVoiceList(
            IReadOnlyList<AndroidSystemTtsPlatformVoice> platformVoices)
        {
            if (platformVoices == null)
            {
                throw new ArgumentNullException(nameof(platformVoices));
            }

            var result = new List<TtsVoice>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (AndroidSystemTtsPlatformVoice voice in platformVoices)
            {
                if (!string.Equals(
                    voice.LanguageTag,
                    configuredLanguageTag,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!identifiers.Add(voice.VoiceId))
                {
                    throw new InvalidOperationException(
                        "Android system TTS returned duplicate voice identifiers.");
                }

                result.Add(new TtsVoice(
                    voice.VoiceId,
                    voice.DisplayName,
                    voice.LanguageTag,
                    voice.NetworkRequirement,
                    voice.IsInstalled));
            }

            result.Sort(CompareVoice);
            return result.AsReadOnly();
        }

        private VoiceValidation ValidateRequestedVoice(
            IReadOnlyList<AndroidSystemTtsPlatformVoice> platformVoices,
            string requestedVoiceId)
        {
            AndroidSystemTtsPlatformVoice? match = null;
            foreach (AndroidSystemTtsPlatformVoice voice in platformVoices)
            {
                if (!string.Equals(voice.VoiceId, requestedVoiceId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (match != null)
                {
                    return VoiceValidation.Rejected(
                        SpeechErrorCategory.ContractViolation,
                        "android_system_tts_duplicate_voice_id",
                        "Android TTS returned duplicate entries for the requested voice identifier.");
                }
                match = voice;
            }

            if (match == null)
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.MissingVoiceData,
                    "android_system_tts_voice_unavailable",
                    "The requested Android TTS voice is no longer available; no alternate voice was selected.");
            }
            if (!string.Equals(
                match.LanguageTag,
                configuredLanguageTag,
                StringComparison.OrdinalIgnoreCase))
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.UnsupportedLanguage,
                    "android_system_tts_voice_locale_mismatch",
                    "The requested Android TTS voice does not match the configured provider locale.");
            }

            if (match.NetworkRequirement == SpeechNetworkRequirement.Required)
            {
                if (!string.Equals(
                    match.VoiceId,
                    explicitlySelectedNetworkVoiceId,
                    StringComparison.Ordinal))
                {
                    return VoiceValidation.Rejected(
                        SpeechErrorCategory.ContractViolation,
                        "android_system_tts_network_voice_not_explicitly_selected",
                        "The requested Android TTS voice requires networking and has not been explicitly selected for this provider instance.");
                }
                return VoiceValidation.Accepted(networkVoiceApproved: true);
            }

            if (!match.IsInstalled)
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.MissingVoiceData,
                    "android_system_tts_offline_voice_not_installed",
                    "The requested non-network Android TTS voice is not installed; no network voice was selected automatically.");
            }

            return VoiceValidation.Accepted(networkVoiceApproved: false);
        }

        private TtsEvent MapPlatformEvent(
            SpeechOperationContext context,
            ulong sequence,
            AndroidSystemTtsPlatformEvent value)
        {
            switch (value.Kind)
            {
                case AndroidSystemTtsPlatformEventKind.Started:
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        TtsEventKind.Started);
                case AndroidSystemTtsPlatformEventKind.Completed:
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        TtsEventKind.Completed);
                case AndroidSystemTtsPlatformEventKind.Cancelled:
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        TtsEventKind.Cancelled);
                case AndroidSystemTtsPlatformEventKind.Failed:
                    AndroidSystemTtsPlatformFailure failure = value.Failure ??
                        throw new InvalidOperationException(
                            "Android system TTS failure event did not include failure detail.");
                    FailureMapping mapping = MapFailure(failure.Kind);
                    return Failed(
                        context,
                        sequence,
                        mapping.Category,
                        failure.Code,
                        failure.Diagnostic,
                        mapping.IsRetryable);
                default:
                    throw new InvalidOperationException(
                        "Android system TTS returned an unknown event kind.");
            }
        }

        private static FailureMapping MapFailure(AndroidSystemTtsFailureKind kind)
        {
            switch (kind)
            {
                case AndroidSystemTtsFailureKind.NetworkFailure:
                case AndroidSystemTtsFailureKind.NetworkTimeout:
                    return new FailureMapping(SpeechErrorCategory.Network, true);
                case AndroidSystemTtsFailureKind.MissingVoiceData:
                case AndroidSystemTtsFailureKind.VoiceUnavailable:
                    return new FailureMapping(SpeechErrorCategory.MissingVoiceData, false);
                case AndroidSystemTtsFailureKind.Busy:
                    return new FailureMapping(SpeechErrorCategory.Busy, true);
                case AndroidSystemTtsFailureKind.InvalidRequest:
                case AndroidSystemTtsFailureKind.VoiceRejected:
                    return new FailureMapping(SpeechErrorCategory.ContractViolation, false);
                case AndroidSystemTtsFailureKind.EngineUnavailable:
                    return new FailureMapping(SpeechErrorCategory.ProviderUnavailable, true);
                case AndroidSystemTtsFailureKind.OutputFailure:
                case AndroidSystemTtsFailureKind.ServiceFailure:
                    return new FailureMapping(SpeechErrorCategory.ServiceFailure, true);
                case AndroidSystemTtsFailureKind.SynthesisFailure:
                    return new FailureMapping(SpeechErrorCategory.ServiceFailure, false);
                default:
                    return new FailureMapping(SpeechErrorCategory.Unknown, false);
            }
        }

        private TtsEvent CancellationOrTimeout(
            SpeechOperationContext context,
            ulong sequence,
            CancellationTokenSource timeoutCancellation,
            CancellationToken callerCancellation)
        {
            if (timeoutCancellation.IsCancellationRequested &&
                !callerCancellation.IsCancellationRequested &&
                !lifetimeCancellation.IsCancellationRequested)
            {
                return Failed(
                    context,
                    sequence,
                    SpeechErrorCategory.Timeout,
                    "android_system_tts_operation_timeout",
                    "Android system/network TTS exceeded the selected speech-operation timeout.",
                    isRetryable: true);
            }

            return new TtsEvent(
                Descriptor.InstanceId,
                context.RequestId,
                sequence,
                TtsEventKind.Cancelled);
        }

        private TtsEvent Failed(
            SpeechOperationContext context,
            ulong sequence,
            SpeechErrorCategory category,
            string code,
            string diagnostic,
            bool isRetryable)
        {
            return new TtsEvent(
                Descriptor.InstanceId,
                context.RequestId,
                sequence,
                TtsEventKind.Failed,
                error: new SpeechProviderError(
                    category,
                    Bound(code, SpeechProviderError.MaximumCodeCharacters),
                    Bound(diagnostic, SpeechProviderError.MaximumDiagnosticCharacters),
                    isRetryable));
        }

        private bool TryAcquireOperation() =>
            Interlocked.CompareExchange(ref operationInFlight, 1, 0) == 0;

        private void ReleaseOperation() =>
            Interlocked.Exchange(ref operationInFlight, 0);

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AndroidSystemTtsProvider));
            }
        }

        private static int CompareVoice(TtsVoice left, TtsVoice right)
        {
            int network = left.NetworkRequirement.CompareTo(right.NetworkRequirement);
            if (network != 0)
            {
                return network;
            }
            int display = StringComparer.OrdinalIgnoreCase.Compare(
                left.DisplayName,
                right.DisplayName);
            return display != 0
                ? display
                : StringComparer.Ordinal.Compare(left.VoiceId, right.VoiceId);
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "Android system/network TTS returned no diagnostic detail."
                : value;
            return result.Length <= maximumCharacters
                ? result
                : result.Substring(0, maximumCharacters);
        }

        private static async ValueTask<PlatformMoveNextResult> MoveNextSafelyAsync(
            IAsyncEnumerator<AndroidSystemTtsPlatformEvent> enumerator)
        {
            try
            {
                bool hasValue = await enumerator.MoveNextAsync().ConfigureAwait(false);
                return hasValue
                    ? PlatformMoveNextResult.FromValue(enumerator.Current)
                    : PlatformMoveNextResult.Ended();
            }
            catch (OperationCanceledException)
            {
                return PlatformMoveNextResult.WasCancelled();
            }
            catch (Exception exception)
            {
                return PlatformMoveNextResult.Failed(exception);
            }
        }

        private sealed class ProbeEvaluation
        {
            private ProbeEvaluation(
                bool cancelled,
                AndroidSystemTtsProbe? value,
                Exception? exception)
            {
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool Cancelled { get; }
            public AndroidSystemTtsProbe? Value { get; }
            public Exception? Exception { get; }

            public static ProbeEvaluation FromValue(AndroidSystemTtsProbe value) =>
                new ProbeEvaluation(
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);

            public static ProbeEvaluation WasCancelled() =>
                new ProbeEvaluation(true, null, null);

            public static ProbeEvaluation Failed(Exception exception) =>
                new ProbeEvaluation(
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
        }

        private sealed class VoiceCatalogEvaluation
        {
            private VoiceCatalogEvaluation(
                bool cancelled,
                IReadOnlyList<AndroidSystemTtsPlatformVoice>? value,
                Exception? exception)
            {
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool Cancelled { get; }
            public IReadOnlyList<AndroidSystemTtsPlatformVoice>? Value { get; }
            public Exception? Exception { get; }

            public static VoiceCatalogEvaluation FromValue(
                IReadOnlyList<AndroidSystemTtsPlatformVoice> value) =>
                new VoiceCatalogEvaluation(
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);

            public static VoiceCatalogEvaluation WasCancelled() =>
                new VoiceCatalogEvaluation(true, null, null);

            public static VoiceCatalogEvaluation Failed(Exception exception) =>
                new VoiceCatalogEvaluation(
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
        }

        private sealed class VoiceValidation
        {
            private VoiceValidation(
                bool allowed,
                bool networkVoiceApproved,
                SpeechErrorCategory category,
                string code,
                string diagnostic,
                bool isRetryable)
            {
                Allowed = allowed;
                NetworkVoiceApproved = networkVoiceApproved;
                Category = category;
                Code = code;
                Diagnostic = diagnostic;
                IsRetryable = isRetryable;
            }

            public bool Allowed { get; }
            public bool NetworkVoiceApproved { get; }
            public SpeechErrorCategory Category { get; }
            public string Code { get; }
            public string Diagnostic { get; }
            public bool IsRetryable { get; }

            public static VoiceValidation Accepted(bool networkVoiceApproved) =>
                new VoiceValidation(
                    true,
                    networkVoiceApproved,
                    SpeechErrorCategory.Unknown,
                    "android_system_tts_voice_accepted",
                    "Android system TTS voice accepted.",
                    false);

            public static VoiceValidation Rejected(
                SpeechErrorCategory category,
                string code,
                string diagnostic,
                bool isRetryable = false) =>
                new VoiceValidation(
                    false,
                    false,
                    category,
                    code,
                    diagnostic,
                    isRetryable);
        }

        private readonly struct FailureMapping
        {
            public FailureMapping(SpeechErrorCategory category, bool isRetryable)
            {
                Category = category;
                IsRetryable = isRetryable;
            }

            public SpeechErrorCategory Category { get; }
            public bool IsRetryable { get; }
        }

        private sealed class PlatformMoveNextResult
        {
            private PlatformMoveNextResult(
                bool hasValue,
                bool cancelled,
                AndroidSystemTtsPlatformEvent? value,
                Exception? exception)
            {
                HasValue = hasValue;
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool HasValue { get; }
            public bool Cancelled { get; }
            public AndroidSystemTtsPlatformEvent? Value { get; }
            public Exception? Exception { get; }

            public static PlatformMoveNextResult FromValue(
                AndroidSystemTtsPlatformEvent value) =>
                new PlatformMoveNextResult(
                    true,
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);

            public static PlatformMoveNextResult Ended() =>
                new PlatformMoveNextResult(false, false, null, null);

            public static PlatformMoveNextResult WasCancelled() =>
                new PlatformMoveNextResult(false, true, null, null);

            public static PlatformMoveNextResult Failed(Exception exception) =>
                new PlatformMoveNextResult(
                    false,
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
        }
    }
}

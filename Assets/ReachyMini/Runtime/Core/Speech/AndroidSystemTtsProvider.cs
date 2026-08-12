#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class AndroidSystemTtsProvider : ITtsProvider
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
    }
}

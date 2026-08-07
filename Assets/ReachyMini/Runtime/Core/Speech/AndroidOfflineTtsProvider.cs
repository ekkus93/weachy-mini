#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class AndroidOfflineTtsProvider : ITtsProvider
    {
        public const string ProviderId = "android-offline-tts";
        public const string ProviderVersion = "rma-123-v1";
        public const int DefaultMaximumInputCharacters = 4000;

        private const string InstallVoiceDataAction =
            "android.speech.tts.engine.INSTALL_TTS_DATA";

        private readonly IAndroidOfflineTtsPlatform platform;
        private readonly string configuredLanguageTag;
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private int operationInFlight;
        private int disposed;

        public AndroidOfflineTtsProvider(
            IAndroidOfflineTtsPlatform platform,
            string instanceId,
            string configuredLanguageTag)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.configuredLanguageTag = SpeechProviderDescriptor.RequireText(
                configuredLanguageTag,
                nameof(configuredLanguageTag));

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.TextToSpeech,
                ProviderId,
                instanceId,
                "Android offline TTS",
                ProviderVersion,
                SpeechProviderLocation.DeviceService,
                SpeechNetworkRequirement.None);
            Capabilities = new TtsCapabilities(
                supportsCancellation: true,
                DefaultMaximumInputCharacters);
        }

        public SpeechProviderDescriptor Descriptor { get; }
        public TtsCapabilities Capabilities { get; }
        public string ConfiguredLanguageTag => configuredLanguageTag;

        public async ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!TryAcquireOperation())
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.Busy,
                    "Android offline TTS is busy with another provider operation; requests are not queued.");
            }

            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            try
            {
                Readiness readiness = await EvaluateReadinessAsync(
                    operationCancellation.Token).ConfigureAwait(false);
                return readiness.Availability;
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
                        "Android offline TTS availability probing failed with " +
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
                    "Android offline TTS is busy; voice enumeration is not queued.");
            }

            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            try
            {
                IReadOnlyList<AndroidOfflineTtsPlatformVoice> platformVoices =
                    await platform.GetVoicesAsync(
                        configuredLanguageTag,
                        operationCancellation.Token).ConfigureAwait(false);
                return BuildOfflineVoiceList(platformVoices);
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
                    "android_offline_tts_busy",
                    "Android offline TTS already has an active operation; the request was not queued.",
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
                ReadinessEvaluation readinessEvaluation =
                    await EvaluateReadinessSafelyAsync(operationCancellation.Token)
                        .ConfigureAwait(false);
                if (readinessEvaluation.Cancelled)
                {
                    yield return CancellationOrTimeout(
                        request.Context,
                        1UL,
                        timeoutCancellation,
                        cancellationToken);
                    yield break;
                }
                if (readinessEvaluation.Exception != null)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ServiceFailure,
                        "android_offline_tts_probe_failed",
                        "Android offline TTS capability probing failed with " +
                            readinessEvaluation.Exception.GetType().Name + ".",
                        isRetryable: true);
                    yield break;
                }

                Readiness readiness = readinessEvaluation.Value ??
                    throw new InvalidOperationException(
                        "Android offline TTS readiness evaluation completed without a result.");
                if (!readiness.Availability.IsAvailable)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        readiness.ErrorCategory,
                        readiness.ErrorCode,
                        readiness.Availability.Diagnostic,
                        readiness.IsRetryable);
                    yield break;
                }

                AndroidOfflineTtsProbe probe = readiness.Probe;
                int effectiveMaximumCharacters = Math.Min(
                    Capabilities.MaximumInputCharacters,
                    probe.MaximumInputCharacters);
                if (request.Text.Length > effectiveMaximumCharacters)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ContractViolation,
                        "android_offline_tts_input_too_long",
                        "TTS input exceeds the Android offline synthesis limit for this provider.",
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
                        "android_offline_tts_voice_catalog_failed",
                        "Android offline TTS voice enumeration failed with " +
                            catalogEvaluation.Exception.GetType().Name + ".",
                        isRetryable: true);
                    yield break;
                }

                IReadOnlyList<AndroidOfflineTtsPlatformVoice> platformVoices =
                    catalogEvaluation.Value ??
                    throw new InvalidOperationException(
                        "Android offline TTS voice enumeration completed without a result.");
                VoiceValidation voiceValidation = ValidateRequestedVoice(
                    platformVoices,
                    request.VoiceId);
                if (!voiceValidation.Allowed)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        voiceValidation.Category,
                        voiceValidation.Code,
                        voiceValidation.Diagnostic,
                        voiceValidation.IsRetryable);
                    yield break;
                }

                ulong sequence = 0UL;
                bool terminal = false;
                IAsyncEnumerator<AndroidOfflineTtsPlatformEvent> enumerator =
                    platform.SpeakAsync(
                            request.Context.RequestId,
                            request.Text,
                            configuredLanguageTag,
                            request.VoiceId,
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
                                "android_offline_tts_stream_failed",
                                "Android offline TTS event streaming failed with " +
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
                                "android_offline_tts_missing_terminal_event",
                                "Android offline TTS ended without a terminal callback.",
                                isRetryable: true);
                            yield break;
                        }

                        AndroidOfflineTtsPlatformEvent value = moveNext.Value ??
                            throw new InvalidOperationException(
                                "Android offline TTS produced an empty platform event.");
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
                                "android_offline_tts_callback_request_identity_mismatch",
                                "Android offline TTS callback returned a different request identifier.",
                                isRetryable: false);
                            yield break;
                        }

                        TtsEvent mapped = MapPlatformEvent(
                            request.Context,
                            sequence,
                            value);
                        terminal = value.IsTerminal;
                        yield return mapped;
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
            string? preferredVoiceId)
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
                TtsVoice? preferred = null;
                foreach (TtsVoice voice in voices)
                {
                    if (!IsUsableOfflineVoice(voice, requiredLanguage) ||
                        !string.Equals(
                            voice.VoiceId,
                            preferredVoiceId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (preferred != null)
                    {
                        throw new InvalidOperationException(
                            "Android offline TTS voice selection is ambiguous because the preferred voice identifier is duplicated.");
                    }
                    preferred = voice;
                }
                return preferred;
            }

            TtsVoice? selected = null;
            foreach (TtsVoice voice in voices)
            {
                if (!IsUsableOfflineVoice(voice, requiredLanguage))
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

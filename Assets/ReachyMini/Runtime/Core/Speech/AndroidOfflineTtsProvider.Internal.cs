#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class AndroidOfflineTtsProvider
    {
        private async ValueTask<Readiness> EvaluateReadinessAsync(
            CancellationToken cancellationToken)
        {
            AndroidOfflineTtsProbe probe = await platform.ProbeAsync(
                    configuredLanguageTag,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!probe.EngineInitialized)
            {
                return Readiness.Unavailable(
                    probe,
                    SpeechAvailabilityState.SetupRequired,
                    SpeechErrorCategory.ProviderUnavailable,
                    "android_offline_tts_engine_unavailable",
                    "Android TextToSpeech is not initialized. Configure or install a system TTS engine, then retry; no network TTS provider will be selected automatically.");
            }

            switch (probe.LanguageStatus)
            {
                case AndroidOfflineTtsLanguageStatus.MissingData:
                    return Readiness.Unavailable(
                        probe,
                        SpeechAvailabilityState.SetupRequired,
                        SpeechErrorCategory.MissingVoiceData,
                        "android_offline_tts_missing_language_data",
                        MissingVoiceDataDiagnostic());
                case AndroidOfflineTtsLanguageStatus.NotSupported:
                    return Readiness.Unavailable(
                        probe,
                        SpeechAvailabilityState.Unavailable,
                        SpeechErrorCategory.UnsupportedLanguage,
                        "android_offline_tts_language_not_supported",
                        "The configured language is not supported by the selected Android TTS engine.");
                case AndroidOfflineTtsLanguageStatus.Unknown:
                    return Readiness.Unavailable(
                        probe,
                        SpeechAvailabilityState.Faulted,
                        SpeechErrorCategory.ServiceFailure,
                        "android_offline_tts_language_status_unknown",
                        "Android TTS returned an unknown language-support status.");
            }

            if (probe.InstalledOfflineVoiceCount <= 0)
            {
                string diagnostic = probe.MatchingNetworkVoiceCount > 0
                    ? "The configured locale has no installed offline TTS voice. Network-required voices were found but are prohibited by RMA-123. " +
                        MissingVoiceDataDiagnostic()
                    : MissingVoiceDataDiagnostic();
                return Readiness.Unavailable(
                    probe,
                    SpeechAvailabilityState.SetupRequired,
                    SpeechErrorCategory.MissingVoiceData,
                    "android_offline_tts_no_installed_offline_voice",
                    diagnostic);
            }

            return Readiness.Available(
                probe,
                "Android offline TTS is available with an installed voice that declares no network requirement.");
        }

        private async ValueTask<ReadinessEvaluation> EvaluateReadinessSafelyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                Readiness readiness = await EvaluateReadinessAsync(cancellationToken)
                    .ConfigureAwait(false);
                return ReadinessEvaluation.FromValue(readiness);
            }
            catch (OperationCanceledException)
            {
                return ReadinessEvaluation.WasCancelled();
            }
            catch (Exception exception)
            {
                return ReadinessEvaluation.Failed(exception);
            }
        }

        private async ValueTask<VoiceCatalogEvaluation> LoadVoiceCatalogSafelyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<AndroidOfflineTtsPlatformVoice> voices =
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

        private IReadOnlyList<TtsVoice> BuildOfflineVoiceList(
            IReadOnlyList<AndroidOfflineTtsPlatformVoice> platformVoices)
        {
            if (platformVoices == null)
            {
                throw new ArgumentNullException(nameof(platformVoices));
            }

            var result = new List<TtsVoice>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (AndroidOfflineTtsPlatformVoice voice in platformVoices)
            {
                if (!string.Equals(
                        voice.LanguageTag,
                        configuredLanguageTag,
                        StringComparison.OrdinalIgnoreCase) ||
                    voice.NetworkRequirement != SpeechNetworkRequirement.None)
                {
                    continue;
                }
                if (!identifiers.Add(voice.VoiceId))
                {
                    throw new InvalidOperationException(
                        "Android offline TTS returned duplicate offline voice identifiers.");
                }

                result.Add(new TtsVoice(
                    voice.VoiceId,
                    voice.DisplayName,
                    voice.LanguageTag,
                    SpeechNetworkRequirement.None,
                    voice.IsInstalled));
            }

            result.Sort(CompareVoice);
            return result.AsReadOnly();
        }

        private VoiceValidation ValidateRequestedVoice(
            IReadOnlyList<AndroidOfflineTtsPlatformVoice> platformVoices,
            string requestedVoiceId)
        {
            if (platformVoices == null)
            {
                throw new ArgumentNullException(nameof(platformVoices));
            }

            AndroidOfflineTtsPlatformVoice? match = null;
            foreach (AndroidOfflineTtsPlatformVoice voice in platformVoices)
            {
                if (!string.Equals(
                    voice.VoiceId,
                    requestedVoiceId,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                if (match != null)
                {
                    return VoiceValidation.Rejected(
                        SpeechErrorCategory.ContractViolation,
                        "android_offline_tts_duplicate_voice_id",
                        "Android TTS returned duplicate entries for the requested voice identifier.");
                }
                match = voice;
            }

            if (match == null)
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.MissingVoiceData,
                    "android_offline_tts_voice_unavailable",
                    "The requested Android offline TTS voice is no longer available. " +
                        MissingVoiceDataDiagnostic());
            }
            if (!string.Equals(
                match.LanguageTag,
                configuredLanguageTag,
                StringComparison.OrdinalIgnoreCase))
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.UnsupportedLanguage,
                    "android_offline_tts_voice_locale_mismatch",
                    "The requested TTS voice does not match the configured provider locale.");
            }
            if (match.NetworkRequirement != SpeechNetworkRequirement.None)
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.ContractViolation,
                    "android_offline_tts_network_voice_rejected",
                    "The requested TTS voice requires or may use networking and is prohibited by the Android offline TTS provider.");
            }
            if (!match.IsInstalled)
            {
                return VoiceValidation.Rejected(
                    SpeechErrorCategory.MissingVoiceData,
                    "android_offline_tts_voice_not_installed",
                    MissingVoiceDataDiagnostic());
            }

            return VoiceValidation.Accepted();
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
                    "android_offline_tts_operation_timeout",
                    "Android offline TTS exceeded the selected speech-operation timeout.",
                    isRetryable: true);
            }

            return new TtsEvent(
                Descriptor.InstanceId,
                context.RequestId,
                sequence,
                TtsEventKind.Cancelled);
        }

        private TtsEvent MapPlatformEvent(
            SpeechOperationContext context,
            ulong sequence,
            AndroidOfflineTtsPlatformEvent value)
        {
            switch (value.Kind)
            {
                case AndroidOfflineTtsPlatformEventKind.Started:
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        TtsEventKind.Started);
                case AndroidOfflineTtsPlatformEventKind.Completed:
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        TtsEventKind.Completed);
                case AndroidOfflineTtsPlatformEventKind.Cancelled:
                    return new TtsEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        TtsEventKind.Cancelled);
                case AndroidOfflineTtsPlatformEventKind.Failed:
                    AndroidOfflineTtsPlatformFailure failure = value.Failure ??
                        throw new InvalidOperationException(
                            "Android offline TTS failure event did not include failure detail.");
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
                        "Android offline TTS returned an unknown event kind.");
            }
        }

        private static FailureMapping MapFailure(AndroidOfflineTtsFailureKind kind)
        {
            switch (kind)
            {
                case AndroidOfflineTtsFailureKind.MissingVoiceData:
                case AndroidOfflineTtsFailureKind.VoiceUnavailable:
                    return new FailureMapping(
                        SpeechErrorCategory.MissingVoiceData,
                        false);
                case AndroidOfflineTtsFailureKind.NetworkFailure:
                case AndroidOfflineTtsFailureKind.NetworkTimeout:
                    return new FailureMapping(
                        SpeechErrorCategory.ContractViolation,
                        false);
                case AndroidOfflineTtsFailureKind.Busy:
                    return new FailureMapping(
                        SpeechErrorCategory.Busy,
                        true);
                case AndroidOfflineTtsFailureKind.InvalidRequest:
                case AndroidOfflineTtsFailureKind.VoiceRejected:
                    return new FailureMapping(
                        SpeechErrorCategory.ContractViolation,
                        false);
                case AndroidOfflineTtsFailureKind.EngineUnavailable:
                    return new FailureMapping(
                        SpeechErrorCategory.ProviderUnavailable,
                        true);
                case AndroidOfflineTtsFailureKind.OutputFailure:
                case AndroidOfflineTtsFailureKind.ServiceFailure:
                    return new FailureMapping(
                        SpeechErrorCategory.ServiceFailure,
                        true);
                case AndroidOfflineTtsFailureKind.SynthesisFailure:
                    return new FailureMapping(
                        SpeechErrorCategory.ServiceFailure,
                        false);
                default:
                    return new FailureMapping(
                        SpeechErrorCategory.Unknown,
                        false);
            }
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
                throw new ObjectDisposedException(nameof(AndroidOfflineTtsProvider));
            }
        }

        private static bool IsUsableOfflineVoice(TtsVoice voice, string languageTag) =>
            voice.IsInstalled &&
            voice.NetworkRequirement == SpeechNetworkRequirement.None &&
            string.Equals(
                voice.LanguageTag,
                languageTag,
                StringComparison.OrdinalIgnoreCase);

        private static int CompareVoice(TtsVoice left, TtsVoice right)
        {
            int display = StringComparer.OrdinalIgnoreCase.Compare(
                left.DisplayName,
                right.DisplayName);
            return display != 0
                ? display
                : StringComparer.Ordinal.Compare(left.VoiceId, right.VoiceId);
        }

        private static string MissingVoiceDataDiagnostic() =>
            "Install offline voice data from Android Text-to-speech settings (" +
            InstallVoiceDataAction +
            "), then retry. RMA-123 will not select a network-required voice automatically.";

        private static string Bound(string value, int maximumCharacters)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "Android offline TTS returned no diagnostic detail."
                : value;
            return result.Length <= maximumCharacters
                ? result
                : result.Substring(0, maximumCharacters);
        }

        private static async ValueTask<PlatformMoveNextResult> MoveNextSafelyAsync(
            IAsyncEnumerator<AndroidOfflineTtsPlatformEvent> enumerator)
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

        private sealed class Readiness
        {
            private Readiness(
                AndroidOfflineTtsProbe probe,
                SpeechProviderAvailability availability,
                SpeechErrorCategory errorCategory,
                string errorCode,
                bool isRetryable)
            {
                Probe = probe;
                Availability = availability;
                ErrorCategory = errorCategory;
                ErrorCode = errorCode;
                IsRetryable = isRetryable;
            }

            public AndroidOfflineTtsProbe Probe { get; }
            public SpeechProviderAvailability Availability { get; }
            public SpeechErrorCategory ErrorCategory { get; }
            public string ErrorCode { get; }
            public bool IsRetryable { get; }

            public static Readiness Available(
                AndroidOfflineTtsProbe probe,
                string diagnostic) =>
                new Readiness(
                    probe ?? throw new ArgumentNullException(nameof(probe)),
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Available,
                        Bound(
                            diagnostic,
                            SpeechProviderError.MaximumDiagnosticCharacters)),
                    SpeechErrorCategory.Unknown,
                    "android_offline_tts_available",
                    false);

            public static Readiness Unavailable(
                AndroidOfflineTtsProbe probe,
                SpeechAvailabilityState state,
                SpeechErrorCategory category,
                string code,
                string diagnostic,
                bool isRetryable = false) =>
                new Readiness(
                    probe ?? throw new ArgumentNullException(nameof(probe)),
                    new SpeechProviderAvailability(
                        state,
                        Bound(
                            diagnostic,
                            SpeechProviderError.MaximumDiagnosticCharacters)),
                    category,
                    code,
                    isRetryable);
        }

        private sealed class ReadinessEvaluation
        {
            private ReadinessEvaluation(
                bool cancelled,
                Readiness? value,
                Exception? exception)
            {
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool Cancelled { get; }
            public Readiness? Value { get; }
            public Exception? Exception { get; }

            public static ReadinessEvaluation FromValue(Readiness value) =>
                new ReadinessEvaluation(
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);

            public static ReadinessEvaluation WasCancelled() =>
                new ReadinessEvaluation(true, null, null);

            public static ReadinessEvaluation Failed(Exception exception) =>
                new ReadinessEvaluation(
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
        }

        private sealed class VoiceCatalogEvaluation
        {
            private VoiceCatalogEvaluation(
                bool cancelled,
                IReadOnlyList<AndroidOfflineTtsPlatformVoice>? value,
                Exception? exception)
            {
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool Cancelled { get; }
            public IReadOnlyList<AndroidOfflineTtsPlatformVoice>? Value { get; }
            public Exception? Exception { get; }

            public static VoiceCatalogEvaluation FromValue(
                IReadOnlyList<AndroidOfflineTtsPlatformVoice> value) =>
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
                SpeechErrorCategory category,
                string code,
                string diagnostic,
                bool isRetryable)
            {
                Allowed = allowed;
                Category = category;
                Code = code;
                Diagnostic = diagnostic;
                IsRetryable = isRetryable;
            }

            public bool Allowed { get; }
            public SpeechErrorCategory Category { get; }
            public string Code { get; }
            public string Diagnostic { get; }
            public bool IsRetryable { get; }

            public static VoiceValidation Accepted() =>
                new VoiceValidation(
                    true,
                    SpeechErrorCategory.Unknown,
                    "android_offline_tts_voice_accepted",
                    "The requested offline TTS voice passed validation.",
                    false);

            public static VoiceValidation Rejected(
                SpeechErrorCategory category,
                string code,
                string diagnostic,
                bool isRetryable = false) =>
                new VoiceValidation(
                    false,
                    category,
                    code,
                    diagnostic,
                    isRetryable);
        }

        private sealed class FailureMapping
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
                AndroidOfflineTtsPlatformEvent? value,
                Exception? exception)
            {
                HasValue = hasValue;
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool HasValue { get; }
            public bool Cancelled { get; }
            public AndroidOfflineTtsPlatformEvent? Value { get; }
            public Exception? Exception { get; }

            public static PlatformMoveNextResult FromValue(
                AndroidOfflineTtsPlatformEvent value) =>
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

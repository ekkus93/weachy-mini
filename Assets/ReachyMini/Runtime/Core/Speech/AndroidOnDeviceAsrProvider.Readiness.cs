#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public sealed partial class AndroidOnDeviceAsrProvider
    {
        private async ValueTask<ReadinessEvaluation> EvaluateReadinessSafelyAsync(
            AsrOptions options,
            CancellationToken cancellationToken)
        {
            try
            {
                Readiness value = await EvaluateReadinessAsync(
                    options,
                    cancellationToken).ConfigureAwait(false);
                return ReadinessEvaluation.FromValue(value);
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

        private async ValueTask<Readiness> EvaluateReadinessAsync(
            AsrOptions options,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(
                    options.LanguageTag,
                    configuredLanguageTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.Unavailable,
                    SpeechErrorCategory.UnsupportedLanguage,
                    "android_on_device_asr_language_not_configured",
                    "The requested recognition language does not match the explicitly configured on-device ASR language.");
            }

            AndroidOnDeviceAsrProbe probe;
            try
            {
                probe = await platform.ProbeAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.Faulted,
                    SpeechErrorCategory.ServiceFailure,
                    "android_on_device_asr_probe_failed",
                    "Android on-device ASR capability discovery failed with " +
                        exception.GetType().Name +
                        ".");
            }

            if (probe.ApiLevel < 31 ||
                !probe.ExplicitOnDeviceRecognitionAvailable)
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.Unavailable,
                    SpeechErrorCategory.ProviderUnavailable,
                    "android_on_device_asr_unavailable",
                    "Android does not expose an explicit on-device SpeechRecognizer on this device/API level.");
            }

            if (!probe.HasMicrophonePermission)
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.PermissionRequired,
                    SpeechErrorCategory.Permission,
                    "android_on_device_asr_microphone_permission_required",
                    "Microphone permission must be granted before an explicit on-device SpeechRecognizer may be created.");
            }

            if (!probe.RecognitionSupportCheckAvailable)
            {
                return Readiness.Available(
                    "Explicit Android on-device recognition is available. This Android version cannot preflight per-language model support, so recognition may still report an unsupported or unavailable language at runtime.");
            }

            AndroidOnDeviceAsrSupportResult support;
            try
            {
                support = await platform.CheckSupportAsync(
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.Faulted,
                    SpeechErrorCategory.ServiceFailure,
                    "android_on_device_asr_support_check_failed",
                    "Android on-device ASR language support discovery failed with " +
                        exception.GetType().Name +
                        ".");
            }

            return support.State switch
            {
                AndroidOnDeviceAsrSupportState.Installed =>
                    Readiness.Available(support.Diagnostic),
                AndroidOnDeviceAsrSupportState.PreflightUnavailable =>
                    Readiness.Available(
                        "Explicit Android on-device recognition is available, but the recognition service could not preflight language support. Runtime language failures remain visible and no fallback is permitted."),
                AndroidOnDeviceAsrSupportState.ModelDownloadRequired =>
                    Readiness.Unavailable(
                        SpeechAvailabilityState.SetupRequired,
                        SpeechErrorCategory.UnsupportedLanguage,
                        "android_on_device_asr_language_model_download_required",
                        support.Diagnostic),
                AndroidOnDeviceAsrSupportState.ModelDownloadPending =>
                    Readiness.Unavailable(
                        SpeechAvailabilityState.SetupRequired,
                        SpeechErrorCategory.UnsupportedLanguage,
                        "android_on_device_asr_language_model_download_pending",
                        support.Diagnostic),
                AndroidOnDeviceAsrSupportState.UnsupportedLanguage =>
                    Readiness.Unavailable(
                        SpeechAvailabilityState.Unavailable,
                        SpeechErrorCategory.UnsupportedLanguage,
                        "android_on_device_asr_language_not_supported",
                        support.Diagnostic),
                AndroidOnDeviceAsrSupportState.Faulted =>
                    Readiness.Unavailable(
                        SpeechAvailabilityState.Faulted,
                        SpeechErrorCategory.ServiceFailure,
                        "android_on_device_asr_support_check_faulted",
                        support.Diagnostic),
                _ => throw new InvalidOperationException(
                    "Android on-device ASR returned an unsupported support state."),
            };
        }

        // MapFailure is called from MapPlatformEvent in
        // AndroidOnDeviceAsrProvider.Recognition.cs — the one genuine
        // cross-partial-file call dependency in this split.
        private static FailureMapping MapFailure(
            AndroidOnDeviceAsrFailureKind failure)
        {
            return failure switch
            {
                AndroidOnDeviceAsrFailureKind.PermissionDenied =>
                    new FailureMapping(SpeechErrorCategory.Permission, false),
                AndroidOnDeviceAsrFailureKind.AudioFailure =>
                    new FailureMapping(SpeechErrorCategory.ServiceFailure, true),
                AndroidOnDeviceAsrFailureKind.Timeout =>
                    new FailureMapping(SpeechErrorCategory.Timeout, true),
                AndroidOnDeviceAsrFailureKind.Busy =>
                    new FailureMapping(SpeechErrorCategory.Busy, true),
                AndroidOnDeviceAsrFailureKind.TooManyRequests =>
                    new FailureMapping(SpeechErrorCategory.Busy, true),
                AndroidOnDeviceAsrFailureKind.LanguageNotSupported =>
                    new FailureMapping(SpeechErrorCategory.UnsupportedLanguage, false),
                AndroidOnDeviceAsrFailureKind.LanguageModelUnavailable =>
                    new FailureMapping(SpeechErrorCategory.UnsupportedLanguage, false),
                AndroidOnDeviceAsrFailureKind.UnexpectedNetworkFailure =>
                    new FailureMapping(SpeechErrorCategory.ContractViolation, false),
                AndroidOnDeviceAsrFailureKind.ClientFailure =>
                    new FailureMapping(SpeechErrorCategory.ServiceFailure, false),
                AndroidOnDeviceAsrFailureKind.ServiceFailure =>
                    new FailureMapping(SpeechErrorCategory.ServiceFailure, true),
                AndroidOnDeviceAsrFailureKind.ServiceDisconnected =>
                    new FailureMapping(SpeechErrorCategory.ServiceFailure, true),
                AndroidOnDeviceAsrFailureKind.Unknown =>
                    new FailureMapping(SpeechErrorCategory.Unknown, false),
                _ => throw new InvalidOperationException(
                    "Android on-device ASR returned an unsupported failure kind."),
            };
        }

        private sealed class Readiness
        {
            private Readiness(
                SpeechProviderAvailability availability,
                SpeechErrorCategory errorCategory,
                string errorCode,
                bool isRetryable)
            {
                Availability = availability;
                ErrorCategory = errorCategory;
                ErrorCode = errorCode;
                IsRetryable = isRetryable;
            }

            public SpeechProviderAvailability Availability { get; }
            public SpeechErrorCategory ErrorCategory { get; }
            public string ErrorCode { get; }
            public bool IsRetryable { get; }

            public static Readiness Available(string diagnostic)
            {
                return new Readiness(
                    new SpeechProviderAvailability(
                        SpeechAvailabilityState.Available,
                        Bound(
                            diagnostic,
                            SpeechProviderError.MaximumDiagnosticCharacters)),
                    SpeechErrorCategory.Unknown,
                    "android_on_device_asr_available",
                    false);
            }

            public static Readiness Unavailable(
                SpeechAvailabilityState state,
                SpeechErrorCategory category,
                string code,
                string diagnostic,
                bool isRetryable = false)
            {
                return new Readiness(
                    new SpeechProviderAvailability(
                        state,
                        Bound(
                            diagnostic,
                            SpeechProviderError.MaximumDiagnosticCharacters)),
                    category,
                    code,
                    isRetryable);
            }
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

            public static ReadinessEvaluation FromValue(Readiness value)
            {
                return new ReadinessEvaluation(
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);
            }

            public static ReadinessEvaluation WasCancelled()
            {
                return new ReadinessEvaluation(
                    true,
                    null,
                    null);
            }

            public static ReadinessEvaluation Failed(Exception exception)
            {
                return new ReadinessEvaluation(
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
            }
        }

        private sealed class FailureMapping
        {
            public FailureMapping(
                SpeechErrorCategory category,
                bool isRetryable)
            {
                Category = category;
                IsRetryable = isRetryable;
            }

            public SpeechErrorCategory Category { get; }
            public bool IsRetryable { get; }
        }
    }
}

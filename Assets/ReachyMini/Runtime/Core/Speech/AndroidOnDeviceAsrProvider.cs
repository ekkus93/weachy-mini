#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum AndroidOnDeviceAsrSupportState
    {
        Installed = 0,
        PreflightUnavailable = 1,
        ModelDownloadRequired = 2,
        ModelDownloadPending = 3,
        UnsupportedLanguage = 4,
        Faulted = 5,
    }

    public enum AndroidOnDeviceAsrPlatformEventKind
    {
        Started = 0,
        PartialResult = 1,
        FinalResult = 2,
        NoMatch = 3,
        Cancelled = 4,
        Failed = 5,
    }

    public enum AndroidOnDeviceAsrFailureKind
    {
        PermissionDenied = 0,
        AudioFailure = 1,
        Timeout = 2,
        ClientFailure = 3,
        ServiceFailure = 4,
        Busy = 5,
        TooManyRequests = 6,
        ServiceDisconnected = 7,
        LanguageNotSupported = 8,
        LanguageModelUnavailable = 9,
        UnexpectedNetworkFailure = 10,
        Unknown = 11,
    }

    public sealed class AndroidOnDeviceAsrProbe
    {
        public AndroidOnDeviceAsrProbe(
            int apiLevel,
            bool hasMicrophonePermission,
            bool explicitOnDeviceRecognitionAvailable,
            bool recognitionSupportCheckAvailable)
        {
            if (apiLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(apiLevel));
            }
            if (apiLevel < 31 && explicitOnDeviceRecognitionAvailable)
            {
                throw new ArgumentException(
                    "Explicit Android on-device recognition cannot be advertised below API 31.",
                    nameof(explicitOnDeviceRecognitionAvailable));
            }
            if (apiLevel < 33 && recognitionSupportCheckAvailable)
            {
                throw new ArgumentException(
                    "Android recognition-support preflight cannot be advertised below API 33.",
                    nameof(recognitionSupportCheckAvailable));
            }

            ApiLevel = apiLevel;
            HasMicrophonePermission = hasMicrophonePermission;
            ExplicitOnDeviceRecognitionAvailable = explicitOnDeviceRecognitionAvailable;
            RecognitionSupportCheckAvailable = recognitionSupportCheckAvailable;
        }

        public int ApiLevel { get; }
        public bool HasMicrophonePermission { get; }
        public bool ExplicitOnDeviceRecognitionAvailable { get; }
        public bool RecognitionSupportCheckAvailable { get; }
    }

    public sealed class AndroidOnDeviceAsrSupportResult
    {
        public AndroidOnDeviceAsrSupportResult(
            AndroidOnDeviceAsrSupportState state,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(AndroidOnDeviceAsrSupportState), state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            State = state;
            Diagnostic = RequireDiagnostic(diagnostic, nameof(diagnostic));
        }

        public AndroidOnDeviceAsrSupportState State { get; }
        public string Diagnostic { get; }

        private static string RequireDiagnostic(string value, string name)
        {
            string result = SpeechProviderDescriptor.RequireText(value, name);
            return result.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? result
                : result.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
        }
    }

    public sealed class AndroidOnDeviceAsrPlatformFailure
    {
        public AndroidOnDeviceAsrPlatformFailure(
            AndroidOnDeviceAsrFailureKind kind,
            string code,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(AndroidOnDeviceAsrFailureKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Code = Bound(
                SpeechProviderDescriptor.RequireText(code, nameof(code)),
                SpeechProviderError.MaximumCodeCharacters);
            Diagnostic = Bound(
                SpeechProviderDescriptor.RequireText(diagnostic, nameof(diagnostic)),
                SpeechProviderError.MaximumDiagnosticCharacters);
        }

        public AndroidOnDeviceAsrFailureKind Kind { get; }
        public string Code { get; }
        public string Diagnostic { get; }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
        }
    }

    public sealed class AndroidOnDeviceAsrPlatformEvent
    {
        public AndroidOnDeviceAsrPlatformEvent(
            string requestId,
            AndroidOnDeviceAsrPlatformEventKind kind,
            string? transcript = null,
            AndroidOnDeviceAsrPlatformFailure? failure = null)
        {
            if (!Enum.IsDefined(typeof(AndroidOnDeviceAsrPlatformEventKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            bool transcriptEvent =
                kind == AndroidOnDeviceAsrPlatformEventKind.PartialResult ||
                kind == AndroidOnDeviceAsrPlatformEventKind.FinalResult;
            bool hasTranscript = !string.IsNullOrWhiteSpace(transcript);
            if (transcriptEvent != hasTranscript)
            {
                throw new ArgumentException(
                    "Only Android ASR partial/final events carry transcript text.",
                    nameof(transcript));
            }
            if ((kind == AndroidOnDeviceAsrPlatformEventKind.Failed) !=
                (failure != null))
            {
                throw new ArgumentException(
                    "Only Android ASR failed events carry platform failure detail.",
                    nameof(failure));
            }

            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            Kind = kind;
            Transcript = transcript;
            Failure = failure;
        }

        public string RequestId { get; }
        public AndroidOnDeviceAsrPlatformEventKind Kind { get; }
        public string? Transcript { get; }
        public AndroidOnDeviceAsrPlatformFailure? Failure { get; }
        public bool IsTerminal =>
            Kind == AndroidOnDeviceAsrPlatformEventKind.FinalResult ||
            Kind == AndroidOnDeviceAsrPlatformEventKind.NoMatch ||
            Kind == AndroidOnDeviceAsrPlatformEventKind.Cancelled ||
            Kind == AndroidOnDeviceAsrPlatformEventKind.Failed;
    }

    public interface IAndroidOnDeviceAsrPlatform : IAsyncDisposable
    {
        ValueTask<AndroidOnDeviceAsrProbe> ProbeAsync(
            CancellationToken cancellationToken);

        ValueTask<AndroidOnDeviceAsrSupportResult> CheckSupportAsync(
            AsrOptions options,
            CancellationToken cancellationToken);

        IAsyncEnumerable<AndroidOnDeviceAsrPlatformEvent> RecognizeAsync(
            string requestId,
            AsrOptions options,
            CancellationToken cancellationToken);
    }

    public sealed class AndroidOnDeviceAsrProvider : IAsrProvider
    {
        public const string ProviderId = "android-explicit-on-device-speech-recognizer";
        public const string ProviderVersion = "rma-121-v1";

        private readonly IAndroidOnDeviceAsrPlatform platform;
        private readonly string configuredLanguageTag;
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private int operationInFlight;
        private int disposed;

        public AndroidOnDeviceAsrProvider(
            IAndroidOnDeviceAsrPlatform platform,
            string instanceId,
            string configuredLanguageTag,
            TimeSpan maximumUtteranceDuration)
        {
            this.platform = platform ??
                throw new ArgumentNullException(nameof(platform));
            this.configuredLanguageTag = SpeechProviderDescriptor.RequireText(
                configuredLanguageTag,
                nameof(configuredLanguageTag));

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                ProviderId,
                instanceId,
                "Android explicit on-device ASR",
                ProviderVersion,
                SpeechProviderLocation.OnDevice,
                SpeechNetworkRequirement.None);
            Capabilities = new AsrCapabilities(
                new[] { this.configuredLanguageTag },
                supportsPartialResults: true,
                supportsCancellation: true,
                maximumUtteranceDuration);
        }

        public SpeechProviderDescriptor Descriptor { get; }

        public AsrCapabilities Capabilities { get; }

        public async ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            AsrOptions options,
            CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ThrowIfDisposed();
            if (!TryAcquireOperation())
            {
                return new SpeechProviderAvailability(
                    SpeechAvailabilityState.Busy,
                    "Android on-device ASR is busy with another provider operation; requests are not queued.");
            }

            try
            {
                Readiness readiness = await EvaluateReadinessAsync(
                    options,
                    cancellationToken).ConfigureAwait(false);
                return readiness.Availability;
            }
            finally
            {
                ReleaseOperation();
            }
        }

        public async IAsyncEnumerable<AsrEvent> RecognizeAsync(
            AsrRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ThrowIfDisposed();
            SpeechProviderContract.ValidateProviderForOperation(
                Descriptor,
                request.Context);

            if (!TryAcquireOperation())
            {
                yield return Failed(
                    request.Context,
                    1UL,
                    SpeechErrorCategory.Busy,
                    "android_on_device_asr_busy",
                    "Android on-device ASR already has an active operation; the request was not queued.",
                    isRetryable: true);
                yield break;
            }

            TimeSpan effectiveTimeout =
                request.Context.Timeout <= Capabilities.MaximumUtteranceDuration
                    ? request.Context.Timeout
                    : Capabilities.MaximumUtteranceDuration;
            using var timeoutCancellation =
                new CancellationTokenSource(effectiveTimeout);
            using var operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lifetimeCancellation.Token,
                    timeoutCancellation.Token);

            try
            {
                ReadinessEvaluation readinessEvaluation =
                    await EvaluateReadinessSafelyAsync(
                        request.Options,
                        operationCancellation.Token).ConfigureAwait(false);
                if (readinessEvaluation.Cancelled)
                {
                    if (timeoutCancellation.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested &&
                        !lifetimeCancellation.IsCancellationRequested)
                    {
                        yield return Failed(
                            request.Context,
                            1UL,
                            SpeechErrorCategory.Timeout,
                            "android_on_device_asr_operation_timeout",
                            "Android on-device ASR exceeded the selected utterance timeout during capability or language preflight.",
                            isRetryable: true);
                    }
                    else
                    {
                        yield return new AsrEvent(
                            Descriptor.InstanceId,
                            request.Context.RequestId,
                            1UL,
                            AsrEventKind.Cancelled);
                    }
                    yield break;
                }
                if (readinessEvaluation.Exception != null)
                {
                    yield return Failed(
                        request.Context,
                        1UL,
                        SpeechErrorCategory.ServiceFailure,
                        "android_on_device_asr_readiness_exception",
                        "Android on-device ASR readiness evaluation failed with " +
                            readinessEvaluation.Exception.GetType().Name +
                            "; no alternate provider was attempted.",
                        isRetryable: true);
                    yield break;
                }

                Readiness readiness = readinessEvaluation.Value ??
                    throw new InvalidOperationException(
                        "Android on-device ASR readiness evaluation returned no result.");
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

                ulong sequence = 0UL;
                bool terminal = false;
                IAsyncEnumerable<AndroidOnDeviceAsrPlatformEvent> stream =
                    platform.RecognizeAsync(
                        request.Context.RequestId,
                        request.Options,
                        operationCancellation.Token);
                await using IAsyncEnumerator<AndroidOnDeviceAsrPlatformEvent> enumerator =
                    stream.GetAsyncEnumerator(operationCancellation.Token);

                while (!terminal)
                {
                    PlatformMoveNextResult move =
                        await MoveNextSafelyAsync(enumerator).ConfigureAwait(false);

                    if (move.Cancelled)
                    {
                        sequence = checked(sequence + 1UL);
                        if (timeoutCancellation.IsCancellationRequested &&
                            !cancellationToken.IsCancellationRequested &&
                            !lifetimeCancellation.IsCancellationRequested)
                        {
                            yield return Failed(
                                request.Context,
                                sequence,
                                SpeechErrorCategory.Timeout,
                                "android_on_device_asr_operation_timeout",
                                "Android on-device ASR exceeded the selected utterance timeout and was cancelled.",
                                isRetryable: true);
                        }
                        else
                        {
                            yield return new AsrEvent(
                                Descriptor.InstanceId,
                                request.Context.RequestId,
                                sequence,
                                AsrEventKind.Cancelled);
                        }

                        yield break;
                    }

                    if (move.Exception != null)
                    {
                        sequence = checked(sequence + 1UL);
                        yield return Failed(
                            request.Context,
                            sequence,
                            SpeechErrorCategory.ServiceFailure,
                            "android_on_device_asr_platform_exception",
                            "Android on-device ASR platform stream failed with " +
                                move.Exception.GetType().Name +
                                "; no alternate provider was attempted.",
                            isRetryable: true);
                        yield break;
                    }

                    if (!move.HasValue)
                    {
                        sequence = checked(sequence + 1UL);
                        yield return Failed(
                            request.Context,
                            sequence,
                            SpeechErrorCategory.ServiceFailure,
                            "android_on_device_asr_stream_ended_without_terminal_event",
                            "Android on-device ASR ended without a final, no-match, cancellation, or failure event.",
                            isRetryable: true);
                        yield break;
                    }

                    AndroidOnDeviceAsrPlatformEvent platformEvent = move.Value!;
                    if (!string.Equals(
                            platformEvent.RequestId,
                            request.Context.RequestId,
                            StringComparison.Ordinal))
                    {
                        operationCancellation.Cancel();
                        sequence = checked(sequence + 1UL);
                        yield return Failed(
                            request.Context,
                            sequence,
                            SpeechErrorCategory.ContractViolation,
                            "android_on_device_asr_request_identity_mismatch",
                            "Android on-device ASR returned an event for a different request; the active recognizer was cancelled.",
                            isRetryable: false);
                        yield break;
                    }

                    sequence = checked(sequence + 1UL);
                    AsrEvent providerEvent = MapPlatformEvent(
                        request.Context,
                        sequence,
                        platformEvent,
                        timeoutCancellation.IsCancellationRequested,
                        cancellationToken.IsCancellationRequested,
                        lifetimeCancellation.IsCancellationRequested);
                    terminal = platformEvent.IsTerminal;
                    yield return providerEvent;
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

        private AsrEvent MapPlatformEvent(
            SpeechOperationContext context,
            ulong sequence,
            AndroidOnDeviceAsrPlatformEvent platformEvent,
            bool timeoutRequested,
            bool callerCancellationRequested,
            bool lifetimeCancellationRequested)
        {
            switch (platformEvent.Kind)
            {
                case AndroidOnDeviceAsrPlatformEventKind.Started:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.Started);
                case AndroidOnDeviceAsrPlatformEventKind.PartialResult:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.PartialResult,
                        platformEvent.Transcript);
                case AndroidOnDeviceAsrPlatformEventKind.FinalResult:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.FinalResult,
                        platformEvent.Transcript);
                case AndroidOnDeviceAsrPlatformEventKind.NoMatch:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.NoMatch);
                case AndroidOnDeviceAsrPlatformEventKind.Cancelled:
                    if (timeoutRequested &&
                        !callerCancellationRequested &&
                        !lifetimeCancellationRequested)
                    {
                        return Failed(
                            context,
                            sequence,
                            SpeechErrorCategory.Timeout,
                            "android_on_device_asr_operation_timeout",
                            "Android on-device ASR exceeded the selected utterance timeout and was cancelled.",
                            isRetryable: true);
                    }

                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.Cancelled);
                case AndroidOnDeviceAsrPlatformEventKind.Failed:
                    AndroidOnDeviceAsrPlatformFailure failure =
                        platformEvent.Failure ??
                        throw new InvalidOperationException(
                            "A failed Android ASR platform event did not contain failure detail.");
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
                        "Android on-device ASR returned an unsupported event kind.");
            }
        }

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

        private AsrEvent Failed(
            SpeechOperationContext context,
            ulong sequence,
            SpeechErrorCategory category,
            string code,
            string diagnostic,
            bool isRetryable)
        {
            return new AsrEvent(
                Descriptor.InstanceId,
                context.RequestId,
                sequence,
                AsrEventKind.Failed,
                error: new SpeechProviderError(
                    category,
                    Bound(code, SpeechProviderError.MaximumCodeCharacters),
                    Bound(
                        diagnostic,
                        SpeechProviderError.MaximumDiagnosticCharacters),
                    isRetryable));
        }

        private bool TryAcquireOperation()
        {
            return Interlocked.CompareExchange(
                ref operationInFlight,
                1,
                0) == 0;
        }

        private void ReleaseOperation()
        {
            Interlocked.Exchange(ref operationInFlight, 0);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(AndroidOnDeviceAsrProvider));
            }
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "Android on-device ASR returned no diagnostic detail."
                : value;
            return result.Length <= maximumCharacters
                ? result
                : result.Substring(0, maximumCharacters);
        }

        private static async ValueTask<PlatformMoveNextResult> MoveNextSafelyAsync(
            IAsyncEnumerator<AndroidOnDeviceAsrPlatformEvent> enumerator)
        {
            try
            {
                bool hasValue = await enumerator.MoveNextAsync()
                    .ConfigureAwait(false);
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

        private sealed class PlatformMoveNextResult
        {
            private PlatformMoveNextResult(
                bool hasValue,
                bool cancelled,
                AndroidOnDeviceAsrPlatformEvent? value,
                Exception? exception)
            {
                HasValue = hasValue;
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool HasValue { get; }
            public bool Cancelled { get; }
            public AndroidOnDeviceAsrPlatformEvent? Value { get; }
            public Exception? Exception { get; }

            public static PlatformMoveNextResult FromValue(
                AndroidOnDeviceAsrPlatformEvent value)
            {
                return new PlatformMoveNextResult(
                    true,
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);
            }

            public static PlatformMoveNextResult Ended()
            {
                return new PlatformMoveNextResult(
                    false,
                    false,
                    null,
                    null);
            }

            public static PlatformMoveNextResult WasCancelled()
            {
                return new PlatformMoveNextResult(
                    false,
                    true,
                    null,
                    null);
            }

            public static PlatformMoveNextResult Failed(Exception exception)
            {
                return new PlatformMoveNextResult(
                    false,
                    false,
                    null,
                    exception ?? throw new ArgumentNullException(nameof(exception)));
            }
        }
    }
}

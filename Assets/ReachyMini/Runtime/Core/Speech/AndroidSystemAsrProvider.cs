#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum AndroidSystemAsrPlatformEventKind
    {
        Started = 0,
        PartialResult = 1,
        FinalResult = 2,
        NoMatch = 3,
        Cancelled = 4,
        Failed = 5,
    }

    public enum AndroidSystemAsrFailureKind
    {
        PermissionDenied = 0,
        AudioFailure = 1,
        SpeechTimeout = 2,
        NetworkFailure = 3,
        NetworkTimeout = 4,
        ClientFailure = 5,
        ServiceFailure = 6,
        Busy = 7,
        TooManyRequests = 8,
        ServiceDisconnected = 9,
        LanguageNotSupported = 10,
        LanguageUnavailable = 11,
        Unknown = 12,
    }

    public sealed class AndroidSystemAsrProbe
    {
        public AndroidSystemAsrProbe(
            int apiLevel,
            bool hasMicrophonePermission,
            bool systemRecognitionAvailable)
        {
            if (apiLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(apiLevel));
            }

            ApiLevel = apiLevel;
            HasMicrophonePermission = hasMicrophonePermission;
            SystemRecognitionAvailable = systemRecognitionAvailable;
        }

        public int ApiLevel { get; }
        public bool HasMicrophonePermission { get; }
        public bool SystemRecognitionAvailable { get; }
    }

    public sealed class AndroidSystemAsrPlatformFailure
    {
        public AndroidSystemAsrPlatformFailure(
            AndroidSystemAsrFailureKind kind,
            string code,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(AndroidSystemAsrFailureKind), kind))
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

        public AndroidSystemAsrFailureKind Kind { get; }
        public string Code { get; }
        public string Diagnostic { get; }

        private static string Bound(string value, int maximumCharacters)
        {
            return value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
        }
    }

    public sealed class AndroidSystemAsrPlatformEvent
    {
        public AndroidSystemAsrPlatformEvent(
            string requestId,
            AndroidSystemAsrPlatformEventKind kind,
            string? transcript = null,
            AndroidSystemAsrPlatformFailure? failure = null)
        {
            if (!Enum.IsDefined(typeof(AndroidSystemAsrPlatformEventKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            bool transcriptEvent =
                kind == AndroidSystemAsrPlatformEventKind.PartialResult ||
                kind == AndroidSystemAsrPlatformEventKind.FinalResult;
            bool hasTranscript = !string.IsNullOrWhiteSpace(transcript);
            if (transcriptEvent != hasTranscript)
            {
                throw new ArgumentException(
                    "Only Android system ASR partial/final events carry transcript text.",
                    nameof(transcript));
            }
            if ((kind == AndroidSystemAsrPlatformEventKind.Failed) != (failure != null))
            {
                throw new ArgumentException(
                    "Only Android system ASR failed events carry platform failure detail.",
                    nameof(failure));
            }

            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            Kind = kind;
            Transcript = transcript;
            Failure = failure;
        }

        public string RequestId { get; }
        public AndroidSystemAsrPlatformEventKind Kind { get; }
        public string? Transcript { get; }
        public AndroidSystemAsrPlatformFailure? Failure { get; }
        public bool IsTerminal =>
            Kind == AndroidSystemAsrPlatformEventKind.FinalResult ||
            Kind == AndroidSystemAsrPlatformEventKind.NoMatch ||
            Kind == AndroidSystemAsrPlatformEventKind.Cancelled ||
            Kind == AndroidSystemAsrPlatformEventKind.Failed;
    }

    public interface IAndroidSystemAsrPlatform : IAsyncDisposable
    {
        ValueTask<AndroidSystemAsrProbe> ProbeAsync(CancellationToken cancellationToken);

        IAsyncEnumerable<AndroidSystemAsrPlatformEvent> RecognizeAsync(
            string requestId,
            AsrOptions options,
            CancellationToken cancellationToken);
    }

    public sealed class AndroidSystemAsrProvider : IAsrProvider
    {
        public const string ProviderId = "android-system-speech-recognizer";
        public const string ProviderVersion = "rma-122-v1";

        private readonly IAndroidSystemAsrPlatform platform;
        private readonly string configuredLanguageTag;
        private readonly CancellationTokenSource lifetimeCancellation =
            new CancellationTokenSource();
        private int operationInFlight;
        private int disposed;

        public AndroidSystemAsrProvider(
            IAndroidSystemAsrPlatform platform,
            string instanceId,
            string configuredLanguageTag,
            TimeSpan maximumUtteranceDuration)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            this.configuredLanguageTag = SpeechProviderDescriptor.RequireText(
                configuredLanguageTag,
                nameof(configuredLanguageTag));

            Descriptor = new SpeechProviderDescriptor(
                SpeechProviderKind.AutomaticSpeechRecognition,
                ProviderId,
                instanceId,
                "Android system ASR (may use network)",
                ProviderVersion,
                SpeechProviderLocation.DeviceService,
                SpeechNetworkRequirement.ProviderControlled);
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
                    "Android system ASR is busy with another provider operation; requests are not queued.");
            }

            try
            {
                Readiness readiness = await EvaluateReadinessAsync(
                    options,
                    cancellationToken).ConfigureAwait(false);
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
                        "Android system ASR availability probing failed with " +
                        exception.GetType().Name + ".",
                        SpeechProviderError.MaximumDiagnosticCharacters));
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
            SpeechProviderContract.ValidateProviderForOperation(Descriptor, request.Context);

            if (!TryAcquireOperation())
            {
                yield return Failed(
                    request.Context,
                    1UL,
                    SpeechErrorCategory.Busy,
                    "android_system_asr_busy",
                    "Android system ASR already has an active operation; the request was not queued.",
                    isRetryable: true);
                yield break;
            }

            TimeSpan effectiveTimeout =
                request.Context.Timeout <= Capabilities.MaximumUtteranceDuration
                    ? request.Context.Timeout
                    : Capabilities.MaximumUtteranceDuration;
            using var timeoutCancellation = new CancellationTokenSource(effectiveTimeout);
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token,
                timeoutCancellation.Token);

            try
            {
                ReadinessEvaluation readinessEvaluation = await EvaluateReadinessSafelyAsync(
                    request.Options,
                    operationCancellation.Token).ConfigureAwait(false);
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
                        "android_system_asr_probe_failed",
                        "Android system ASR capability probing failed with " +
                            readinessEvaluation.Exception.GetType().Name + ".",
                        isRetryable: true);
                    yield break;
                }

                Readiness readiness = readinessEvaluation.Value ?? throw new InvalidOperationException(
                    "Android system ASR readiness evaluation completed without a result.");
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
                IAsyncEnumerator<AndroidSystemAsrPlatformEvent> enumerator =
                    platform.RecognizeAsync(
                        request.Context.RequestId,
                        request.Options,
                        operationCancellation.Token)
                    .GetAsyncEnumerator(operationCancellation.Token);
                await using (enumerator.ConfigureAwait(false))
                {
                    while (!terminal)
                    {
                        PlatformMoveNextResult moveNext = await MoveNextSafelyAsync(enumerator)
                            .ConfigureAwait(false);
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
                                "android_system_asr_stream_failed",
                                "Android system ASR event streaming failed with " +
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
                                "android_system_asr_missing_terminal_event",
                                "Android system ASR ended without a terminal callback.",
                                isRetryable: true);
                            yield break;
                        }

                        AndroidSystemAsrPlatformEvent value = moveNext.Value ??
                            throw new InvalidOperationException(
                                "Android system ASR produced an empty platform event.");
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
                                "android_system_asr_callback_request_identity_mismatch",
                                "Android system ASR callback returned a different request identifier.",
                                isRetryable: false);
                            yield break;
                        }

                        AsrEvent mapped = MapPlatformEvent(request.Context, sequence, value);
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
                    "android_system_asr_language_not_configured",
                    "The requested ASR language does not match this Android system ASR provider instance.");
            }

            AndroidSystemAsrProbe probe = await platform.ProbeAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!probe.HasMicrophonePermission)
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.PermissionRequired,
                    SpeechErrorCategory.Permission,
                    "android_system_asr_microphone_permission_required",
                    "Microphone permission is required before Android system ASR can create a recognizer.");
            }
            if (!probe.SystemRecognitionAvailable)
            {
                return Readiness.Unavailable(
                    SpeechAvailabilityState.Unavailable,
                    SpeechErrorCategory.ProviderUnavailable,
                    "android_system_asr_service_unavailable",
                    "Android reports no installed system speech-recognition service.");
            }

            return Readiness.Available(
                "Android system ASR is available. The selected device recognition service controls processing locality and may use the network.");
        }

        private async ValueTask<ReadinessEvaluation> EvaluateReadinessSafelyAsync(
            AsrOptions options,
            CancellationToken cancellationToken)
        {
            try
            {
                Readiness readiness = await EvaluateReadinessAsync(options, cancellationToken)
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

        private AsrEvent CancellationOrTimeout(
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
                    "android_system_asr_operation_timeout",
                    "Android system ASR exceeded the selected utterance timeout.",
                    isRetryable: true);
            }

            return new AsrEvent(
                Descriptor.InstanceId,
                context.RequestId,
                sequence,
                AsrEventKind.Cancelled);
        }

        private AsrEvent MapPlatformEvent(
            SpeechOperationContext context,
            ulong sequence,
            AndroidSystemAsrPlatformEvent value)
        {
            switch (value.Kind)
            {
                case AndroidSystemAsrPlatformEventKind.Started:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.Started);
                case AndroidSystemAsrPlatformEventKind.PartialResult:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.PartialResult,
                        value.Transcript);
                case AndroidSystemAsrPlatformEventKind.FinalResult:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.FinalResult,
                        value.Transcript);
                case AndroidSystemAsrPlatformEventKind.NoMatch:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.NoMatch);
                case AndroidSystemAsrPlatformEventKind.Cancelled:
                    return new AsrEvent(
                        Descriptor.InstanceId,
                        context.RequestId,
                        sequence,
                        AsrEventKind.Cancelled);
                case AndroidSystemAsrPlatformEventKind.Failed:
                    AndroidSystemAsrPlatformFailure failure = value.Failure ??
                        throw new InvalidOperationException(
                            "Android system ASR failure event did not include failure detail.");
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
                        "Android system ASR returned an unknown event kind.");
            }
        }

        private static FailureMapping MapFailure(AndroidSystemAsrFailureKind kind)
        {
            switch (kind)
            {
                case AndroidSystemAsrFailureKind.PermissionDenied:
                    return new FailureMapping(SpeechErrorCategory.Permission, false);
                case AndroidSystemAsrFailureKind.SpeechTimeout:
                    return new FailureMapping(SpeechErrorCategory.Timeout, true);
                case AndroidSystemAsrFailureKind.NetworkFailure:
                    return new FailureMapping(SpeechErrorCategory.Network, true);
                case AndroidSystemAsrFailureKind.NetworkTimeout:
                    return new FailureMapping(SpeechErrorCategory.Timeout, true);
                case AndroidSystemAsrFailureKind.Busy:
                case AndroidSystemAsrFailureKind.TooManyRequests:
                    return new FailureMapping(SpeechErrorCategory.Busy, true);
                case AndroidSystemAsrFailureKind.LanguageNotSupported:
                case AndroidSystemAsrFailureKind.LanguageUnavailable:
                    return new FailureMapping(SpeechErrorCategory.UnsupportedLanguage, false);
                case AndroidSystemAsrFailureKind.ServiceDisconnected:
                    return new FailureMapping(SpeechErrorCategory.ServiceFailure, true);
                case AndroidSystemAsrFailureKind.AudioFailure:
                case AndroidSystemAsrFailureKind.ClientFailure:
                case AndroidSystemAsrFailureKind.ServiceFailure:
                    return new FailureMapping(SpeechErrorCategory.ServiceFailure, true);
                default:
                    return new FailureMapping(SpeechErrorCategory.Unknown, false);
            }
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
                    Bound(diagnostic, SpeechProviderError.MaximumDiagnosticCharacters),
                    isRetryable));
        }

        private bool TryAcquireOperation()
        {
            return Interlocked.CompareExchange(ref operationInFlight, 1, 0) == 0;
        }

        private void ReleaseOperation()
        {
            Interlocked.Exchange(ref operationInFlight, 0);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AndroidSystemAsrProvider));
            }
        }

        private static string Bound(string value, int maximumCharacters)
        {
            string result = string.IsNullOrWhiteSpace(value)
                ? "Android system ASR returned no diagnostic detail."
                : value;
            return result.Length <= maximumCharacters
                ? result
                : result.Substring(0, maximumCharacters);
        }

        private static async ValueTask<PlatformMoveNextResult> MoveNextSafelyAsync(
            IAsyncEnumerator<AndroidSystemAsrPlatformEvent> enumerator)
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
                        Bound(diagnostic, SpeechProviderError.MaximumDiagnosticCharacters)),
                    SpeechErrorCategory.Unknown,
                    "android_system_asr_available",
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
                        Bound(diagnostic, SpeechProviderError.MaximumDiagnosticCharacters)),
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
                return new ReadinessEvaluation(true, null, null);
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
                AndroidSystemAsrPlatformEvent? value,
                Exception? exception)
            {
                HasValue = hasValue;
                Cancelled = cancelled;
                Value = value;
                Exception = exception;
            }

            public bool HasValue { get; }
            public bool Cancelled { get; }
            public AndroidSystemAsrPlatformEvent? Value { get; }
            public Exception? Exception { get; }

            public static PlatformMoveNextResult FromValue(AndroidSystemAsrPlatformEvent value)
            {
                return new PlatformMoveNextResult(
                    true,
                    false,
                    value ?? throw new ArgumentNullException(nameof(value)),
                    null);
            }

            public static PlatformMoveNextResult Ended()
            {
                return new PlatformMoveNextResult(false, false, null, null);
            }

            public static PlatformMoveNextResult WasCancelled()
            {
                return new PlatformMoveNextResult(false, true, null, null);
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

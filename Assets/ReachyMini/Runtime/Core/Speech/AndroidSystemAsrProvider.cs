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
                    if (timeoutCancellation.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested &&
                        !lifetimeCancellation.IsCancellationRequested)
                    {
                        yield return Failed(
                            request.Context,
                            1UL,
                            SpeechErrorCategory.Timeout,
                            "android_system_asr_operation_timeout",
                          "Android system ASR exceeded the selected utterance timeout during capability probing.",
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
                if (readi
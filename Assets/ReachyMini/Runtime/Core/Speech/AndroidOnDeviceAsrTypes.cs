#nullable enable

using System;
using System.Collections.Generic;
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
}

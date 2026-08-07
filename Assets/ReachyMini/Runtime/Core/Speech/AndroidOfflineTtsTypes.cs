#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum AndroidOfflineTtsLanguageStatus
    {
        ExactAvailable = 0,
        CountryAvailable = 1,
        LanguageAvailable = 2,
        MissingData = 3,
        NotSupported = 4,
        Unknown = 5,
    }

    public enum AndroidOfflineTtsPlatformEventKind
    {
        Started = 0,
        Completed = 1,
        Cancelled = 2,
        Failed = 3,
    }

    public enum AndroidOfflineTtsFailureKind
    {
        EngineUnavailable = 0,
        MissingVoiceData = 1,
        NetworkFailure = 2,
        NetworkTimeout = 3,
        OutputFailure = 4,
        ServiceFailure = 5,
        SynthesisFailure = 6,
        InvalidRequest = 7,
        Busy = 8,
        VoiceUnavailable = 9,
        VoiceRejected = 10,
        Unknown = 11,
    }

    public sealed class AndroidOfflineTtsPlatformVoice
    {
        public AndroidOfflineTtsPlatformVoice(
            string voiceId,
            string displayName,
            string languageTag,
            SpeechNetworkRequirement networkRequirement,
            bool isInstalled)
        {
            if (!Enum.IsDefined(typeof(SpeechNetworkRequirement), networkRequirement))
            {
                throw new ArgumentOutOfRangeException(nameof(networkRequirement));
            }

            VoiceId = SpeechProviderDescriptor.RequireText(voiceId, nameof(voiceId));
            DisplayName = SpeechProviderDescriptor.RequireText(
                displayName,
                nameof(displayName));
            LanguageTag = SpeechProviderDescriptor.RequireText(
                languageTag,
                nameof(languageTag));
            NetworkRequirement = networkRequirement;
            IsInstalled = isInstalled;
        }

        public string VoiceId { get; }
        public string DisplayName { get; }
        public string LanguageTag { get; }
        public SpeechNetworkRequirement NetworkRequirement { get; }
        public bool IsInstalled { get; }
    }

    public sealed class AndroidOfflineTtsProbe
    {
        public AndroidOfflineTtsProbe(
            int apiLevel,
            bool engineInitialized,
            AndroidOfflineTtsLanguageStatus languageStatus,
            int matchingOfflineVoiceCount,
            int installedOfflineVoiceCount,
            int matchingNetworkVoiceCount,
            int maximumInputCharacters,
            string diagnostic)
        {
            if (apiLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(apiLevel));
            }
            if (!Enum.IsDefined(typeof(AndroidOfflineTtsLanguageStatus), languageStatus))
            {
                throw new ArgumentOutOfRangeException(nameof(languageStatus));
            }
            if (matchingOfflineVoiceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(matchingOfflineVoiceCount));
            }
            if (installedOfflineVoiceCount < 0 ||
                installedOfflineVoiceCount > matchingOfflineVoiceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(installedOfflineVoiceCount));
            }
            if (matchingNetworkVoiceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(matchingNetworkVoiceCount));
            }
            if (maximumInputCharacters <= 0 || maximumInputCharacters > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumInputCharacters));
            }

            ApiLevel = apiLevel;
            EngineInitialized = engineInitialized;
            LanguageStatus = languageStatus;
            MatchingOfflineVoiceCount = matchingOfflineVoiceCount;
            InstalledOfflineVoiceCount = installedOfflineVoiceCount;
            MatchingNetworkVoiceCount = matchingNetworkVoiceCount;
            MaximumInputCharacters = maximumInputCharacters;
            Diagnostic = BoundDiagnostic(diagnostic);
        }

        public int ApiLevel { get; }
        public bool EngineInitialized { get; }
        public AndroidOfflineTtsLanguageStatus LanguageStatus { get; }
        public int MatchingOfflineVoiceCount { get; }
        public int InstalledOfflineVoiceCount { get; }
        public int MatchingNetworkVoiceCount { get; }
        public int MaximumInputCharacters { get; }
        public string Diagnostic { get; }

        private static string BoundDiagnostic(string value)
        {
            string text = SpeechProviderDescriptor.RequireText(value, nameof(value));
            return text.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? text
                : text.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
        }
    }

    public sealed class AndroidOfflineTtsPlatformFailure
    {
        public AndroidOfflineTtsPlatformFailure(
            AndroidOfflineTtsFailureKind kind,
            string code,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(AndroidOfflineTtsFailureKind), kind))
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

        public AndroidOfflineTtsFailureKind Kind { get; }
        public string Code { get; }
        public string Diagnostic { get; }

        private static string Bound(string value, int maximumCharacters) =>
            value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
    }

    public sealed class AndroidOfflineTtsPlatformEvent
    {
        public AndroidOfflineTtsPlatformEvent(
            string requestId,
            AndroidOfflineTtsPlatformEventKind kind,
            AndroidOfflineTtsPlatformFailure? failure = null)
        {
            if (!Enum.IsDefined(typeof(AndroidOfflineTtsPlatformEventKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if ((kind == AndroidOfflineTtsPlatformEventKind.Failed) != (failure != null))
            {
                throw new ArgumentException(
                    "Only Android offline TTS failed events carry platform failure detail.",
                    nameof(failure));
            }

            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            Kind = kind;
            Failure = failure;
        }

        public string RequestId { get; }
        public AndroidOfflineTtsPlatformEventKind Kind { get; }
        public AndroidOfflineTtsPlatformFailure? Failure { get; }
        public bool IsTerminal =>
            Kind == AndroidOfflineTtsPlatformEventKind.Completed ||
            Kind == AndroidOfflineTtsPlatformEventKind.Cancelled ||
            Kind == AndroidOfflineTtsPlatformEventKind.Failed;
    }

    public interface IAndroidOfflineTtsPlatform : IAsyncDisposable
    {
        ValueTask<AndroidOfflineTtsProbe> ProbeAsync(
            string languageTag,
            CancellationToken cancellationToken);

        ValueTask<IReadOnlyList<AndroidOfflineTtsPlatformVoice>> GetVoicesAsync(
            string languageTag,
            CancellationToken cancellationToken);

        IAsyncEnumerable<AndroidOfflineTtsPlatformEvent> SpeakAsync(
            string requestId,
            string text,
            string languageTag,
            string voiceId,
            CancellationToken cancellationToken);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum AndroidSystemTtsPlatformEventKind
    {
        Started = 0,
        Completed = 1,
        Cancelled = 2,
        Failed = 3,
    }

    public enum AndroidSystemTtsFailureKind
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

    public sealed class AndroidSystemTtsPlatformVoice
    {
        public AndroidSystemTtsPlatformVoice(
            string voiceId,
            string displayName,
            string languageTag,
            SpeechNetworkRequirement networkRequirement,
            bool isInstalled)
        {
            if (networkRequirement != SpeechNetworkRequirement.None &&
                networkRequirement != SpeechNetworkRequirement.Required)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(networkRequirement),
                    "Android TTS voices must declare networking as either none or required.");
            }

            VoiceId = SpeechProviderDescriptor.RequireText(voiceId, nameof(voiceId));
            DisplayName = SpeechProviderDescriptor.RequireText(displayName, nameof(displayName));
            LanguageTag = SpeechProviderDescriptor.RequireText(languageTag, nameof(languageTag));
            NetworkRequirement = networkRequirement;
            IsInstalled = isInstalled;
        }

        public string VoiceId { get; }
        public string DisplayName { get; }
        public string LanguageTag { get; }
        public SpeechNetworkRequirement NetworkRequirement { get; }
        public bool IsInstalled { get; }
        public bool RequiresNetwork =>
            NetworkRequirement == SpeechNetworkRequirement.Required;
    }

    public sealed class AndroidSystemTtsProbe
    {
        public AndroidSystemTtsProbe(
            int apiLevel,
            bool engineInitialized,
            int matchingVoiceCount,
            int matchingOfflineVoiceCount,
            int matchingNetworkVoiceCount,
            int maximumInputCharacters,
            string diagnostic)
        {
            if (apiLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(apiLevel));
            }
            if (matchingVoiceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(matchingVoiceCount));
            }
            if (matchingOfflineVoiceCount < 0 || matchingNetworkVoiceCount < 0 ||
                matchingOfflineVoiceCount + matchingNetworkVoiceCount != matchingVoiceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(matchingVoiceCount),
                    "Android system TTS voice counts must be non-negative and internally consistent.");
            }
            if (maximumInputCharacters <= 0 || maximumInputCharacters > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumInputCharacters));
            }

            ApiLevel = apiLevel;
            EngineInitialized = engineInitialized;
            MatchingVoiceCount = matchingVoiceCount;
            MatchingOfflineVoiceCount = matchingOfflineVoiceCount;
            MatchingNetworkVoiceCount = matchingNetworkVoiceCount;
            MaximumInputCharacters = maximumInputCharacters;
            Diagnostic = BoundDiagnostic(diagnostic);
        }

        public int ApiLevel { get; }
        public bool EngineInitialized { get; }
        public int MatchingVoiceCount { get; }
        public int MatchingOfflineVoiceCount { get; }
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

    public sealed class AndroidSystemTtsPlatformFailure
    {
        public AndroidSystemTtsPlatformFailure(
            AndroidSystemTtsFailureKind kind,
            string code,
            string diagnostic)
        {
            if (!Enum.IsDefined(typeof(AndroidSystemTtsFailureKind), kind))
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

        public AndroidSystemTtsFailureKind Kind { get; }
        public string Code { get; }
        public string Diagnostic { get; }

        private static string Bound(string value, int maximumCharacters) =>
            value.Length <= maximumCharacters
                ? value
                : value.Substring(0, maximumCharacters);
    }

    public sealed class AndroidSystemTtsPlatformEvent
    {
        public AndroidSystemTtsPlatformEvent(
            string requestId,
            AndroidSystemTtsPlatformEventKind kind,
            AndroidSystemTtsPlatformFailure? failure = null)
        {
            if (!Enum.IsDefined(typeof(AndroidSystemTtsPlatformEventKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if ((kind == AndroidSystemTtsPlatformEventKind.Failed) != (failure != null))
            {
                throw new ArgumentException(
                    "Only Android system TTS failed events carry platform failure detail.",
                    nameof(failure));
            }

            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            Kind = kind;
            Failure = failure;
        }

        public string RequestId { get; }
        public AndroidSystemTtsPlatformEventKind Kind { get; }
        public AndroidSystemTtsPlatformFailure? Failure { get; }
        public bool IsTerminal =>
            Kind == AndroidSystemTtsPlatformEventKind.Completed ||
            Kind == AndroidSystemTtsPlatformEventKind.Cancelled ||
            Kind == AndroidSystemTtsPlatformEventKind.Failed;
    }

    public interface IAndroidSystemTtsPlatform : IAsyncDisposable
    {
        ValueTask<AndroidSystemTtsProbe> ProbeAsync(
            string languageTag,
            CancellationToken cancellationToken);

        ValueTask<IReadOnlyList<AndroidSystemTtsPlatformVoice>> GetVoicesAsync(
            string languageTag,
            CancellationToken cancellationToken);

        IAsyncEnumerable<AndroidSystemTtsPlatformEvent> SpeakAsync(
            string requestId,
            string text,
            string languageTag,
            string voiceId,
            bool networkVoiceApproved,
            CancellationToken cancellationToken);
    }
}

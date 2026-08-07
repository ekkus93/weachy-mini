#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum AsrEventKind
    {
        Started = 0,
        PartialResult = 1,
        FinalResult = 2,
        NoMatch = 3,
        Cancelled = 4,
        Failed = 5,
    }

    public sealed class AsrCapabilities
    {
        private readonly ReadOnlyCollection<string> supportedLanguageTags;

        public AsrCapabilities(
            IEnumerable<string> supportedLanguageTags,
            bool supportsPartialResults,
            bool supportsCancellation,
            TimeSpan maximumUtteranceDuration)
        {
            this.supportedLanguageTags = CopyLanguageTags(
                supportedLanguageTags,
                nameof(supportedLanguageTags));
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "RMA-120 ASR providers must support cancellation.",
                    nameof(supportsCancellation));
            }
            if (maximumUtteranceDuration <= TimeSpan.Zero ||
                maximumUtteranceDuration > TimeSpan.FromHours(1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumUtteranceDuration));
            }

            SupportsPartialResults = supportsPartialResults;
            SupportsCancellation = supportsCancellation;
            MaximumUtteranceDuration = maximumUtteranceDuration;
        }

        public IReadOnlyList<string> SupportedLanguageTags => supportedLanguageTags;
        public bool SupportsPartialResults { get; }
        public bool SupportsCancellation { get; }
        public TimeSpan MaximumUtteranceDuration { get; }

        private static ReadOnlyCollection<string> CopyLanguageTags(
            IEnumerable<string> values,
            string name)
        {
            if (values == null)
            {
                throw new ArgumentNullException(name);
            }

            var result = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in values)
            {
                string languageTag = SpeechProviderDescriptor.RequireText(value, name);
                if (!unique.Add(languageTag))
                {
                    throw new ArgumentException(
                        "Speech language capability tags must be unique.",
                        name);
                }

                result.Add(languageTag);
            }

            if (result.Count == 0)
            {
                throw new ArgumentException(
                    "An ASR provider must declare at least one supported language.",
                    name);
            }

            return new ReadOnlyCollection<string>(result);
        }
    }

    public sealed class AsrOptions
    {
        public AsrOptions(string languageTag, bool requestPartialResults)
        {
            LanguageTag = SpeechProviderDescriptor.RequireText(
                languageTag,
                nameof(languageTag));
            RequestPartialResults = requestPartialResults;
        }

        public string LanguageTag { get; }
        public bool RequestPartialResults { get; }
    }

    public sealed class AsrRequest
    {
        public AsrRequest(SpeechOperationContext context, AsrOptions options)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            if (context.ProviderKind != SpeechProviderKind.AutomaticSpeechRecognition)
            {
                throw new ArgumentException(
                    "ASR requests require an ASR provider selection.",
                    nameof(context));
            }
        }

        public SpeechOperationContext Context { get; }
        public AsrOptions Options { get; }
    }

    public sealed class AsrEvent
    {
        public AsrEvent(
            string providerInstanceId,
            string requestId,
            ulong sequence,
            AsrEventKind kind,
            string? transcript = null,
            SpeechProviderError? error = null)
        {
            if (!Enum.IsDefined(typeof(AsrEventKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (sequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            bool hasTranscript = !string.IsNullOrWhiteSpace(transcript);
            bool transcriptEvent = kind == AsrEventKind.PartialResult ||
                kind == AsrEventKind.FinalResult;
            if (transcriptEvent != hasTranscript)
            {
                throw new ArgumentException(
                    "Only ASR partial/final result events carry non-empty transcript text.",
                    nameof(transcript));
            }
            if ((kind == AsrEventKind.Failed) != (error != null))
            {
                throw new ArgumentException(
                    "Only failed ASR events carry a structured error.",
                    nameof(error));
            }

            ProviderInstanceId = SpeechProviderDescriptor.RequireText(
                providerInstanceId,
                nameof(providerInstanceId));
            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            Sequence = sequence;
            Kind = kind;
            Transcript = transcript;
            Error = error;
        }

        public string ProviderInstanceId { get; }
        public string RequestId { get; }
        public ulong Sequence { get; }
        public AsrEventKind Kind { get; }
        public string? Transcript { get; }
        public SpeechProviderError? Error { get; }
    }

    public interface IAsrProvider : IAsyncDisposable
    {
        SpeechProviderDescriptor Descriptor { get; }
        AsrCapabilities Capabilities { get; }

        ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            AsrOptions options,
            CancellationToken cancellationToken);

        IAsyncEnumerable<AsrEvent> RecognizeAsync(
            AsrRequest request,
            CancellationToken cancellationToken);
    }
}

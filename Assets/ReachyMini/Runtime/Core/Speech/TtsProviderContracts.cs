#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Speech
{
    public enum TtsEventKind
    {
        Started = 0,
        Completed = 1,
        Cancelled = 2,
        Failed = 3,
    }

    public sealed class TtsVoice
    {
        public TtsVoice(
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
        public bool MayUseNetwork => NetworkRequirement != SpeechNetworkRequirement.None;
    }

    public sealed class TtsCapabilities
    {
        public TtsCapabilities(bool supportsCancellation, int maximumInputCharacters)
        {
            if (!supportsCancellation)
            {
                throw new ArgumentException(
                    "RMA-120 TTS providers must support cancellation.",
                    nameof(supportsCancellation));
            }
            if (maximumInputCharacters <= 0 || maximumInputCharacters > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumInputCharacters));
            }

            SupportsCancellation = supportsCancellation;
            MaximumInputCharacters = maximumInputCharacters;
        }

        public bool SupportsCancellation { get; }
        public int MaximumInputCharacters { get; }
    }

    public sealed class TtsRequest
    {
        public TtsRequest(
            SpeechOperationContext context,
            string text,
            string voiceId)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            if (context.ProviderKind != SpeechProviderKind.TextToSpeech)
            {
                throw new ArgumentException(
                    "TTS requests require a TTS provider selection.",
                    nameof(context));
            }

            Text = SpeechProviderDescriptor.RequireText(text, nameof(text));
            VoiceId = SpeechProviderDescriptor.RequireText(voiceId, nameof(voiceId));
        }

        public SpeechOperationContext Context { get; }
        public string Text { get; }
        public string VoiceId { get; }
    }

    public sealed class TtsEvent
    {
        public TtsEvent(
            string providerInstanceId,
            string requestId,
            ulong sequence,
            TtsEventKind kind,
            SpeechProviderError? error = null)
        {
            if (!Enum.IsDefined(typeof(TtsEventKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (sequence == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }
            if ((kind == TtsEventKind.Failed) != (error != null))
            {
                throw new ArgumentException(
                    "Only failed TTS events carry a structured error.",
                    nameof(error));
            }

            ProviderInstanceId = SpeechProviderDescriptor.RequireText(
                providerInstanceId,
                nameof(providerInstanceId));
            RequestId = SpeechProviderDescriptor.RequireText(requestId, nameof(requestId));
            Sequence = sequence;
            Kind = kind;
            Error = error;
        }

        public string ProviderInstanceId { get; }
        public string RequestId { get; }
        public ulong Sequence { get; }
        public TtsEventKind Kind { get; }
        public SpeechProviderError? Error { get; }
    }

    public interface ITtsProvider : IAsyncDisposable
    {
        SpeechProviderDescriptor Descriptor { get; }
        TtsCapabilities Capabilities { get; }

        ValueTask<SpeechProviderAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken);

        ValueTask<IReadOnlyList<TtsVoice>> GetVoicesAsync(
            CancellationToken cancellationToken);

        IAsyncEnumerable<TtsEvent> SpeakAsync(
            TtsRequest request,
            CancellationToken cancellationToken);
    }
}

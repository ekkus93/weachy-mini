#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Conversation;
using ReachyMini.Speech;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal sealed partial class ReachyConversationTurnOrchestrator
    {
        private const string TtsLanguageTag = "en-US";
        private static readonly TimeSpan TtsTimeout = TimeSpan.FromSeconds(60.0);

        private readonly struct TtsTurnResult
        {
            private TtsTurnResult(bool succeeded, string detail)
            {
                Succeeded = succeeded;
                Detail = detail;
            }

            public bool Succeeded { get; }

            public string Detail { get; }

            public static TtsTurnResult Success() => new TtsTurnResult(true, string.Empty);

            public static TtsTurnResult Failure(string detail) => new TtsTurnResult(false, detail);
        }

        // The provider is the TTS leg of the same per-turn
        // ReachyAndroidSpeechAudioStack used for ASR. Its
        // AudioCoordinatedTtsProvider shares the exact audio-focus
        // coordinator with listening, so speaking cannot acquire focus until
        // the ASR terminal event has been exposed after listening release.
        private async Task<TtsTurnResult> RunTtsAsync(
            ITtsProvider provider,
            string speech,
            ReachyConversationOperation preparingSpeech)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (Application.platform != RuntimePlatform.Android)
            {
                return TtsTurnResult.Failure("Speech playback requires an Android device.");
            }

            SpeechProviderAvailability availability = await provider
                .CheckAvailabilityAsync(preparingSpeech.CancellationToken)
                .ConfigureAwait(true);
            if (!availability.IsAvailable)
            {
                return TtsTurnResult.Failure(availability.Diagnostic);
            }

            IReadOnlyList<TtsVoice> voices = await provider
                .GetVoicesAsync(preparingSpeech.CancellationToken)
                .ConfigureAwait(true);
            TtsVoice? voice = ReachyTtsVoiceSelectionPolicy.SelectExactOfflineVoice(
                voices,
                TtsLanguageTag);
            if (voice == null)
            {
                return TtsTurnResult.Failure(
                    "No installed exact-locale offline text-to-speech voice is available.");
            }

            ReachyConversationOperation speaking =
                conversation.MarkSpeechPrepared(preparingSpeech);
            stateStore.SetInteraction(ReachyInteractionState.Speaking, "Speaking.");

            SpeechProviderSelection selection = new SpeechProviderSelection(
                provider.Descriptor);
            SpeechOperationContext context = new SpeechOperationContext(
                NextRequestId("tts"),
                selection.Current,
                TtsTimeout);
            TtsRequest request = new TtsRequest(context, speech, voice.VoiceId);

            // AudioCoordinatedTtsProvider withholds Completed until the
            // speaking focus lease has been released.
            await foreach (TtsEvent ttsEvent in provider.SpeakAsync(
                request,
                speaking.CancellationToken))
            {
                switch (ttsEvent.Kind)
                {
                    case TtsEventKind.Completed:
                        conversation.MarkSpeechComplete(speaking);
                        return TtsTurnResult.Success();
                    case TtsEventKind.Cancelled:
                        return TtsTurnResult.Failure("Speech playback was cancelled.");
                    case TtsEventKind.Failed:
                        return TtsTurnResult.Failure(
                            ttsEvent.Error?.Diagnostic ?? "Speech playback failed.");
                    default:
                        continue;
                }
            }

            return TtsTurnResult.Failure(
                "Speech playback ended without completing.");
        }
    }
}

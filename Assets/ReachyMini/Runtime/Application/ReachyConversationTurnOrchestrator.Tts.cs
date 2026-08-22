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

        // Fully self-contained, mirroring RunAsrAsync: resolves, uses, and
        // disposes its own TTS provider instance for this one turn.
        // Transitions the conversation state machine from PreparingSpeech
        // (voice selection) into Speaking (playback) internally, matching
        // how RunAsrAsync transitions Listening -> Transcribing internally.
        private async Task<TtsTurnResult> RunTtsAsync(
            string speech,
            ReachyConversationOperation preparingSpeech)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return TtsTurnResult.Failure("Speech playback requires an Android device.");
            }

            ITtsProvider? provider = await ResolveTtsProviderAsync(
                    preparingSpeech.CancellationToken)
                .ConfigureAwait(true);
            if (provider == null)
            {
                return TtsTurnResult.Failure("No usable text-to-speech provider is available.");
            }

            try
            {
                IReadOnlyList<TtsVoice> voices = await provider
                    .GetVoicesAsync(preparingSpeech.CancellationToken)
                    .ConfigureAwait(true);
                TtsVoice? voice = SelectVoice(voices);
                if (voice == null)
                {
                    return TtsTurnResult.Failure(
                        "No text-to-speech voice is available on this device.");
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

                // See RunAsrAsync's comment: no explicit .ConfigureAwait()
                // on this async-enumerable iteration -- plain `await
                // foreach` already defaults to continuing on the captured
                // (Unity main-thread) context.
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

                return TtsTurnResult.Failure("Speech playback ended without completing.");
            }
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static async Task<ITtsProvider?> ResolveTtsProviderAsync(
            CancellationToken cancellationToken)
        {
            ITtsProvider offline = ReachyAndroidOfflineTtsProviderFactory.Create(
                "conversation-turn-offline-tts", TtsLanguageTag);
            SpeechProviderAvailability availability = await offline
                .CheckAvailabilityAsync(cancellationToken)
                .ConfigureAwait(true);
            if (availability.IsAvailable)
            {
                return offline;
            }
            await offline.DisposeAsync().ConfigureAwait(true);

            return null;
        }

        private static TtsVoice? SelectVoice(IReadOnlyList<TtsVoice> voices)
        {
            if (voices.Count == 0)
            {
                return null;
            }

            for (int index = 0; index < voices.Count; ++index)
            {
                if (string.Equals(
                        voices[index].LanguageTag,
                        TtsLanguageTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return voices[index];
                }
            }

            return voices[0];
        }
    }
}

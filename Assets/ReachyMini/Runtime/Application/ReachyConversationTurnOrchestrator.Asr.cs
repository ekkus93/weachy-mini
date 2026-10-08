#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Conversation;
using ReachyMini.Speech;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal sealed partial class ReachyConversationTurnOrchestrator
    {
        private const string AsrLanguageTag = "en-US";
        private static readonly TimeSpan AsrTimeout = TimeSpan.FromSeconds(30.0);

        private readonly struct AsrTurnResult
        {
            private AsrTurnResult(
                bool succeeded,
                string transcript,
                string detail,
                ReachyConversationOperation? transcribing)
            {
                Succeeded = succeeded;
                Transcript = transcript;
                Detail = detail;
                Transcribing = transcribing;
            }

            public bool Succeeded { get; }

            public string Transcript { get; }

            public string Detail { get; }

            // Non-null exactly when the ASR stream produced at least one
            // event and the orchestrator transitioned the conversation state
            // machine past Listening into Transcribing.
            public ReachyConversationOperation? Transcribing { get; }

            public static AsrTurnResult Success(
                string transcript,
                ReachyConversationOperation transcribing) =>
                new AsrTurnResult(true, transcript, string.Empty, transcribing);

            public static AsrTurnResult Failure(
                string detail,
                ReachyConversationOperation? transcribing) =>
                new AsrTurnResult(false, string.Empty, detail, transcribing);
        }

        // The provider is the ASR leg of one per-turn
        // ReachyAndroidSpeechAudioStack. The stack wraps the explicit
        // on-device recognizer in AudioCoordinatedAsrProvider and shares one
        // SpeechAudioFocusCoordinator with the TTS leg. No system/network ASR
        // fallback is resolved here.
        private async Task<AsrTurnResult> RunAsrAsync(
            IAsrProvider provider,
            ReachyConversationOperation listening,
            CancellationToken cancellationToken)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (Application.platform != RuntimePlatform.Android)
            {
                return AsrTurnResult.Failure(
                    "Speech recognition requires an Android device.",
                    null);
            }

            AsrOptions options = new AsrOptions(
                AsrLanguageTag,
                requestPartialResults: false);
            SpeechProviderAvailability availability = await provider
                .CheckAvailabilityAsync(options, cancellationToken)
                .ConfigureAwait(true);
            if (!availability.IsAvailable)
            {
                return AsrTurnResult.Failure(
                    availability.Diagnostic,
                    null);
            }

            SpeechProviderSelection selection = new SpeechProviderSelection(
                provider.Descriptor);
            SpeechOperationContext context = new SpeechOperationContext(
                NextRequestId("asr"),
                selection.Current,
                AsrTimeout);
            AsrRequest request = new AsrRequest(context, options);

            // Plain await foreach preserves Unity's captured synchronization
            // context. AudioCoordinatedAsrProvider withholds its terminal
            // event until the listening focus lease has been released.
            ReachyConversationOperation? transcribing = null;
            await foreach (AsrEvent asrEvent in provider.RecognizeAsync(
                request,
                cancellationToken))
            {
                if (transcribing == null)
                {
                    transcribing = conversation.MarkListeningComplete(listening);
                    stateStore.SetInteraction(
                        ReachyInteractionState.Transcribing,
                        "Transcribing speech.");
                }

                switch (asrEvent.Kind)
                {
                    case AsrEventKind.FinalResult:
                        return AsrTurnResult.Success(
                            asrEvent.Transcript ?? string.Empty,
                            transcribing!);
                    case AsrEventKind.NoMatch:
                        return AsrTurnResult.Failure(
                            "No speech was recognized.",
                            transcribing);
                    case AsrEventKind.Cancelled:
                        return AsrTurnResult.Failure(
                            "Speech recognition was cancelled.",
                            transcribing);
                    case AsrEventKind.Failed:
                        return AsrTurnResult.Failure(
                            asrEvent.Error?.Diagnostic ?? "Speech recognition failed.",
                            transcribing);
                    default:
                        continue;
                }
            }

            return AsrTurnResult.Failure(
                "Speech recognition ended without a final result.",
                transcribing);
        }
    }
}

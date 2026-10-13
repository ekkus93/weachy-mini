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
                string failureCode,
                ReachyConversationOperation? transcribing)
            {
                Succeeded = succeeded;
                Transcript = transcript;
                Detail = detail;
                FailureCode = failureCode;
                Transcribing = transcribing;
            }

            public bool Succeeded { get; }

            public string Transcript { get; }

            public string Detail { get; }

            public string FailureCode { get; }

            // Non-null after a partial or final speech result advances the
            // conversation state machine past Listening into Transcribing.
            public ReachyConversationOperation? Transcribing { get; }

            public static AsrTurnResult Success(
                string transcript,
                ReachyConversationOperation transcribing) =>
                new AsrTurnResult(
                    true,
                    transcript,
                    string.Empty,
                    string.Empty,
                    transcribing);

            public static AsrTurnResult Failure(
                string failureCode,
                string detail,
                ReachyConversationOperation? transcribing) =>
                new AsrTurnResult(
                    false,
                    string.Empty,
                    detail,
                    failureCode,
                    transcribing);
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
                    "android_on_device_asr_platform_unavailable",
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
                    "android_on_device_asr_availability_" +
                        availability.State.ToString().ToLowerInvariant(),
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

            var progress = new ReachyConversationAsrProgress(
                conversation,
                listening);

            // Plain await foreach preserves Unity's captured synchronization
            // context. AudioCoordinatedAsrProvider withholds its terminal
            // event until the listening focus lease has been released.
            ReachyConversationOperation? transcribing = null;
            await foreach (AsrEvent asrEvent in provider.RecognizeAsync(
                request,
                cancellationToken))
            {
                bool enteredTranscribing = progress.Observe(asrEvent.Kind);
                ObservedRecognizerReady = progress.RecognizerReady;
                transcribing = progress.Transcribing;
                if (enteredTranscribing)
                {
                    stateStore.SetInteraction(
                        ReachyInteractionState.Transcribing,
                        "Transcribing speech.");
                }

                switch (asrEvent.Kind)
                {
                    case AsrEventKind.Started:
                        break;
                    case AsrEventKind.PartialResult:
                        break;
                    case AsrEventKind.FinalResult:
                        return AsrTurnResult.Success(
                            asrEvent.Transcript ?? string.Empty,
                            transcribing!);
                    case AsrEventKind.NoMatch:
                        return AsrTurnResult.Failure(
                            "android_on_device_asr_no_match",
                            "No speech was recognized.",
                            transcribing);
                    case AsrEventKind.Cancelled:
                        return AsrTurnResult.Failure(
                            "android_on_device_asr_cancelled",
                            "Speech recognition was cancelled.",
                            transcribing);
                    case AsrEventKind.Failed:
                        return AsrTurnResult.Failure(
                            asrEvent.Error?.Code ??
                                "android_on_device_asr_failed",
                            asrEvent.Error?.Diagnostic ?? "Speech recognition failed.",
                            transcribing);
                    default:
                        continue;
                }
            }

            return AsrTurnResult.Failure(
                "android_on_device_asr_stream_ended",
                "Speech recognition ended without a final result.",
                transcribing);
        }

    }
}

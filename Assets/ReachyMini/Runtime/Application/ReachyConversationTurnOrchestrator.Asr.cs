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

        // Fully self-contained: resolves, uses, and disposes its own ASR
        // provider instance for this one turn. Mirrors
        // ReachyAndroidSpeechCapabilityApplicationService's probe pattern
        // (on-device first, system ASR as a fallback with broader API-level
        // coverage), but performs a real recognition instead of only an
        // availability check.
        private async Task<AsrTurnResult> RunAsrAsync(
            ReachyConversationOperation listening,
            CancellationToken cancellationToken)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return AsrTurnResult.Failure(
                    "Speech recognition requires an Android device.",
                    null);
            }

            IAsrProvider? provider =
                await ResolveAsrProviderAsync(cancellationToken).ConfigureAwait(true);
            if (provider == null)
            {
                return AsrTurnResult.Failure(
                    "No usable on-device or system speech-recognition provider is available.",
                    null);
            }

            try
            {
                SpeechProviderSelection selection = new SpeechProviderSelection(
                    provider.Descriptor);
                SpeechOperationContext context = new SpeechOperationContext(
                    NextRequestId("asr"),
                    selection.Current,
                    AsrTimeout);
                AsrRequest request = new AsrRequest(
                    context,
                    new AsrOptions(AsrLanguageTag, requestPartialResults: false));

                // No .ConfigureAwait() call on this async-enumerable
                // iteration: plain `await foreach` already defaults to
                // continuing on the captured context (the Unity main-thread
                // SynchronizationContext), the same behavior
                // .ConfigureAwait(true) would request explicitly -- kept
                // implicit here since IAsyncEnumerable<T>.ConfigureAwait is
                // an extension method whose availability under Unity's API
                // compatibility profile could not be verified locally.
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
                            // transcribing is provably non-null here: this
                            // switch only runs after the if-block above has
                            // either just set it or found it already set.
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
            finally
            {
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static async Task<IAsrProvider?> ResolveAsrProviderAsync(
            CancellationToken cancellationToken)
        {
            AsrOptions probeOptions = new AsrOptions(AsrLanguageTag, requestPartialResults: false);

            IAsrProvider onDevice = ReachyAndroidOnDeviceAsrProviderFactory.Create(
                "conversation-turn-on-device-asr", AsrLanguageTag, AsrTimeout);
            SpeechProviderAvailability onDeviceAvailability = await onDevice
                .CheckAvailabilityAsync(probeOptions, cancellationToken)
                .ConfigureAwait(true);
            if (onDeviceAvailability.IsAvailable)
            {
                return onDevice;
            }
            await onDevice.DisposeAsync().ConfigureAwait(true);

            IAsrProvider systemAsr = ReachyAndroidSystemAsrProviderFactory.Create(
                "conversation-turn-system-asr", AsrLanguageTag, AsrTimeout);
            SpeechProviderAvailability systemAvailability = await systemAsr
                .CheckAvailabilityAsync(probeOptions, cancellationToken)
                .ConfigureAwait(true);
            if (systemAvailability.IsAvailable)
            {
                return systemAsr;
            }
            await systemAsr.DisposeAsync().ConfigureAwait(true);

            return null;
        }
    }
}

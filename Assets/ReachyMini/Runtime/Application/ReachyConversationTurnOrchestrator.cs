#nullable enable

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.Conversation;
using ReachyMini.Providers;
using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 (voice+visual grounding, 2026-08-22): the first real, live
    // conversational-turn orchestrator. Wires together the already-built but
    // previously-unwired pieces: real ASR (ReachyAndroidOnDeviceAsrPlatform /
    // ReachyAndroidSystemAsrPlatform), a best-effort camera-frame VLM scene
    // description (ReachyAndroidPerceptionApplicationService.CloudVlm.cs),
    // the local/cloud LLM behavior-intent generation
    // (ReachyLocalLlmProviderApplicationService), RMA-151 behavior-intent
    // validation/recovery (ReachyBehaviorIntentJsonParser /
    // ReachyBehaviorIntentRecoveryPolicy), the RMA-152/153 baseline-behavior
    // gesture vocabulary (IReachyBehaviorService.TryTriggerGesture), and real
    // TTS playback (ReachyAndroidOfflineTtsPlatform). Turn/session/operation
    // bookkeeping is delegated to ReachyConversationStateMachine (RMA-150),
    // which is exactly designed for this: guarding against a second mic
    // press starting a new turn while an old turn's ASR/LLM/TTS callback is
    // still in flight.
    //
    // Deliberately a plain internal coordinator (not a MonoBehaviour, not a
    // ReachyApplicationServiceBase) owned and disposed by
    // ReachySettingsMainScreenApplicationService, matching this codebase's
    // existing coordinator convention (see
    // ReachyDiagnosticBundleExportCoordinator in the same file).
    internal sealed partial class ReachyConversationTurnOrchestrator : IDisposable
    {
        // A best-effort scene description is folded into the LLM prompt, not
        // sent to the model verbatim -- bounded here (independent of the
        // cloud VLM's own CloudVlmMaximumResponseCharacters=8192, which is
        // sized for the VLM response transport, not for what belongs inline
        // in an LLM chat message subject to
        // ReachyLlmProviderPolicy.MaximumMessageCharacters /
        // LocalLlmProviderPolicy.MaximumMessageCharacters = 4096) so a long
        // scene description plus a long transcript can never together
        // exceed either provider's per-message bound.
        private const int MaximumSceneDescriptionCharactersInPrompt = 300;
        private const int MaximumPromptCharacters = 3900;
        private const string VlmScenePrompt =
            "Briefly describe what the camera currently sees in one or two sentences.";

        private readonly ReachyMainScreenStateStore stateStore;
        private readonly ReachySettingsPersistenceApplicationService persistence;
        private readonly IReachyBehaviorService behavior;
        private readonly ILocalLlmProviderCapability? localLlm;
        private readonly ICloudLlmProviderCapability? cloudLlm;
        private readonly ReachyAndroidPerceptionApplicationService? perceptionScene;
        private readonly ReachyConversationStateMachine conversation =
            new ReachyConversationStateMachine();
        // Interlocked.Increment has no ulong overload (only int/long), so
        // this is a long -- realistic turn counts never approach negative
        // wraparound, and the value is only ever used as a positive-looking
        // numeric suffix in NextRequestId, never compared or persisted.
        private long requestSequence;
        private int disposed;

        internal ReachyConversationTurnOrchestrator(
            ReachyMainScreenStateStore stateStore,
            ReachySettingsPersistenceApplicationService persistence,
            IReachyProviderService provider,
            IReachyPerceptionService perception,
            IReachyBehaviorService behavior)
        {
            this.stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            this.persistence = persistence ??
                throw new ArgumentNullException(nameof(persistence));
            this.behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
            IReachyProviderService resolvedProvider = provider ??
                throw new ArgumentNullException(nameof(provider));
            // Mirrors the existing `resolvedProvider as
            // IReachyProviderGovernorDiagnosticsSource` pattern
            // (ReachyProductionApplicationCompositionProvider.cs): the
            // production concrete type
            // (ReachyLocalLlmProviderApplicationService) implements both
            // capabilities directly, but the interface only promises
            // IReachyProviderService, so both are optional as-castable
            // facets.
            localLlm = resolvedProvider as ILocalLlmProviderCapability;
            cloudLlm = resolvedProvider as ICloudLlmProviderCapability;
            IReachyPerceptionService resolvedPerception = perception ??
                throw new ArgumentNullException(nameof(perception));
            perceptionScene = resolvedPerception as ReachyAndroidPerceptionApplicationService;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            // Cancels and disposes any in-flight operation's
            // CancellationTokenSource -- every await in RunTurnAsync (and
            // the Asr/Llm/Tts helpers) observes a token sourced from the
            // conversation state machine, so this is sufficient to unwind an
            // in-flight turn without a separate turn-level
            // CancellationTokenSource.
            conversation.Dispose();
        }

        // Bound to ReachyMainScreen.ConfigureConversationTurn and invoked
        // fire-and-forget from RequestMicrophone() (a synchronous IMGUI
        // button-click handler). Never throws synchronously to its caller:
        // every failure path funnels through FailTurnClosed, which only
        // touches state stores, never re-throws.
        public async Task StartTurnAsync()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            string requestId = NextRequestId("turn");
            ReachyConversationOperation listening;
            try
            {
                listening = conversation.BeginListening();
            }
            catch (InvalidOperationException)
            {
                // A turn is already in flight (e.g. a second mic press while
                // ASR/LLM/TTS from a prior press is still running). Ignore
                // the new press rather than corrupting the in-flight turn's
                // bookkeeping -- the state machine is the single source of
                // truth for "is a turn active", exactly per its design
                // intent of guarding stale/overlapping async completions.
                return;
            }

            try
            {
                await RunTurnAsync(requestId, listening).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                FailTurnClosed(
                    "turn-unhandled-exception",
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private async Task RunTurnAsync(string requestId, ReachyConversationOperation listening)
        {
            AsrTurnResult asrResult = await RunAsrAsync(listening, listening.CancellationToken)
                .ConfigureAwait(true);
            if (!asrResult.Succeeded || string.IsNullOrWhiteSpace(asrResult.Transcript))
            {
                FailTurnClosed("asr-failed", asrResult.Detail);
                return;
            }

            ReachyConversationOperation transcribing = asrResult.Transcribing ??
                throw new InvalidOperationException(
                    "ASR reported success without transitioning past Listening.");
            ReachyConversationOperation thinking =
                conversation.MarkTranscriptionComplete(transcribing);
            stateStore.SetInteraction(
                ReachyInteractionState.Thinking,
                "Thinking about a response.");

            string? sceneDescription = await TryDescribeSceneAsync(
                    requestId,
                    thinking.CancellationToken)
                .ConfigureAwait(true);
            string prompt = BuildPrompt(asrResult.Transcript, sceneDescription);

            LlmTurnResult llmResult = await RunLlmAsync(
                    requestId,
                    prompt,
                    thinking.CancellationToken)
                .ConfigureAwait(true);
            if (!llmResult.Succeeded || llmResult.Intent == null)
            {
                FailTurnClosed(llmResult.DiagnosticCode, llmResult.Detail);
                return;
            }

            ReachyBehaviorIntent intent = llmResult.Intent;
            TriggerGesture(intent.Gesture);

            ReachyConversationOperation preparingSpeech =
                conversation.MarkThinkingComplete(thinking);

            string? speech = intent.Speech;
            if (string.IsNullOrWhiteSpace(speech))
            {
                // A valid RMA-151 intent may omit speech entirely (a
                // gesture-only reaction) -- schema_version is the only
                // required top-level property. The conversation state
                // machine's sequence is fixed
                // (Thinking->PreparingSpeech->Speaking->Idle), so this walks
                // through PreparingSpeech/Speaking to keep its bookkeeping
                // consistent without invoking any TTS provider, since there
                // is nothing to speak.
                ReachyConversationOperation speakingNoOp =
                    conversation.MarkSpeechPrepared(preparingSpeech);
                conversation.MarkSpeechComplete(speakingNoOp);
                stateStore.SetInteraction(
                    ReachyInteractionState.Idle,
                    "Reachy responded with a gesture only; the behavior intent carried no speech.");
                return;
            }

            TtsTurnResult ttsResult = await RunTtsAsync(speech, preparingSpeech)
                .ConfigureAwait(true);
            if (!ttsResult.Succeeded)
            {
                FailTurnClosed("tts-playback-failed", ttsResult.Detail);
                return;
            }

            stateStore.SetInteraction(ReachyInteractionState.Idle, "Ready.");
        }

        private void TriggerGesture(ReachyBehaviorGesture? gesture)
        {
            ReachyBaselineBehaviorKind? mapped =
                ReachyBehaviorIntentGestureMapper.Map(gesture ?? ReachyBehaviorGesture.None);
            if (mapped == null)
            {
                return;
            }

            // Speech is the primary payload; a missed baseline gesture is
            // degraded, not fatal -- do not abort the turn over it (RMA-195
            // spec).
            if (!behavior.TryTriggerGesture(mapped.Value, out string diagnosticCode))
            {
                Debug.LogWarning(
                    "ReachyConversationTurnOrchestrator: baseline gesture " + mapped.Value +
                    " was not triggered (" + diagnosticCode + ").");
            }
        }

        private async Task<string?> TryDescribeSceneAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            ReachyAndroidPerceptionApplicationService? scene = perceptionScene;
            if (scene == null)
            {
                return null;
            }

            try
            {
                SceneCaptureAnalysisOutcome outcome = await scene
                    .AnalyzeCurrentSceneAsync(VlmScenePrompt, requestId, cancellationToken)
                    .ConfigureAwait(true);
                if (!outcome.FrameCaptured || outcome.Result == null || !outcome.Result.Succeeded)
                {
                    // Per AnalyzeCurrentSceneAsync's own contract: both "no
                    // frame could be captured" and "a frame was captured but
                    // the VLM call did not succeed" mean "no scene
                    // description available" -- proceed voice-only rather
                    // than failing the turn.
                    return null;
                }

                string? text = outcome.Result.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return text.Length > MaximumSceneDescriptionCharactersInPrompt
                    ? text.Substring(0, MaximumSceneDescriptionCharactersInPrompt)
                    : text;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Best-effort: the VLM fold-in must never fail the whole
                // conversational turn. Any unexpected exception here is
                // treated the same as "no scene description available".
                Debug.LogWarning(
                    "ReachyConversationTurnOrchestrator: scene description failed (" +
                    exception.GetType().Name + ": " + exception.Message + "); proceeding voice-only.");
                return null;
            }
        }

        private static string BuildPrompt(string transcript, string? sceneDescription)
        {
            string prompt = string.IsNullOrWhiteSpace(sceneDescription)
                ? transcript
                : "Scene: " + sceneDescription + "\nUser: " + transcript;
            return prompt.Length > MaximumPromptCharacters
                ? prompt.Substring(0, MaximumPromptCharacters)
                : prompt;
        }

        private void FailTurnClosed(string diagnosticCode, string detail)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            // Fail closed: no fabricated apology speech, no silent
            // fallback. The conversation state machine is reset back to
            // Idle immediately afterward (a fresh session/turn id) so the
            // next mic press is not permanently blocked by one failed turn
            // -- the Error interaction state below is purely informational
            // for the UI, not a lasting internal lock.
            try
            {
                conversation.MarkError(diagnosticCode);
                conversation.ResetAfterError();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            string safeDetail = string.IsNullOrWhiteSpace(detail)
                ? "The conversation turn failed (" + diagnosticCode + ")."
                : detail;
            stateStore.SetInteraction(ReachyInteractionState.Error, safeDetail);
        }

        private string NextRequestId(string prefix)
        {
            long sequence = Interlocked.Increment(ref requestSequence);
            return "conversation-turn-" + prefix + "-" +
                sequence.ToString(CultureInfo.InvariantCulture);
        }
    }
}

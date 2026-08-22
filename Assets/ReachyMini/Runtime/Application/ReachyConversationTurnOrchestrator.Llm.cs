#nullable enable

using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;
using ReachyMini.LocalModels;
using ReachyMini.Providers;

namespace ReachyMini.AppState
{
    internal sealed partial class ReachyConversationTurnOrchestrator
    {
        private readonly struct LlmTurnResult
        {
            private LlmTurnResult(
                bool succeeded,
                ReachyBehaviorIntent? intent,
                string diagnosticCode,
                string detail)
            {
                Succeeded = succeeded;
                Intent = intent;
                DiagnosticCode = diagnosticCode;
                Detail = detail;
            }

            public bool Succeeded { get; }

            public ReachyBehaviorIntent? Intent { get; }

            public string DiagnosticCode { get; }

            public string Detail { get; }

            public static LlmTurnResult Success(ReachyBehaviorIntent intent) =>
                new LlmTurnResult(true, intent, "llm-succeeded", string.Empty);

            public static LlmTurnResult Failure(string diagnosticCode, string detail) =>
                new LlmTurnResult(false, null, diagnosticCode, detail);
        }

        // Decides local vs. cloud purely from the durable LLM provider
        // preference (RMA-195 spec): Cloud uses the cloud overload,
        // OnDevice/AndroidService both use the local governed overload
        // (AndroidService execution has no distinct wiring anywhere yet),
        // Unconfigured fails closed rather than silently no-op'ing.
        private async Task<LlmTurnResult> RunLlmAsync(
            string requestId,
            string prompt,
            CancellationToken cancellationToken)
        {
            ReachyProviderExecution execution = persistence.Settings.Current
                .GetProvider(ReachyProviderKind.Llm)
                .Execution;

            switch (execution)
            {
                case ReachyProviderExecution.Cloud:
                    return await RunCloudLlmAsync(requestId, prompt, cancellationToken)
                        .ConfigureAwait(true);
                case ReachyProviderExecution.OnDevice:
                case ReachyProviderExecution.AndroidService:
                    return await RunLocalLlmAsync(requestId, prompt, cancellationToken)
                        .ConfigureAwait(true);
                case ReachyProviderExecution.Unconfigured:
                default:
                    return LlmTurnResult.Failure(
                        "llm-unconfigured",
                        "No LLM provider is configured.");
            }
        }

        private async Task<LlmTurnResult> RunCloudLlmAsync(
            string requestId,
            string prompt,
            CancellationToken cancellationToken)
        {
            ICloudLlmProviderCapability? cloud = cloudLlm;
            if (cloud == null)
            {
                return LlmTurnResult.Failure(
                    "llm-cloud-unavailable",
                    "The cloud LLM capability is not bound.");
            }

            ReachyLlmGenerationRequest request = new ReachyLlmGenerationRequest(
                requestId + "-llm",
                new[] { new ReachyLlmChatMessage(ReachyLlmChatRole.User, prompt) });

            int regenerationsUsed = 0;
            while (true)
            {
                ReachyLlmGenerationResult result = await cloud
                    .GenerateAsync(request, cancellationToken)
                    .ConfigureAwait(true);
                if (result.Status == ReachyLlmGenerationStatus.HttpFailure)
                {
                    return LlmTurnResult.Failure("llm-http-failure", result.Detail);
                }
                if (result.Status == ReachyLlmGenerationStatus.Cancelled)
                {
                    return LlmTurnResult.Failure("llm-cancelled", result.Detail);
                }

                ReachyBehaviorIntentValidationResult? validation = result.Validation;
                if (validation == null)
                {
                    return LlmTurnResult.Failure("llm-missing-validation", result.Detail);
                }
                if (validation.Succeeded && validation.Intent != null)
                {
                    return LlmTurnResult.Success(validation.Intent);
                }

                ReachyBehaviorIntentRecoveryDecision decision =
                    ReachyBehaviorIntentRecoveryPolicy.Evaluate(
                        validation,
                        sameProviderRetryAuthorized: true,
                        regenerationAttemptsAlreadyUsed: regenerationsUsed);
                if (decision.Action != ReachyBehaviorIntentRecoveryAction.Regenerate)
                {
                    // Fail closed: no fabricated apology speech, no silent
                    // fallback -- a second invalid intent (or a
                    // retry-not-authorized decision) ends the turn with the
                    // real diagnostic visible, exactly like camera/mic
                    // unavailability elsewhere in this codebase.
                    return LlmTurnResult.Failure(decision.DiagnosticCode, validation.Detail);
                }

                ++regenerationsUsed;
                // Re-call with the SAME request (same prompt) -- a real
                // "ask again" retry, not a fabricated fallback.
            }
        }

        // RMA-195 (2026-08-22): unlike the cloud path
        // (ReachyLlmGenerationResult exposes a full
        // ReachyBehaviorIntentValidationResult via .Validation), the local
        // governed pipeline (LocalLlmGovernedGenerationResult ->
        // LocalLlmGenerationResult) validates the model's JSON output
        // internally (ReachyLocalLlmBehaviorContract.TryParseIntent) but
        // only surfaces a bounded diagnostic string on failure, not the
        // validation result object itself -- there is no public
        // constructor for ReachyBehaviorIntentValidationResult reachable
        // from here that could reconstruct one from that string. So this
        // mirrors ReachyBehaviorIntentRecoveryPolicy's "regenerate exactly
        // once, then fail closed" policy by hand, using the same
        // ReachyBehaviorIntentPolicy.MaximumRegenerationAttempts bound,
        // rather than calling ReachyBehaviorIntentRecoveryPolicy.Evaluate
        // directly as the cloud path does.
        private async Task<LlmTurnResult> RunLocalLlmAsync(
            string requestId,
            string prompt,
            CancellationToken cancellationToken)
        {
            ILocalLlmProviderCapability? local = localLlm;
            if (local == null)
            {
                return LlmTurnResult.Failure(
                    "llm-local-unavailable",
                    "The local LLM capability is not bound.");
            }

            LocalLlmGenerationRequest request = new LocalLlmGenerationRequest(
                requestId + "-llm",
                new[] { new LocalLlmChatMessage(LocalLlmChatRole.User, prompt) });
            // Streaming UI (partial-token display while the local model
            // generates) is a deliberate v1 deferral, not an oversight --
            // this orchestrator only needs the final governed result.
            NoOpLocalLlmStreamSink sink = new NoOpLocalLlmStreamSink();

            int regenerationsUsed = 0;
            while (true)
            {
                LocalLlmGovernedGenerationResult governed = await local
                    .GenerateAsync(request, sink, cancellationToken)
                    .ConfigureAwait(true);
                if (governed.Status != LocalLlmGovernedGenerationStatus.ProviderCompleted)
                {
                    return LlmTurnResult.Failure("llm-local-governed-failure", governed.Detail);
                }

                LocalLlmGenerationResult? providerResult = governed.ProviderResult;
                if (providerResult == null)
                {
                    return LlmTurnResult.Failure("llm-local-missing-result", governed.Detail);
                }
                if (providerResult.Succeeded && providerResult.Intent != null)
                {
                    return LlmTurnResult.Success(providerResult.Intent);
                }
                if (regenerationsUsed >= ReachyBehaviorIntentPolicy.MaximumRegenerationAttempts)
                {
                    return LlmTurnResult.Failure(
                        "invalid-intent-regeneration-limit-reached",
                        providerResult.Detail);
                }

                ++regenerationsUsed;
                // Re-call with the SAME request (same prompt) -- a real
                // "ask again" retry, not a fabricated fallback.
            }
        }

        private sealed class NoOpLocalLlmStreamSink : ILocalLlmStreamSink
        {
            public ValueTask OnEventAsync(
                LocalLlmStreamEvent streamEvent,
                CancellationToken cancellationToken)
            {
                return default;
            }
        }
    }
}

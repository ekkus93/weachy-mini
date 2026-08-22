#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Behavior;

namespace ReachyMini.Providers
{
    // RMA-142/143: shared bounded constants for cloud LLM chat requests. Kept
    // deliberately identical in shape to ReachyMini.LocalModels.LocalLlmProviderPolicy
    // (MaximumConversationMessages/MaximumMessageCharacters/etc.) so a future RMA-195
    // Phase D unification of local and cloud generation can normalize both under one
    // request/message DTO pair without a translation layer.
    public static class ReachyLlmProviderPolicy
    {
        public const int MaximumConversationMessages = 32;
        public const int MaximumMessageCharacters = 4096;
        public const int MaximumRequestIdCharacters = 128;
        public const int MaximumDiagnosticCharacters = 384;
    }

    public enum ReachyLlmChatRole
    {
        User = 0,
        Assistant = 1,
    }

    public sealed class ReachyLlmChatMessage
    {
        public ReachyLlmChatMessage(ReachyLlmChatRole role, string content)
        {
            if (!Enum.IsDefined(typeof(ReachyLlmChatRole), role))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }
            if (string.IsNullOrEmpty(content) ||
                content.Length > ReachyLlmProviderPolicy.MaximumMessageCharacters)
            {
                throw new ArgumentException(
                    "LLM chat message content is empty or exceeds the bounded character limit.",
                    nameof(content));
            }
            Role = role;
            Content = content;
        }

        public ReachyLlmChatRole Role { get; }

        public string Content { get; }
    }

    public sealed class ReachyLlmGenerationRequest
    {
        public ReachyLlmGenerationRequest(
            string requestId,
            IReadOnlyList<ReachyLlmChatMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(requestId) ||
                requestId.Length > ReachyLlmProviderPolicy.MaximumRequestIdCharacters)
            {
                throw new ArgumentException(
                    "LLM generation request id is empty or exceeds the bounded character limit.",
                    nameof(requestId));
            }
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }
            if (messages.Count == 0 ||
                messages.Count > ReachyLlmProviderPolicy.MaximumConversationMessages)
            {
                throw new ArgumentException(
                    "LLM generation requires between one and the bounded maximum conversation messages.",
                    nameof(messages));
            }
            for (int index = 0; index < messages.Count; ++index)
            {
                if (messages[index] == null)
                {
                    throw new ArgumentException(
                        "LLM generation messages cannot contain null.",
                        nameof(messages));
                }
            }
            if (messages[messages.Count - 1].Role != ReachyLlmChatRole.User)
            {
                throw new ArgumentException(
                    "The final message in an LLM generation request must be from the user.",
                    nameof(messages));
            }

            RequestId = requestId;
            Messages = messages;
        }

        public string RequestId { get; }

        public IReadOnlyList<ReachyLlmChatMessage> Messages { get; }
    }

    public enum ReachyLlmGenerationStatus
    {
        // The provider produced a syntactically valid RMA-151 behavior intent.
        // Callers still check ReachyLlmGenerationResult.Validation.Succeeded --
        // schema-version/bound/unknown-property rejections surface here too, with
        // the validation detail carrying the specific reason.
        BehaviorIntentValid = 0,
        BehaviorIntentInvalid = 1,
        HttpFailure = 2,
        Cancelled = 3,
    }

    public sealed class ReachyLlmGenerationResult
    {
        internal ReachyLlmGenerationResult(
            ReachyLlmGenerationStatus status,
            ReachyBehaviorIntentValidationResult? validation,
            string rawResponseText,
            string detail)
        {
            if (!Enum.IsDefined(typeof(ReachyLlmGenerationStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
            Status = status;
            Validation = validation;
            RawResponseText = BoundDiagnostic(rawResponseText);
            Detail = BoundDiagnostic(detail);
        }

        public ReachyLlmGenerationStatus Status { get; }

        // Present only when Status is BehaviorIntentValid or BehaviorIntentInvalid --
        // i.e. the provider returned text that reached RMA-151 validation at all.
        public ReachyBehaviorIntentValidationResult? Validation { get; }

        public string RawResponseText { get; }

        public string Detail { get; }

        public bool Succeeded =>
            Status == ReachyLlmGenerationStatus.BehaviorIntentValid &&
            Validation?.Succeeded == true;

        private static string BoundDiagnostic(string? value)
        {
            string text = value ?? string.Empty;
            return text.Length <= ReachyLlmProviderPolicy.MaximumDiagnosticCharacters
                ? text
                : text.Substring(0, ReachyLlmProviderPolicy.MaximumDiagnosticCharacters);
        }
    }

    // RMA-143: an OpenAI-compatible server is never assumed to support anything beyond
    // plain bounded text generation. Every flag here defaults to false in
    // ReachyLlmCapabilities.TextOnly() and must be explicitly turned on by the caller
    // that constructed the provider for its specific configured endpoint.
    public sealed class ReachyLlmCapabilities
    {
        public ReachyLlmCapabilities(
            bool supportsImages,
            bool supportsToolCalls,
            bool supportsJsonSchema,
            bool supportsStreaming)
        {
            SupportsImages = supportsImages;
            SupportsToolCalls = supportsToolCalls;
            SupportsJsonSchema = supportsJsonSchema;
            SupportsStreaming = supportsStreaming;
        }

        public bool SupportsImages { get; }

        public bool SupportsToolCalls { get; }

        public bool SupportsJsonSchema { get; }

        public bool SupportsStreaming { get; }

        public static ReachyLlmCapabilities TextOnly()
        {
            return new ReachyLlmCapabilities(
                supportsImages: false,
                supportsToolCalls: false,
                supportsJsonSchema: false,
                supportsStreaming: false);
        }
    }

    // RMA-195 phase B defined the analogous ILocalLlmProviderCapability for the
    // on-device provider (ReachyMini.AppState). This is the cloud-provider sibling,
    // kept in its own namespace/type rather than sharing the local one because the
    // local capability's request/stream-sink DTOs (LocalLlmGenerationRequest,
    // ILocalLlmStreamSink) live in ReachyMini.LocalModels and are shaped around a
    // governed streaming coordinator that has no cloud equivalent yet -- unifying
    // them is left for whichever future phase actually wires both into one call site.
    public interface ICloudLlmProviderCapability
    {
        ReachyLlmCapabilities Capabilities { get; }

        Task<ReachyLlmGenerationResult> GenerateAsync(
            ReachyLlmGenerationRequest request,
            CancellationToken cancellationToken);
    }
}

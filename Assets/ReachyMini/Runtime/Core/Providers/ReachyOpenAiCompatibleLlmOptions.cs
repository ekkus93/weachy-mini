#nullable enable

using System;

namespace ReachyMini.Providers
{
    public enum ReachyCompatibleLlmAuthenticationMode
    {
        None = 0,
        BearerCredentialReference = 1,
        ConfiguredHeaders = 2,
    }

    public sealed class ReachyOpenAiCompatibleLlmOptions
    {
        public const int MaximumSystemPromptCharactersLimit = 4096;
        public const int MaximumResponseBytesLimit = 1024 * 1024;
        public const int MaximumOutputTokensLimit = 4096;

        // RMA-151's behavior-intent JSON schema, restated as an instruction. Kept here
        // (not baked into the provider) so a caller can override it, but every
        // OpenAiDefault-style factory uses it -- the provider itself never invents or
        // relaxes the schema it asks the model to produce.
        public const string DefaultBehaviorIntentSystemPrompt =
            "Respond with exactly one JSON object describing a Reachy Mini behavior " +
            "intent and nothing else -- no prose, no markdown fences. Schema: " +
            "{\"schema_version\":1,\"speech\":<string, 1-160 chars, optional>," +
            "\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":<string, " +
            "optional>},\"expression\":<one of neutral|attentive|curious|pleased|" +
            "concerned|surprised, optional>,\"gesture\":<one of none|nod|" +
            "small_head_tilt|recoil, optional>,\"urgency\":<one of low|normal|high, " +
            "optional>,\"timing\":{\"start_delay_ms\":<int, optional>," +
            "\"maximum_duration_ms\":<int, optional>}}. Omit any field you do not " +
            "need. Do not invent properties outside this schema.";

        public ReachyOpenAiCompatibleLlmOptions(
            string relativeCompletionPath,
            ReachyCompatibleLlmAuthenticationMode authenticationMode,
            string systemPrompt,
            int maximumOutputTokens,
            int maximumResponseBytes)
        {
            RelativeCompletionPath = ValidateRelativePath(relativeCompletionPath);
            if (!Enum.IsDefined(
                    typeof(ReachyCompatibleLlmAuthenticationMode),
                    authenticationMode))
            {
                throw new ArgumentOutOfRangeException(nameof(authenticationMode));
            }
            if (string.IsNullOrWhiteSpace(systemPrompt) ||
                systemPrompt.Length > MaximumSystemPromptCharactersLimit)
            {
                throw new ArgumentException(
                    "LLM system prompt is empty or exceeds its bound.",
                    nameof(systemPrompt));
            }
            if (maximumOutputTokens < 1 || maximumOutputTokens > MaximumOutputTokensLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumOutputTokens));
            }
            if (maximumResponseBytes < 1024 ||
                maximumResponseBytes > MaximumResponseBytesLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
            }

            AuthenticationMode = authenticationMode;
            SystemPrompt = systemPrompt;
            MaximumOutputTokens = maximumOutputTokens;
            MaximumResponseBytes = maximumResponseBytes;
        }

        public string RelativeCompletionPath { get; }

        public ReachyCompatibleLlmAuthenticationMode AuthenticationMode { get; }

        public string SystemPrompt { get; }

        public int MaximumOutputTokens { get; }

        public int MaximumResponseBytes { get; }

        public static ReachyOpenAiCompatibleLlmOptions OpenAiResponsesDefault(
            string? systemPrompt = null)
        {
            return new ReachyOpenAiCompatibleLlmOptions(
                "v1/responses",
                ReachyCompatibleLlmAuthenticationMode.BearerCredentialReference,
                systemPrompt ?? DefaultBehaviorIntentSystemPrompt,
                512,
                MaximumResponseBytesLimit);
        }

        public static ReachyOpenAiCompatibleLlmOptions OpenAiChatCompletionsDefault(
            string? systemPrompt = null)
        {
            return new ReachyOpenAiCompatibleLlmOptions(
                "v1/chat/completions",
                ReachyCompatibleLlmAuthenticationMode.BearerCredentialReference,
                systemPrompt ?? DefaultBehaviorIntentSystemPrompt,
                512,
                MaximumResponseBytesLimit);
        }

        private static string ValidateRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.Length > 256 ||
                path[0] == '/' ||
                path.Contains("..", StringComparison.Ordinal) ||
                path.Contains('\\') ||
                path.Contains('?') ||
                path.Contains('#') ||
                Uri.TryCreate(path, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "LLM completion endpoint must be a bounded relative path without traversal, query, or fragment components.",
                    nameof(path));
            }
            return path;
        }
    }
}

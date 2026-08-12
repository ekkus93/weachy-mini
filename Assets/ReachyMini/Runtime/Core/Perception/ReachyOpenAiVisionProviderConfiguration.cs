#nullable enable

using System;
using System.Text;

namespace ReachyMini.Perception
{
    public sealed class OpenAiVisionProviderConfiguration
    {
        public OpenAiVisionProviderConfiguration(
            OpenAiVisionEndpointStyle endpointStyle,
            string providerId,
            string providerInstanceId,
            string displayName,
            string version,
            string modelId,
            VisionProviderLocation location,
            bool supportsVisualQuestions,
            bool supportsSceneDescription,
            int maximumConcurrentOperations,
            int maximumPromptCharacters,
            int maximumOutputTokens,
            int maximumResponseCharacters,
            RemoteVlmImagePolicy imagePolicy)
        {
            if (!Enum.IsDefined(
                    typeof(OpenAiVisionEndpointStyle),
                    endpointStyle))
            {
                throw new ArgumentOutOfRangeException(nameof(endpointStyle));
            }
            if (location != VisionProviderLocation.Cloud &&
                location != VisionProviderLocation.LocalNetwork)
            {
                throw new ArgumentException(
                    "OpenAI-compatible VLM providers must be explicit cloud or local-network providers.",
                    nameof(location));
            }
            if (!supportsVisualQuestions && !supportsSceneDescription)
            {
                throw new ArgumentException(
                    "A remote VLM provider must declare at least one semantic capability.");
            }
            if (maximumConcurrentOperations <= 0 ||
                maximumConcurrentOperations > 16)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentOperations));
            }
            if (maximumPromptCharacters <= 0 ||
                maximumPromptCharacters > 131072)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPromptCharacters));
            }
            if (maximumOutputTokens <= 0 || maximumOutputTokens > 32768)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumOutputTokens));
            }
            if (maximumResponseCharacters <= 0 ||
                maximumResponseCharacters > 1_000_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumResponseCharacters));
            }

            EndpointStyle = endpointStyle;
            ProviderId = RequireBoundedText(
                providerId,
                nameof(providerId),
                128);
            ProviderInstanceId = RequireBoundedText(
                providerInstanceId,
                nameof(providerInstanceId),
                128);
            DisplayName = RequireBoundedText(
                displayName,
                nameof(displayName),
                128);
            Version = RequireBoundedText(
                version,
                nameof(version),
                128);
            ModelId = RequireBoundedText(modelId, nameof(modelId), 256);
            Location = location;
            SupportsVisualQuestions = supportsVisualQuestions;
            SupportsSceneDescription = supportsSceneDescription;
            MaximumConcurrentOperations = maximumConcurrentOperations;
            MaximumPromptCharacters = maximumPromptCharacters;
            MaximumOutputTokens = maximumOutputTokens;
            MaximumResponseCharacters = maximumResponseCharacters;
            ImagePolicy = imagePolicy ??
                throw new ArgumentNullException(nameof(imagePolicy));
        }

        public OpenAiVisionEndpointStyle EndpointStyle { get; }

        public string ProviderId { get; }

        public string ProviderInstanceId { get; }

        public string DisplayName { get; }

        public string Version { get; }

        public string ModelId { get; }

        public VisionProviderLocation Location { get; }

        public bool SupportsVisualQuestions { get; }

        public bool SupportsSceneDescription { get; }

        public int MaximumConcurrentOperations { get; }

        public int MaximumPromptCharacters { get; }

        public int MaximumOutputTokens { get; }

        public int MaximumResponseCharacters { get; }

        public RemoteVlmImagePolicy ImagePolicy { get; }

        public ProviderDescriptor CreateDescriptor()
        {
            return new ProviderDescriptor(
                VisionProviderKind.SemanticVisionLanguage,
                ProviderId,
                ProviderInstanceId,
                DisplayName,
                Version,
                Location);
        }

        public VisionLanguageCapabilities CreateCapabilities()
        {
            return new VisionLanguageCapabilities(
                SupportsVisualQuestions,
                SupportsSceneDescription,
                supportsCancellation: true,
                maximumConcurrentOperations: MaximumConcurrentOperations,
                maximumPromptCharacters: MaximumPromptCharacters);
        }

        private static string RequireBoundedText(
            string value,
            string name,
            int maximumLength)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            return text;
        }
    }

    public sealed class OpenAiVisionProviderError
    {
        private static readonly string[] ForbiddenDetailFragments =
        {
            "authorization:",
            "bearer ",
            "x-api-key",
            "api-key",
            "api_key",
            "data:image/",
            "base64,",
            "sk-",
            "secret=",
            "access_token",
        };

        public OpenAiVisionProviderError(
            OpenAiVisionProviderErrorCategory category,
            string code,
            int? httpStatusCode,
            string? providerRequestId,
            string detail)
        {
            if (!Enum.IsDefined(
                    typeof(OpenAiVisionProviderErrorCategory),
                    category))
            {
                throw new ArgumentOutOfRangeException(nameof(category));
            }
            if (httpStatusCode.HasValue &&
                (httpStatusCode.Value < 100 || httpStatusCode.Value > 599))
            {
                throw new ArgumentOutOfRangeException(nameof(httpStatusCode));
            }

            Category = category;
            Code = ReachyOpenAiVisionDiagnosticTokens.RequireSafeToken(
                code,
                nameof(code),
                64);
            HttpStatusCode = httpStatusCode;
            ProviderRequestId = string.IsNullOrWhiteSpace(providerRequestId)
                ? null
                : ReachyOpenAiVisionDiagnosticTokens.RequireSafeToken(
                    providerRequestId,
                    nameof(providerRequestId),
                    128);
            Detail = SanitizeDetail(detail, out bool redacted);
            DetailRedacted = redacted;
        }

        public OpenAiVisionProviderErrorCategory Category { get; }

        public string Code { get; }

        public int? HttpStatusCode { get; }

        public string? ProviderRequestId { get; }

        public string Detail { get; }

        public bool DetailRedacted { get; }

        private static string SanitizeDetail(
            string value,
            out bool redacted)
        {
            string text = ProviderDescriptor.RequireText(
                value,
                nameof(value)).Trim();
            var builder = new StringBuilder(Math.Min(text.Length, 512));
            bool previousWasSpace = false;
            for (int index = 0; index < text.Length && builder.Length < 512; ++index)
            {
                char character = text[index];
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    if (!previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                    continue;
                }
                builder.Append(character);
                previousWasSpace = false;
            }

            string sanitized = builder.ToString().Trim();
            redacted = text.Length > 512 || ContainsForbiddenDetail(sanitized) ||
                ContainsLongOpaqueToken(sanitized);
            return redacted
                ? "Provider detail was redacted because it contained credential or payload-like material."
                : sanitized;
        }

        private static bool ContainsForbiddenDetail(string value)
        {
            for (int index = 0; index < ForbiddenDetailFragments.Length; ++index)
            {
                if (value.Contains(
                        ForbiddenDetailFragments[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsLongOpaqueToken(string value)
        {
            int run = 0;
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool opaque =
                    (character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '+' ||
                    character == '/' ||
                    character == '=' ||
                    character == '_' ||
                    character == '-';
                run = opaque ? run + 1 : 0;
                if (run >= 80)
                {
                    return true;
                }
            }
            return false;
        }
    }
}

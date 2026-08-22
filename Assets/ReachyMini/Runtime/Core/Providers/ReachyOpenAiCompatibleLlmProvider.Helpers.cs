#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReachyMini.Providers
{
    public abstract partial class ReachyOpenAiCompatibleLlmProviderBase
    {
        private static (
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore) CreateTransportBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyCompatibleLlmAuthenticationMode authenticationMode)
        {
            switch (authenticationMode)
            {
                case ReachyCompatibleLlmAuthenticationMode.None:
                    if (profile.CredentialReference != null)
                    {
                        throw new ArgumentException(
                            "Unauthenticated LLM profiles cannot retain an unused primary credential reference.",
                            nameof(profile));
                    }
                    return (profile, secretStore);

                case ReachyCompatibleLlmAuthenticationMode.ConfiguredHeaders:
                    if (profile.CredentialReference != null)
                    {
                        throw new ArgumentException(
                            "Configured-header LLM profiles cannot also define a primary credential reference.",
                            nameof(profile));
                    }
                    return (profile, secretStore);

                case ReachyCompatibleLlmAuthenticationMode.BearerCredentialReference:
                    ReachyBearerCredentialTransportBinding bearer =
                        ReachyBearerCredentialTransportBinding.Create(
                            profile,
                            secretStore ?? throw new ArgumentNullException(nameof(secretStore)),
                            InternalBearerSecretReference);
                    return (bearer.Profile, bearer.SecretStore);

                default:
                    throw new ArgumentOutOfRangeException(nameof(authenticationMode));
            }
        }

        private static void ValidateProfile(
            ReachyProviderProfile profile,
            ReachyProviderEndpointStyle requiredEndpointStyle,
            ReachyCompatibleLlmAuthenticationMode authenticationMode)
        {
            if (profile.EndpointStyle != requiredEndpointStyle)
            {
                throw new ArgumentException(
                    "LLM providers require a profile whose endpoint style matches the adapter class.",
                    nameof(profile));
            }
            _ = profile.GetModelId(ReachyProviderModelRole.Text);
            if (profile.StreamingEnabled)
            {
                throw new ArgumentException(
                    "RMA-142/143 buffered LLM profiles must disable streaming explicitly.",
                    nameof(profile));
            }
            if (!Enum.IsDefined(
                    typeof(ReachyCompatibleLlmAuthenticationMode),
                    authenticationMode))
            {
                throw new ArgumentOutOfRangeException(nameof(authenticationMode));
            }
        }

        private static byte[] BuildRequestBody(
            ReachyProviderEndpointStyle endpointStyle,
            string model,
            string systemPrompt,
            IReadOnlyList<ReachyLlmChatMessage> messages,
            int maximumOutputTokens)
        {
            var builder = new StringBuilder(512 + systemPrompt.Length);
            builder.Append('{');
            AppendProperty(builder, "model", model, first: true);
            builder.Append(',');
            AppendJsonString(builder, endpointStyle == ReachyProviderEndpointStyle.Responses ? "input" : "messages");
            builder.Append(':');
            builder.Append('[');
            AppendMessage(builder, "system", systemPrompt, first: true);
            for (int index = 0; index < messages.Count; ++index)
            {
                AppendMessage(
                    builder,
                    RoleName(messages[index].Role),
                    messages[index].Content,
                    first: false);
            }
            builder.Append(']');
            builder.Append(',');
            AppendJsonString(
                builder,
                endpointStyle == ReachyProviderEndpointStyle.Responses
                    ? "max_output_tokens"
                    : "max_tokens");
            builder.Append(':');
            builder.Append(maximumOutputTokens.ToString(CultureInfo.InvariantCulture));
            builder.Append('}');
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
        }

        private static void AppendMessage(
            StringBuilder builder,
            string role,
            string content,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            builder.Append('{');
            AppendProperty(builder, "role", role, first: true);
            builder.Append(',');
            AppendProperty(builder, "content", content, first: true);
            builder.Append('}');
        }

        private static string RoleName(ReachyLlmChatRole role)
        {
            switch (role)
            {
                case ReachyLlmChatRole.User: return "user";
                case ReachyLlmChatRole.Assistant: return "assistant";
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private static void AppendProperty(
            StringBuilder builder,
            string name,
            string value,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            AppendJsonString(builder, name);
            builder.Append(':');
            AppendJsonString(builder, value);
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            builder.Append('"');
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else if (char.IsHighSurrogate(character))
                        {
                            if (index + 1 >= value.Length ||
                                !char.IsLowSurrogate(value[index + 1]))
                            {
                                throw new ArgumentException(
                                    "LLM request JSON text contains an unpaired high surrogate.",
                                    nameof(value));
                            }
                            builder.Append(character);
                            builder.Append(value[++index]);
                        }
                        else if (char.IsLowSurrogate(character))
                        {
                            throw new ArgumentException(
                                "LLM request JSON text contains an unpaired low surrogate.",
                                nameof(value));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private static string DecodeStrictUtf8(ReadOnlyMemory<byte> bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            }
            catch (DecoderFallbackException exception)
            {
                throw new FormatException("LLM provider response is not strict UTF-8.", exception);
            }
        }

        private static ReachyLlmGenerationResult FailureFromTransport(
            ReachyHttpTransportResult result,
            bool callerCancelled,
            bool effectiveTimeoutCancelled)
        {
            ReachyHttpTransportError error = result.Error ??
                throw new InvalidOperationException("Failed LLM HTTP result omitted its typed error.");
            if (callerCancelled)
            {
                return Cancelled();
            }
            if (effectiveTimeoutCancelled && error.Category == ReachyHttpErrorCategory.Cancelled)
            {
                return new ReachyLlmGenerationResult(
                    ReachyLlmGenerationStatus.HttpFailure,
                    null,
                    string.Empty,
                    "LLM generation exceeded its bounded timeout.");
            }
            return new ReachyLlmGenerationResult(
                ReachyLlmGenerationStatus.HttpFailure,
                null,
                string.Empty,
                BoundDiagnostic(error.Detail));
        }

        private static string BoundDiagnostic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "LLM provider operation failed without a diagnostic.";
            }
            return value.Length <= ReachyLlmProviderPolicy.MaximumDiagnosticCharacters
                ? value
                : value.Substring(0, ReachyLlmProviderPolicy.MaximumDiagnosticCharacters);
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ReachyMini.Speech;

namespace ReachyMini.Providers
{
    public sealed partial class ReachyOpenAiCompatibleTtsProvider
    {
        private static (
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore) CreateTransportBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyCompatibleTtsAuthenticationMode authenticationMode)
        {
            switch (authenticationMode)
            {
                case ReachyCompatibleTtsAuthenticationMode.None:
                    if (profile.CredentialReference != null)
                    {
                        throw new ArgumentException(
                            "Unauthenticated TTS profiles cannot retain an unused primary credential reference.",
                            nameof(profile));
                    }
                    return (profile, secretStore);

                case ReachyCompatibleTtsAuthenticationMode.ConfiguredHeaders:
                    if (profile.CredentialReference != null)
                    {
                        throw new ArgumentException(
                            "Configured-header TTS profiles cannot also define a primary credential reference.",
                            nameof(profile));
                    }
                    return (profile, secretStore);

                case ReachyCompatibleTtsAuthenticationMode.BearerCredentialReference:
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
            ReachyCompatibleTtsAuthenticationMode authenticationMode)
        {
            if (profile.EndpointStyle != ReachyProviderEndpointStyle.AudioSpeech)
            {
                throw new ArgumentException(
                    "TTS providers require the AudioSpeech endpoint style.",
                    nameof(profile));
            }
            _ = profile.GetModelId(ReachyProviderModelRole.Tts);
            if (profile.StreamingEnabled)
            {
                throw new ArgumentException(
                    "RMA-145 buffered TTS profiles must disable streaming explicitly.",
                    nameof(profile));
            }
            if (!Enum.IsDefined(
                    typeof(ReachyCompatibleTtsAuthenticationMode),
                    authenticationMode))
            {
                throw new ArgumentOutOfRangeException(nameof(authenticationMode));
            }
        }

        private static void ValidateLocation(SpeechProviderLocation location)
        {
            if (location != SpeechProviderLocation.Cloud &&
                location != SpeechProviderLocation.LocalNetwork)
            {
                throw new ArgumentException(
                    "OpenAI-compatible TTS must declare Cloud or LocalNetwork location.",
                    nameof(location));
            }
        }

        private static TtsVoice[] CopyVoices(IEnumerable<TtsVoice> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            var result = new List<TtsVoice>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TtsVoice? voice in source)
            {
                TtsVoice actual = voice ?? throw new ArgumentException(
                    "TTS voice list cannot contain null.",
                    nameof(source));
                if (!IsBoundedVoiceId(actual.VoiceId))
                {
                    throw new ArgumentException(
                        "TTS voice identifiers must be bounded visible text without control characters.",
                        nameof(source));
                }
                if (!ids.Add(actual.VoiceId))
                {
                    throw new ArgumentException(
                        "TTS voice identifiers must be unique within a provider instance.",
                        nameof(source));
                }
                result.Add(actual);
                if (result.Count > 128)
                {
                    throw new ArgumentException(
                        "TTS voice list exceeds the provider bound.",
                        nameof(source));
                }
            }
            if (result.Count == 0)
            {
                throw new ArgumentException(
                    "TTS provider requires at least one declared voice.",
                    nameof(source));
            }
            return result.ToArray();
        }

        private static bool IsBoundedVoiceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                if (char.IsControl(value[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] BuildRequestBody(
            string model,
            string input,
            string voice,
            TtsEncodedAudioFormat responseFormat,
            string? instructions)
        {
            var builder = new StringBuilder(512 + input.Length + (instructions?.Length ?? 0));
            builder.Append('{');
            AppendProperty(builder, "model", model, first: true);
            AppendProperty(builder, "input", input, first: false);
            AppendProperty(builder, "voice", voice, first: false);
            AppendProperty(
                builder,
                "response_format",
                ReachyOpenAiCompatibleTtsOptions.ResponseFormatName(responseFormat),
                first: false);
            if (instructions != null)
            {
                AppendProperty(builder, "instructions", instructions, first: false);
            }
            builder.Append('}');
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
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
                                    "TTS JSON text contains an unpaired high surrogate.",
                                    nameof(value));
                            }
                            builder.Append(character);
                            builder.Append(value[++index]);
                        }
                        else if (char.IsLowSurrogate(character))
                        {
                            throw new ArgumentException(
                                "TTS JSON text contains an unpaired low surrogate.",
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

        private TtsEvent FailureFromTransport(
            string requestId,
            ulong sequence,
            ReachyHttpTransportResult result,
            bool callerCancelled,
            bool effectiveTimeoutCancelled)
        {
            ReachyHttpTransportError error = result.Error ??
                throw new InvalidOperationException(
                    "Failed TTS HTTP result omitted its typed error.");
            if (callerCancelled)
            {
                return new TtsEvent(
                    Descriptor.InstanceId,
                    requestId,
                    sequence,
                    TtsEventKind.Cancelled);
            }
            if (effectiveTimeoutCancelled &&
                error.Category == ReachyHttpErrorCategory.Cancelled)
            {
                return FailureEvent(
                    requestId,
                    sequence,
                    SpeechErrorCategory.Timeout,
                    "TTS_HTTP_TIMEOUT",
                    "TTS generation exceeded its bounded timeout.",
                    retryable: true);
            }
            if (error.Category == ReachyHttpErrorCategory.Cancelled)
            {
                return FailureEvent(
                    requestId,
                    sequence,
                    SpeechErrorCategory.ServiceFailure,
                    "TTS_HTTP_UNEXPECTED_CANCELLATION",
                    "TTS transport cancelled without caller cancellation or timeout.",
                    retryable: false);
            }

            SpeechErrorCategory category;
            string code;
            switch (error.Category)
            {
                case ReachyHttpErrorCategory.Timeout:
                    category = SpeechErrorCategory.Timeout;
                    code = "TTS_HTTP_TIMEOUT";
                    break;
                case ReachyHttpErrorCategory.Authentication:
                case ReachyHttpErrorCategory.Permission:
                    category = SpeechErrorCategory.ProviderUnavailable;
                    code = "TTS_HTTP_AUTHORIZATION";
                    break;
                case ReachyHttpErrorCategory.MalformedResponse:
                case ReachyHttpErrorCategory.ResponseTooLarge:
                    category = SpeechErrorCategory.MalformedResponse;
                    code = "TTS_HTTP_RESPONSE_INVALID";
                    break;
                case ReachyHttpErrorCategory.Configuration:
                    category = SpeechErrorCategory.ContractViolation;
                    code = "TTS_HTTP_CONFIGURATION";
                    break;
                case ReachyHttpErrorCategory.Network:
                case ReachyHttpErrorCategory.Tls:
                    category = SpeechErrorCategory.Network;
                    code = "TTS_HTTP_NETWORK";
                    break;
                default:
                    category = SpeechErrorCategory.ServiceFailure;
                    code = "TTS_HTTP_PROVIDER_FAILURE";
                    break;
            }
            return FailureEvent(
                requestId,
                sequence,
                category,
                code,
                BoundDiagnostic(error.Detail),
                error.Retryable);
        }

        private TtsEvent FailureEvent(
            string requestId,
            ulong sequence,
            SpeechErrorCategory category,
            string code,
            string diagnostic,
            bool retryable)
        {
            return new TtsEvent(
                Descriptor.InstanceId,
                requestId,
                sequence,
                TtsEventKind.Failed,
                error: new SpeechProviderError(
                    category,
                    code,
                    BoundDiagnostic(diagnostic),
                    retryable));
        }

        private static string BoundDiagnostic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "TTS provider operation failed without a diagnostic.";
            }
            return value.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? value
                : value.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
        }
    }
}

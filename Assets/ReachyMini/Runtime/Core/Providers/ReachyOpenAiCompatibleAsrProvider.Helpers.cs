#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReachyMini.Speech;

namespace ReachyMini.Providers
{
    public sealed partial class ReachyOpenAiCompatibleAsrProvider
    {
        private static (
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore) CreateTransportBinding(
            ReachyProviderProfile profile,
            IReachyProviderSecretStore? secretStore,
            ReachyCompatibleAsrAuthenticationMode authenticationMode)
        {
            switch (authenticationMode)
            {
                case ReachyCompatibleAsrAuthenticationMode.None:
                    if (profile.CredentialReference != null)
                    {
                        throw new ArgumentException(
                            "Unauthenticated ASR profiles cannot retain an unused primary credential reference.",
                            nameof(profile));
                    }
                    return (profile, secretStore);

                case ReachyCompatibleAsrAuthenticationMode.ConfiguredHeaders:
                    if (profile.CredentialReference != null)
                    {
                        throw new ArgumentException(
                            "Configured-header ASR profiles cannot also define a primary credential reference.",
                            nameof(profile));
                    }
                    return (profile, secretStore);

                case ReachyCompatibleAsrAuthenticationMode.BearerCredentialReference:
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
            ReachyCompatibleAsrAuthenticationMode authenticationMode)
        {
            if (profile.EndpointStyle != ReachyProviderEndpointStyle.AudioTranscriptions)
            {
                throw new ArgumentException(
                    "ASR providers require the AudioTranscriptions endpoint style.",
                    nameof(profile));
            }
            _ = profile.GetModelId(ReachyProviderModelRole.Asr);
            if (profile.StreamingEnabled)
            {
                throw new ArgumentException(
                    "RMA-144 buffered ASR profiles must disable streaming explicitly.",
                    nameof(profile));
            }
            if (!Enum.IsDefined(
                    typeof(ReachyCompatibleAsrAuthenticationMode),
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
                    "OpenAI-compatible ASR must declare Cloud or LocalNetwork location.",
                    nameof(location));
            }
        }

        private static string NormalizeLanguage(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                throw new ArgumentException("ASR language tag is required.", nameof(languageTag));
            }
            int separator = languageTag.IndexOf('-');
            string primary = separator < 0
                ? languageTag
                : languageTag.Substring(0, separator);
            if (primary.Length != 2 ||
                !IsAsciiLetter(primary[0]) ||
                !IsAsciiLetter(primary[1]))
            {
                throw new ArgumentException(
                    "OpenAI-compatible ASR language tags require a two-letter ISO-639-1 primary language subtag.",
                    nameof(languageTag));
            }
            return primary.ToLowerInvariant();
        }

        private static bool IsAsciiLetter(char value)
        {
            return value >= 'A' && value <= 'Z' ||
                value >= 'a' && value <= 'z';
        }

        private static string CreateBoundary(string requestId)
        {
            byte[] requestBytes = Encoding.UTF8.GetBytes(requestId);
            try
            {
                ulong hash = 14695981039346656037UL;
                for (int index = 0; index < requestBytes.Length; ++index)
                {
                    hash ^= requestBytes[index];
                    hash *= 1099511628211UL;
                }
                return "reachy-asr-" + hash.ToString("x16");
            }
            finally
            {
                Array.Clear(requestBytes, 0, requestBytes.Length);
            }
        }

        private static string CreateMultipartContentType(string requestId)
        {
            return "multipart/form-data; boundary=" + CreateBoundary(requestId);
        }

        private static byte[] BuildMultipartBody(
            string requestId,
            string model,
            string language,
            AsrEncodedAudioFormat format,
            byte[] audio,
            int maximumAudioBytes)
        {
            if (audio == null)
            {
                throw new ArgumentNullException(nameof(audio));
            }
            if (audio.Length == 0 || audio.Length > maximumAudioBytes)
            {
                throw new InvalidDataException(
                    "ASR audio payload is empty or exceeds the configured byte bound.");
            }
            string boundary = CreateBoundary(requestId);
            string fileName = "utterance." + ExtensionFor(format);
            string contentType = ContentTypeFor(format);

            using var stream = new MemoryStream(
                checked(audio.Length + 2048));
            WriteAscii(stream, "--" + boundary + "\r\n");
            WriteAscii(stream,
                "Content-Disposition: form-data; name=\"model\"\r\n\r\n");
            WriteUtf8Field(stream, model);
            WriteAscii(stream, "\r\n--" + boundary + "\r\n");
            WriteAscii(stream,
                "Content-Disposition: form-data; name=\"language\"\r\n\r\n");
            WriteAscii(stream, language);
            WriteAscii(stream, "\r\n--" + boundary + "\r\n");
            WriteAscii(stream,
                "Content-Disposition: form-data; name=\"response_format\"\r\n\r\njson");
            WriteAscii(stream, "\r\n--" + boundary + "\r\n");
            WriteAscii(stream,
                "Content-Disposition: form-data; name=\"file\"; filename=\"" +
                fileName + "\"\r\n");
            WriteAscii(stream, "Content-Type: " + contentType + "\r\n\r\n");
            stream.Write(audio, 0, audio.Length);
            WriteAscii(stream, "\r\n--" + boundary + "--\r\n");

            if (stream.Length > checked((long)maximumAudioBytes + 4096L))
            {
                throw new InvalidDataException(
                    "ASR multipart body exceeds its bounded audio-plus-metadata allowance.");
            }
            return stream.ToArray();
        }

        private static void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            try
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static void WriteUtf8Field(Stream stream, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            {
                throw new ArgumentException("ASR multipart text field is invalid.");
            }
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value);
            try
            {
                for (int index = 0; index < bytes.Length; ++index)
                {
                    if (bytes[index] == 0x0d || bytes[index] == 0x0a)
                    {
                        throw new ArgumentException(
                            "ASR multipart text fields cannot contain CR or LF bytes.");
                    }
                }
                stream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static string ExtensionFor(AsrEncodedAudioFormat format)
        {
            switch (format)
            {
                case AsrEncodedAudioFormat.Flac: return "flac";
                case AsrEncodedAudioFormat.Mp3: return "mp3";
                case AsrEncodedAudioFormat.Mp4: return "mp4";
                case AsrEncodedAudioFormat.Mpeg: return "mpeg";
                case AsrEncodedAudioFormat.Mpga: return "mpga";
                case AsrEncodedAudioFormat.M4a: return "m4a";
                case AsrEncodedAudioFormat.Ogg: return "ogg";
                case AsrEncodedAudioFormat.Wav: return "wav";
                case AsrEncodedAudioFormat.Webm: return "webm";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static string ContentTypeFor(AsrEncodedAudioFormat format)
        {
            switch (format)
            {
                case AsrEncodedAudioFormat.Flac: return "audio/flac";
                case AsrEncodedAudioFormat.Mp3: return "audio/mpeg";
                case AsrEncodedAudioFormat.Mp4: return "audio/mp4";
                case AsrEncodedAudioFormat.Mpeg: return "audio/mpeg";
                case AsrEncodedAudioFormat.Mpga: return "audio/mpeg";
                case AsrEncodedAudioFormat.M4a: return "audio/mp4";
                case AsrEncodedAudioFormat.Ogg: return "audio/ogg";
                case AsrEncodedAudioFormat.Wav: return "audio/wav";
                case AsrEncodedAudioFormat.Webm: return "audio/webm";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static string DecodeStrictUtf8(ReadOnlyMemory<byte> bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            }
            catch (DecoderFallbackException exception)
            {
                throw new FormatException(
                    "ASR provider response is not strict UTF-8.",
                    exception);
            }
        }

        private AsrEvent FailureFromTransport(
            string requestId,
            ulong sequence,
            ReachyHttpTransportResult result,
            bool callerCancelled,
            bool effectiveTimeoutCancelled)
        {
            ReachyHttpTransportError error = result.Error ??
                throw new InvalidOperationException(
                    "Failed ASR HTTP result omitted its typed error.");
            if (callerCancelled)
            {
                return new AsrEvent(
                    Descriptor.InstanceId,
                    requestId,
                    sequence,
                    AsrEventKind.Cancelled);
            }
            if (effectiveTimeoutCancelled &&
                error.Category == ReachyHttpErrorCategory.Cancelled)
            {
                return FailureEvent(
                    requestId,
                    sequence,
                    SpeechErrorCategory.Timeout,
                    "ASR_HTTP_TIMEOUT",
                    "ASR transcription exceeded its bounded timeout.",
                    retryable: true);
            }
            if (error.Category == ReachyHttpErrorCategory.Cancelled)
            {
                return FailureEvent(
                    requestId,
                    sequence,
                    SpeechErrorCategory.ServiceFailure,
                    "ASR_HTTP_UNEXPECTED_CANCELLATION",
                    "ASR transport cancelled without caller cancellation or timeout.",
                    retryable: false);
            }

            SpeechErrorCategory category;
            string code;
            switch (error.Category)
            {
                case ReachyHttpErrorCategory.Timeout:
                    category = SpeechErrorCategory.Timeout;
                    code = "ASR_HTTP_TIMEOUT";
                    break;
                case ReachyHttpErrorCategory.Authentication:
                case ReachyHttpErrorCategory.Permission:
                    category = SpeechErrorCategory.ProviderUnavailable;
                    code = "ASR_HTTP_AUTHORIZATION";
                    break;
                case ReachyHttpErrorCategory.MalformedResponse:
                case ReachyHttpErrorCategory.ResponseTooLarge:
                    category = SpeechErrorCategory.MalformedResponse;
                    code = "ASR_HTTP_RESPONSE_INVALID";
                    break;
                case ReachyHttpErrorCategory.Configuration:
                    category = SpeechErrorCategory.ContractViolation;
                    code = "ASR_HTTP_CONFIGURATION";
                    break;
                case ReachyHttpErrorCategory.Network:
                case ReachyHttpErrorCategory.Tls:
                    category = SpeechErrorCategory.Network;
                    code = "ASR_HTTP_NETWORK";
                    break;
                default:
                    category = SpeechErrorCategory.ServiceFailure;
                    code = "ASR_HTTP_PROVIDER_FAILURE";
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

        private AsrEvent FailureEvent(
            string requestId,
            ulong sequence,
            SpeechErrorCategory category,
            string code,
            string diagnostic,
            bool retryable)
        {
            return new AsrEvent(
                Descriptor.InstanceId,
                requestId,
                sequence,
                AsrEventKind.Failed,
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
                return "ASR provider operation failed without a diagnostic.";
            }
            return value.Length <= SpeechProviderError.MaximumDiagnosticCharacters
                ? value
                : value.Substring(0, SpeechProviderError.MaximumDiagnosticCharacters);
        }
    }
}

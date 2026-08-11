#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.Speech;

namespace ReachyMini.Providers
{
    public enum ReachyCompatibleTtsAuthenticationMode
    {
        None = 0,
        BearerCredentialReference = 1,
        ConfiguredHeaders = 2,
    }

    public sealed class ReachyOpenAiCompatibleTtsOptions
    {
        public const int MaximumInputCharactersLimit = 4096;
        public const int MaximumInstructionsCharactersLimit = 4096;
        public const int MaximumResponseBytesLimit = 16 * 1024 * 1024;

        private readonly string[] acceptedResponseContentTypes;

        public ReachyOpenAiCompatibleTtsOptions(
            string relativeSpeechPath,
            ReachyCompatibleTtsAuthenticationMode authenticationMode,
            TtsEncodedAudioFormat responseFormat,
            int maximumInputCharacters,
            int maximumResponseBytes,
            string? instructions,
            bool supportsInstructions,
            string[] acceptedResponseContentTypes)
        {
            RelativeSpeechPath = ValidateRelativePath(relativeSpeechPath);
            if (!Enum.IsDefined(
                    typeof(ReachyCompatibleTtsAuthenticationMode),
                    authenticationMode))
            {
                throw new ArgumentOutOfRangeException(nameof(authenticationMode));
            }
            if (!Enum.IsDefined(typeof(TtsEncodedAudioFormat), responseFormat))
            {
                throw new ArgumentOutOfRangeException(nameof(responseFormat));
            }
            if (maximumInputCharacters < 1 ||
                maximumInputCharacters > MaximumInputCharactersLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumInputCharacters));
            }
            if (maximumResponseBytes < 1024 ||
                maximumResponseBytes > MaximumResponseBytesLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
            }
            if (instructions != null)
            {
                if (!supportsInstructions)
                {
                    throw new ArgumentException(
                        "TTS instructions were configured for a model/profile that does not declare instruction support.",
                        nameof(instructions));
                }
                ValidateText(instructions, MaximumInstructionsCharactersLimit, nameof(instructions));
            }

            this.acceptedResponseContentTypes =
                CopyContentTypes(responseFormat, acceptedResponseContentTypes);
            AuthenticationMode = authenticationMode;
            ResponseFormat = responseFormat;
            MaximumInputCharacters = maximumInputCharacters;
            MaximumResponseBytes = maximumResponseBytes;
            Instructions = instructions;
            SupportsInstructions = supportsInstructions;
        }

        public string RelativeSpeechPath { get; }

        public ReachyCompatibleTtsAuthenticationMode AuthenticationMode { get; }

        public TtsEncodedAudioFormat ResponseFormat { get; }

        public int MaximumInputCharacters { get; }

        public int MaximumResponseBytes { get; }

        public string? Instructions { get; }

        public bool SupportsInstructions { get; }

        public IReadOnlyList<string> AcceptedResponseContentTypes =>
            Array.AsReadOnly(acceptedResponseContentTypes);

        public bool AcceptsContentType(string? contentType)
        {
            if (contentType == null)
            {
                return false;
            }
            for (int index = 0; index < acceptedResponseContentTypes.Length; ++index)
            {
                if (string.Equals(
                        acceptedResponseContentTypes[index],
                        contentType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static ReachyOpenAiCompatibleTtsOptions OpenAiDefault(
            TtsEncodedAudioFormat responseFormat = TtsEncodedAudioFormat.Mp3)
        {
            return new ReachyOpenAiCompatibleTtsOptions(
                "v1/audio/speech",
                ReachyCompatibleTtsAuthenticationMode.BearerCredentialReference,
                responseFormat,
                MaximumInputCharactersLimit,
                MaximumResponseBytesLimit,
                instructions: null,
                supportsInstructions: false,
                acceptedResponseContentTypes: DefaultContentTypes(responseFormat));
        }

        public static string ResponseFormatName(TtsEncodedAudioFormat format)
        {
            switch (format)
            {
                case TtsEncodedAudioFormat.Mp3: return "mp3";
                case TtsEncodedAudioFormat.Opus: return "opus";
                case TtsEncodedAudioFormat.Aac: return "aac";
                case TtsEncodedAudioFormat.Flac: return "flac";
                case TtsEncodedAudioFormat.Wav: return "wav";
                case TtsEncodedAudioFormat.Pcm: return "pcm";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static string[] DefaultContentTypes(TtsEncodedAudioFormat format)
        {
            switch (format)
            {
                case TtsEncodedAudioFormat.Mp3:
                    return new[] { "audio/mpeg" };
                case TtsEncodedAudioFormat.Opus:
                    return new[] { "audio/opus", "audio/ogg" };
                case TtsEncodedAudioFormat.Aac:
                    return new[] { "audio/aac" };
                case TtsEncodedAudioFormat.Flac:
                    return new[] { "audio/flac" };
                case TtsEncodedAudioFormat.Wav:
                    return new[] { "audio/wav", "audio/x-wav" };
                case TtsEncodedAudioFormat.Pcm:
                    return new[] { "audio/pcm", "application/octet-stream" };
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static string ValidateRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.Length > 256 ||
                path[0] == '/' ||
                path.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                path.IndexOf('\\') >= 0 ||
                path.IndexOf('?') >= 0 ||
                path.IndexOf('#') >= 0 ||
                Uri.TryCreate(path, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "TTS speech endpoint must be a bounded relative path without traversal, query, or fragment components.",
                    nameof(path));
            }
            return path;
        }

        private static string[] CopyContentTypes(
            TtsEncodedAudioFormat responseFormat,
            string[] contentTypes)
        {
            if (contentTypes == null)
            {
                throw new ArgumentNullException(nameof(contentTypes));
            }
            if (contentTypes.Length == 0 || contentTypes.Length > 8)
            {
                throw new ArgumentException(
                    "TTS response content-type allowlist must contain between one and eight values.",
                    nameof(contentTypes));
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var copy = new string[contentTypes.Length];
            for (int index = 0; index < contentTypes.Length; ++index)
            {
                string value = contentTypes[index] ??
                    throw new ArgumentException(
                        "TTS response content-type allowlist cannot contain null.",
                        nameof(contentTypes));
                if (!IsMediaType(value) ||
                    !IsContentTypeCompatibleWithFormat(responseFormat, value) ||
                    !seen.Add(value))
                {
                    throw new ArgumentException(
                        "TTS response content-type allowlist contains an invalid, duplicate, or format-incompatible media type.",
                        nameof(contentTypes));
                }
                copy[index] = value;
            }
            return copy;
        }

        private static bool IsContentTypeCompatibleWithFormat(
            TtsEncodedAudioFormat format,
            string value)
        {
            switch (format)
            {
                case TtsEncodedAudioFormat.Mp3:
                    return EqualsMediaType(value, "audio/mpeg") ||
                        EqualsMediaType(value, "audio/mp3");
                case TtsEncodedAudioFormat.Opus:
                    return EqualsMediaType(value, "audio/opus") ||
                        EqualsMediaType(value, "audio/ogg");
                case TtsEncodedAudioFormat.Aac:
                    return EqualsMediaType(value, "audio/aac") ||
                        EqualsMediaType(value, "audio/mp4");
                case TtsEncodedAudioFormat.Flac:
                    return EqualsMediaType(value, "audio/flac") ||
                        EqualsMediaType(value, "audio/x-flac");
                case TtsEncodedAudioFormat.Wav:
                    return EqualsMediaType(value, "audio/wav") ||
                        EqualsMediaType(value, "audio/x-wav") ||
                        EqualsMediaType(value, "audio/wave");
                case TtsEncodedAudioFormat.Pcm:
                    return EqualsMediaType(value, "audio/pcm") ||
                        EqualsMediaType(value, "audio/l16") ||
                        EqualsMediaType(value, "application/octet-stream");
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static bool EqualsMediaType(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMediaType(string value)
        {
            if (value.Length < 3 || value.Length > 128)
            {
                return false;
            }
            int slash = value.IndexOf('/');
            if (slash < 1 || slash == value.Length - 1 ||
                value.IndexOf('/', slash + 1) >= 0)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '!' || character == '#' || character == '$' ||
                    character == '&' || character == '^' || character == '_' ||
                    character == '.' || character == '+' || character == '-' ||
                    character == '/';
                if (!valid)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateText(
            string value,
            int maximumCharacters,
            string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumCharacters)
            {
                throw new ArgumentException(
                    "TTS configured text is empty or exceeds its bound.",
                    name);
            }
        }
    }
}

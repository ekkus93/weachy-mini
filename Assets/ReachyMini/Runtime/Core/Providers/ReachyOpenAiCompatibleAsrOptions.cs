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
    public enum ReachyCompatibleAsrAuthenticationMode
    {
        None = 0,
        BearerCredentialReference = 1,
        ConfiguredHeaders = 2,
    }

    public sealed class ReachyOpenAiCompatibleAsrOptions
    {
        public const int MaximumAudioBytesLimit = 24 * 1024 * 1024;
        public const int MaximumResponseBytesLimit = 1024 * 1024;
        public static readonly TimeSpan MaximumUtteranceDurationLimit =
            TimeSpan.FromMinutes(30.0);

        public ReachyOpenAiCompatibleAsrOptions(
            string relativeTranscriptionPath,
            ReachyCompatibleAsrAuthenticationMode authenticationMode,
            int maximumAudioBytes,
            TimeSpan maximumUtteranceDuration,
            int maximumResponseBytes)
        {
            RelativeTranscriptionPath = ValidateRelativePath(
                relativeTranscriptionPath);
            if (!Enum.IsDefined(
                    typeof(ReachyCompatibleAsrAuthenticationMode),
                    authenticationMode))
            {
                throw new ArgumentOutOfRangeException(nameof(authenticationMode));
            }
            if (maximumAudioBytes < 1024 ||
                maximumAudioBytes > MaximumAudioBytesLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumAudioBytes));
            }
            if (maximumUtteranceDuration <= TimeSpan.Zero ||
                maximumUtteranceDuration > MaximumUtteranceDurationLimit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumUtteranceDuration));
            }
            if (maximumResponseBytes < 256 ||
                maximumResponseBytes > MaximumResponseBytesLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
            }

            AuthenticationMode = authenticationMode;
            MaximumAudioBytes = maximumAudioBytes;
            MaximumUtteranceDuration = maximumUtteranceDuration;
            MaximumResponseBytes = maximumResponseBytes;
        }

        public string RelativeTranscriptionPath { get; }

        public ReachyCompatibleAsrAuthenticationMode AuthenticationMode { get; }

        public int MaximumAudioBytes { get; }

        public TimeSpan MaximumUtteranceDuration { get; }

        public int MaximumResponseBytes { get; }

        public static ReachyOpenAiCompatibleAsrOptions OpenAiDefault()
        {
            return new ReachyOpenAiCompatibleAsrOptions(
                "v1/audio/transcriptions",
                ReachyCompatibleAsrAuthenticationMode.BearerCredentialReference,
                MaximumAudioBytesLimit,
                TimeSpan.FromMinutes(10.0),
                256 * 1024);
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
                    "ASR transcription endpoint must be a bounded relative path without traversal, query, or fragment components.",
                    nameof(path));
            }
            return path;
        }
    }

}

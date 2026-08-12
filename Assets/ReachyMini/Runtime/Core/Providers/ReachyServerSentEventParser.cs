#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.Providers
{
    internal sealed class ReachyServerSentEventParser
    {
        private const int MaximumLineCharacters = 64 * 1024;

        private readonly UTF8Encoding encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private readonly Decoder decoder;
        private readonly int maximumEventCharacters;
        private readonly StringBuilder line = new StringBuilder();
        private readonly StringBuilder data = new StringBuilder();
        private string eventName = string.Empty;
        private string eventId = string.Empty;
        private bool previousWasCarriageReturn;

        public ReachyServerSentEventParser(int maximumEventCharacters)
        {
            if (maximumEventCharacters < 1 ||
                maximumEventCharacters >
                    ReachyHttpTransportPolicy.MaximumSseEventCharactersLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEventCharacters));
            }
            this.maximumEventCharacters = maximumEventCharacters;
            decoder = encoding.GetDecoder();
        }

        public IReadOnlyList<ReachyServerSentEvent> Append(
            byte[] buffer,
            int count,
            int statusCode,
            string? providerRequestId)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (count < 0 || count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            _ = statusCode;
            _ = providerRequestId;

            int maximumChars = encoding.GetMaxCharCount(count);
            char[] chars = new char[maximumChars];
            decoder.Convert(
                buffer,
                0,
                count,
                chars,
                0,
                chars.Length,
                flush: false,
                out _,
                out int charsUsed,
                out _);

            var events = new List<ReachyServerSentEvent>();
            for (int index = 0; index < charsUsed; ++index)
            {
                ProcessCharacter(chars[index], events);
            }
            return events.AsReadOnly();
        }

        public ReachyHttpTransportError? Complete(
            int statusCode,
            string? providerRequestId)
        {
            char[] chars = new char[4];
            decoder.Convert(
                Array.Empty<byte>(),
                0,
                0,
                chars,
                0,
                chars.Length,
                flush: true,
                out _,
                out int charsUsed,
                out _);
            var ignoredEvents = new List<ReachyServerSentEvent>();
            for (int index = 0; index < charsUsed; ++index)
            {
                ProcessCharacter(chars[index], ignoredEvents);
            }
            if (line.Length != 0 ||
                data.Length != 0 ||
                eventName.Length != 0)
            {
                return new ReachyHttpTransportError(
                    ReachyHttpErrorCategory.MalformedResponse,
                    ReachyHttpTimeoutPhase.ResponseRead,
                    statusCode,
                    providerRequestId,
                    false,
                    "Streaming HTTP response ended with an incomplete SSE event.");
            }
            return null;
        }

        private void ProcessCharacter(
            char character,
            List<ReachyServerSentEvent> events)
        {
            if (previousWasCarriageReturn)
            {
                previousWasCarriageReturn = false;
                if (character == '\n')
                {
                    return;
                }
            }

            if (character == '\r')
            {
                ProcessLine(events);
                previousWasCarriageReturn = true;
                return;
            }
            if (character == '\n')
            {
                ProcessLine(events);
                return;
            }

            line.Append(character);
            if (line.Length > MaximumLineCharacters)
            {
                throw new InvalidDataException(
                    "SSE line exceeds the configured parser bound.");
            }
        }

        private void ProcessLine(List<ReachyServerSentEvent> events)
        {
            if (line.Length == 0)
            {
                Dispatch(events);
                return;
            }

            string currentLine = line.ToString();
            line.Clear();
            if (currentLine[0] == ':')
            {
                return;
            }

            int separator = currentLine.IndexOf(':');
            string field;
            string value;
            if (separator < 0)
            {
                field = currentLine;
                value = string.Empty;
            }
            else
            {
                field = currentLine.Substring(0, separator);
                int valueStart = separator + 1;
                if (valueStart < currentLine.Length &&
                    currentLine[valueStart] == ' ')
                {
                    ++valueStart;
                }
                value = currentLine.Substring(valueStart);
            }

            if (string.Equals(field, "data", StringComparison.Ordinal))
            {
                if (data.Length != 0)
                {
                    data.Append('\n');
                }
                data.Append(value);
                EnsureEventBound();
            }
            else if (string.Equals(field, "event", StringComparison.Ordinal))
            {
                eventName = value;
                EnsureEventBound();
            }
            else if (string.Equals(field, "id", StringComparison.Ordinal) &&
                value.IndexOf('\0') < 0)
            {
                eventId = value;
                EnsureEventBound();
            }
        }

        private void Dispatch(List<ReachyServerSentEvent> events)
        {
            if (data.Length == 0)
            {
                eventName = string.Empty;
                return;
            }
            events.Add(new ReachyServerSentEvent(
                eventName,
                data.ToString(),
                eventId));
            data.Clear();
            eventName = string.Empty;
        }

        private void EnsureEventBound()
        {
            long characters = (long)data.Length + eventName.Length + eventId.Length;
            if (characters > maximumEventCharacters)
            {
                throw new InvalidDataException(
                    "SSE event exceeds the configured parser bound.");
            }
        }
    }
}

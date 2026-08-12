#nullable enable

using System;
using System.Text;

namespace ReachyMini.Behavior
{
    internal sealed class ReachyBehaviorIntentJsonReader
    {
        private readonly string text;
        private int index;

        internal ReachyBehaviorIntentJsonReader(string text)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
        }

        internal bool IsAtEnd
        {
            get
            {
                SkipWhitespace();
                return index == text.Length;
            }
        }

        internal void Expect(char expected)
        {
            SkipWhitespace();
            if (index >= text.Length || text[index] != expected)
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "invalid-json-token",
                    "Behavior intent JSON expected '" + expected + "'.");
            }
            ++index;
        }

        internal bool TryConsume(char value)
        {
            SkipWhitespace();
            if (index >= text.Length || text[index] != value)
            {
                return false;
            }
            ++index;
            return true;
        }

        internal bool TryConsumeLiteral(string literal)
        {
            SkipWhitespace();
            if (index + literal.Length > text.Length)
            {
                return false;
            }
            for (int offset = 0; offset < literal.Length; ++offset)
            {
                if (text[index + offset] != literal[offset])
                {
                    return false;
                }
            }
            index += literal.Length;
            return true;
        }

        internal string ReadString()
        {
            SkipWhitespace();
            ExpectRaw('"');
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char character = text[index++];
                if (character == '"')
                {
                    return builder.ToString();
                }
                if (character == '\\')
                {
                    ReadEscape(builder);
                    continue;
                }
                if (character < 0x20 || character == 0x7f)
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-string",
                        "Behavior intent JSON string contains an unescaped control character.");
                }
                AppendValidatedCharacter(builder, character);
            }

            throw Failure(
                ReachyBehaviorIntentValidationStatus.InvalidJson,
                "unterminated-json-string",
                "Behavior intent JSON string is unterminated.");
        }

        internal int ReadNonNegativeInteger(int maximum, string diagnosticCode)
        {
            SkipWhitespace();
            if (index >= text.Length)
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "missing-json-integer",
                    "Behavior intent JSON integer is missing.");
            }
            if (text[index] == '-')
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    diagnosticCode,
                    "Behavior intent integer cannot be negative.");
            }
            if (text[index] < '0' || text[index] > '9')
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "invalid-json-integer",
                    "Behavior intent JSON expected an integer.");
            }

            int value = 0;
            if (text[index] == '0')
            {
                ++index;
                if (index < text.Length && text[index] >= '0' && text[index] <= '9')
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-integer",
                        "Behavior intent JSON integer cannot contain a leading zero.");
                }
                EnsureIntegerTerminated();
                return 0;
            }

            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                int digit = text[index++] - '0';
                if (value > (maximum - digit) / 10)
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.BoundExceeded,
                        diagnosticCode,
                        "Behavior intent integer exceeds its allowed maximum.");
                }
                value = value * 10 + digit;
            }
            EnsureIntegerTerminated();
            return value;
        }

        internal static int CountUnicodeScalars(string value, string diagnosticCode)
        {
            int count = 0;
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length ||
                        !char.IsLowSurrogate(value[index + 1]))
                    {
                        throw Failure(
                            ReachyBehaviorIntentValidationStatus.InvalidValue,
                            diagnosticCode,
                            "Behavior intent string contains an invalid Unicode surrogate pair.");
                    }
                    ++index;
                }
                else if (char.IsLowSurrogate(character))
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidValue,
                        diagnosticCode,
                        "Behavior intent string contains an unexpected low Unicode surrogate.");
                }
                ++count;
            }
            return count;
        }

        private void EnsureIntegerTerminated()
        {
            if (index >= text.Length)
            {
                return;
            }
            char character = text[index];
            if (character == '.' || character == 'e' || character == 'E' ||
                character == '+' || character == '-')
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "invalid-json-integer",
                    "Behavior intent integer fields do not accept fractions or exponents.");
            }
        }

        private void ReadEscape(StringBuilder builder)
        {
            if (index >= text.Length)
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "incomplete-json-escape",
                    "Behavior intent JSON escape is incomplete.");
            }

            char escape = text[index++];
            switch (escape)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escape);
                    return;
                case 'b':
                    builder.Append('\b');
                    return;
                case 'f':
                    builder.Append('\f');
                    return;
                case 'n':
                    builder.Append('\n');
                    return;
                case 'r':
                    builder.Append('\r');
                    return;
                case 't':
                    builder.Append('\t');
                    return;
                case 'u':
                    ReadUnicodeEscape(builder);
                    return;
                default:
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-escape",
                        "Behavior intent JSON string contains an invalid escape sequence.");
            }
        }

        private void ReadUnicodeEscape(StringBuilder builder)
        {
            char first = ReadHexCodeUnit();
            if (char.IsHighSurrogate(first))
            {
                if (index + 1 >= text.Length ||
                    text[index] != '\\' || text[index + 1] != 'u')
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-surrogate",
                        "Behavior intent JSON string contains an incomplete escaped surrogate pair.");
                }
                index += 2;
                char second = ReadHexCodeUnit();
                if (!char.IsLowSurrogate(second))
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-surrogate",
                        "Behavior intent JSON string contains an invalid escaped surrogate pair.");
                }
                builder.Append(first);
                builder.Append(second);
                return;
            }
            if (char.IsLowSurrogate(first))
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "invalid-json-surrogate",
                    "Behavior intent JSON string contains an unexpected escaped low surrogate.");
            }
            if (first == '\0')
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "embedded-nul",
                    "Behavior intent strings cannot contain an embedded NUL character.");
            }
            builder.Append(first);
        }

        private char ReadHexCodeUnit()
        {
            if (index + 4 > text.Length)
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "incomplete-json-unicode-escape",
                    "Behavior intent JSON Unicode escape is incomplete.");
            }

            int value = 0;
            for (int count = 0; count < 4; ++count)
            {
                char character = text[index++];
                int digit;
                if (character >= '0' && character <= '9')
                {
                    digit = character - '0';
                }
                else if (character >= 'a' && character <= 'f')
                {
                    digit = character - 'a' + 10;
                }
                else if (character >= 'A' && character <= 'F')
                {
                    digit = character - 'A' + 10;
                }
                else
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-unicode-escape",
                        "Behavior intent JSON Unicode escape contains a non-hexadecimal digit.");
                }
                value = (value << 4) | digit;
            }
            return (char)value;
        }

        private void AppendValidatedCharacter(StringBuilder builder, char character)
        {
            if (character == '\0')
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "embedded-nul",
                    "Behavior intent strings cannot contain an embedded NUL character.");
            }
            if (char.IsHighSurrogate(character))
            {
                if (index >= text.Length || !char.IsLowSurrogate(text[index]))
                {
                    throw Failure(
                        ReachyBehaviorIntentValidationStatus.InvalidJson,
                        "invalid-json-surrogate",
                        "Behavior intent JSON string contains an invalid Unicode surrogate pair.");
                }
                builder.Append(character);
                builder.Append(text[index++]);
                return;
            }
            if (char.IsLowSurrogate(character))
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "invalid-json-surrogate",
                    "Behavior intent JSON string contains an unexpected low Unicode surrogate.");
            }
            builder.Append(character);
        }

        private void ExpectRaw(char expected)
        {
            if (index >= text.Length || text[index] != expected)
            {
                throw Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "invalid-json-token",
                    "Behavior intent JSON expected '" + expected + "'.");
            }
            ++index;
        }

        private void SkipWhitespace()
        {
            while (index < text.Length)
            {
                char character = text[index];
                if (character != ' ' && character != '\t' &&
                    character != '\n' && character != '\r')
                {
                    return;
                }
                ++index;
            }
        }

        internal static ReachyBehaviorIntentValidationException Failure(
            ReachyBehaviorIntentValidationStatus status,
            string diagnosticCode,
            string detail)
        {
            return new ReachyBehaviorIntentValidationException(
                status,
                diagnosticCode,
                detail);
        }
    }

    internal sealed class ReachyBehaviorIntentValidationException : FormatException
    {
        internal ReachyBehaviorIntentValidationException(
            ReachyBehaviorIntentValidationStatus status,
            string diagnosticCode,
            string detail)
            : base(detail)
        {
            Status = status;
            DiagnosticCode = diagnosticCode;
        }

        internal ReachyBehaviorIntentValidationStatus Status { get; }

        internal string DiagnosticCode { get; }
    }
}

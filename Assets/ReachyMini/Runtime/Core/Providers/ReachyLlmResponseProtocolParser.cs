#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace ReachyMini.Providers
{
    // RMA-142/143: hand-rolled bounded JSON scanner extracting exactly the assistant
    // text from an OpenAI Chat Completions or Responses API response, mirroring
    // ReachyAsrTranscriptionProtocolParser's style (skip everything except the target
    // field(s), full-document consumption required, depth-bounded). Deliberately not a
    // general-purpose JSON value tree: every other adapter family in this codebase
    // (ASR/TTS) hand-rolls its own narrow protocol parser rather than sharing one.
    internal sealed class ReachyLlmResponseProtocolParser
    {
        private readonly string text;
        private readonly int maximumDepth;
        private readonly int maximumTextCharacters;
        private int index;

        public ReachyLlmResponseProtocolParser(
            string text,
            int maximumDepth,
            int maximumTextCharacters)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            if (maximumDepth < 1 || maximumDepth > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            }
            if (maximumTextCharacters < 1 || maximumTextCharacters > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTextCharacters));
            }
            this.maximumDepth = maximumDepth;
            this.maximumTextCharacters = maximumTextCharacters;
        }

        // choices[0].message.content
        public string? ParseChatCompletionsAssistantText()
        {
            string? content = null;
            bool sawChoices = false;

            SkipWhitespace();
            Expect('{');
            SkipWhitespace();
            if (!TryConsume('}'))
            {
                while (true)
                {
                    string property = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    if (property == "choices")
                    {
                        RequireFirstOccurrence(ref sawChoices, "choices");
                        content = ReadFirstChoiceMessageContent();
                    }
                    else
                    {
                        SkipValue(1);
                    }
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }
                    Expect(',');
                    SkipWhitespace();
                }
            }
            SkipWhitespace();
            RequireFullyConsumed();
            return content;
        }

        // Prefers a flattened top-level "output_text" string (several
        // OpenAI-compatible gateways expose this convenience field); otherwise walks
        // output[].content[] for the first {"type":"output_text","text":...}.
        public string? ParseResponsesAssistantText()
        {
            string? content = null;
            bool sawOutputText = false;
            bool sawOutput = false;

            SkipWhitespace();
            Expect('{');
            SkipWhitespace();
            if (!TryConsume('}'))
            {
                while (true)
                {
                    string property = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    if (property == "output_text" && !sawOutputText)
                    {
                        sawOutputText = true;
                        string candidate = ReadString();
                        content ??= BoundText(candidate);
                    }
                    else if (property == "output" && !sawOutput)
                    {
                        sawOutput = true;
                        string? fromOutput = ReadFirstOutputTextFromOutputArray();
                        content ??= fromOutput;
                    }
                    else
                    {
                        SkipValue(1);
                    }
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }
                    Expect(',');
                    SkipWhitespace();
                }
            }
            SkipWhitespace();
            RequireFullyConsumed();
            return content;
        }

        private string? ReadFirstChoiceMessageContent()
        {
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return null;
            }
            string? result = null;
            bool first = true;
            while (true)
            {
                if (first)
                {
                    result = ReadChoiceObjectMessageContent();
                    first = false;
                }
                else
                {
                    SkipValue(2);
                }
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    break;
                }
                Expect(',');
                SkipWhitespace();
            }
            return result;
        }

        private string? ReadChoiceObjectMessageContent()
        {
            EnsureDepth(2);
            Expect('{');
            SkipWhitespace();
            string? content = null;
            if (!TryConsume('}'))
            {
                while (true)
                {
                    string property = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    if (property == "message" && content == null)
                    {
                        content = ReadMessageObjectContent();
                    }
                    else
                    {
                        SkipValue(3);
                    }
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }
                    Expect(',');
                    SkipWhitespace();
                }
            }
            return content;
        }

        private string? ReadMessageObjectContent()
        {
            EnsureDepth(3);
            Expect('{');
            SkipWhitespace();
            string? content = null;
            if (!TryConsume('}'))
            {
                while (true)
                {
                    string property = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    if (property == "content" && content == null)
                    {
                        content = BoundText(ReadString());
                    }
                    else
                    {
                        SkipValue(4);
                    }
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }
                    Expect(',');
                    SkipWhitespace();
                }
            }
            return content;
        }

        private string? ReadFirstOutputTextFromOutputArray()
        {
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return null;
            }
            string? found = null;
            while (true)
            {
                string? candidate = ReadOutputItemFirstOutputText(found == null);
                found ??= candidate;
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    break;
                }
                Expect(',');
                SkipWhitespace();
            }
            return found;
        }

        private string? ReadOutputItemFirstOutputText(bool search)
        {
            EnsureDepth(2);
            Expect('{');
            SkipWhitespace();
            string? found = null;
            if (!TryConsume('}'))
            {
                while (true)
                {
                    string property = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    if (search && property == "content" && found == null)
                    {
                        found = ReadContentArrayFirstOutputText();
                    }
                    else
                    {
                        SkipValue(3);
                    }
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }
                    Expect(',');
                    SkipWhitespace();
                }
            }
            return found;
        }

        private string? ReadContentArrayFirstOutputText()
        {
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return null;
            }
            string? found = null;
            while (true)
            {
                string? candidate = ReadContentItemIfOutputText();
                found ??= candidate;
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    break;
                }
                Expect(',');
                SkipWhitespace();
            }
            return found;
        }

        private string? ReadContentItemIfOutputText()
        {
            EnsureDepth(4);
            Expect('{');
            SkipWhitespace();
            string? type = null;
            string? contentText = null;
            if (!TryConsume('}'))
            {
                while (true)
                {
                    string property = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    if (property == "type" && type == null)
                    {
                        type = ReadString();
                    }
                    else if (property == "text" && contentText == null)
                    {
                        contentText = BoundText(ReadString());
                    }
                    else
                    {
                        SkipValue(5);
                    }
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        break;
                    }
                    Expect(',');
                    SkipWhitespace();
                }
            }
            return type == "output_text" ? contentText : null;
        }

        private string BoundText(string value)
        {
            if (value.Length > maximumTextCharacters)
            {
                throw Error("LLM response assistant text exceeds the configured character bound.");
            }
            return value;
        }

        private void RequireFullyConsumed()
        {
            if (index != text.Length)
            {
                throw Error("Trailing JSON data.");
            }
        }

        private void SkipValue(int depth)
        {
            EnsureDepth(depth);
            SkipWhitespace();
            if (index >= text.Length)
            {
                throw Error("Expected JSON value.");
            }
            char token = text[index];
            if (token == '"')
            {
                _ = ReadString();
                return;
            }
            if (token == '{')
            {
                SkipObject(depth + 1);
                return;
            }
            if (token == '[')
            {
                SkipArray(depth + 1);
                return;
            }
            if (token == 't')
            {
                RequireLiteral("true");
                return;
            }
            if (token == 'f')
            {
                RequireLiteral("false");
                return;
            }
            if (token == 'n')
            {
                RequireLiteral("null");
                return;
            }
            if (token == '-' || token >= '0' && token <= '9')
            {
                SkipNumber();
                return;
            }
            throw Error("Unsupported JSON value.");
        }

        private void SkipObject(int depth)
        {
            EnsureDepth(depth);
            Expect('{');
            SkipWhitespace();
            if (TryConsume('}'))
            {
                return;
            }
            while (true)
            {
                _ = ReadString();
                SkipWhitespace();
                Expect(':');
                SkipValue(depth + 1);
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return;
                }
                Expect(',');
                SkipWhitespace();
            }
        }

        private void SkipArray(int depth)
        {
            EnsureDepth(depth);
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']'))
            {
                return;
            }
            while (true)
            {
                SkipValue(depth + 1);
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return;
                }
                Expect(',');
                SkipWhitespace();
            }
        }

        private void SkipNumber()
        {
            int start = index;
            if (TryConsume('-'))
            {
                if (index >= text.Length)
                {
                    throw Error("Incomplete JSON number.");
                }
            }
            if (TryConsume('0'))
            {
                if (index < text.Length && char.IsDigit(text[index]))
                {
                    throw Error("JSON number contains a leading zero.");
                }
            }
            else
            {
                RequireDigit();
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    ++index;
                }
            }
            if (TryConsume('.'))
            {
                RequireDigit();
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    ++index;
                }
            }
            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                ++index;
                if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                {
                    ++index;
                }
                RequireDigit();
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    ++index;
                }
            }
            if (index == start)
            {
                throw Error("Invalid JSON number.");
            }
        }

        private void RequireDigit()
        {
            if (index >= text.Length || !char.IsDigit(text[index]))
            {
                throw Error("Expected JSON number digit.");
            }
            ++index;
        }

        private string ReadString()
        {
            Expect('"');
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
                if (character < 0x20 || char.IsSurrogate(character))
                {
                    throw Error(
                        "JSON strings cannot contain control characters or unescaped surrogates.");
                }
                builder.Append(character);
            }
            throw Error("Unterminated JSON string.");
        }

        private void ReadEscape(StringBuilder builder)
        {
            if (index >= text.Length)
            {
                throw Error("Incomplete JSON escape.");
            }
            char escape = text[index++];
            switch (escape)
            {
                case '"': builder.Append('"'); return;
                case '\\': builder.Append('\\'); return;
                case '/': builder.Append('/'); return;
                case 'b': builder.Append('\b'); return;
                case 'f': builder.Append('\f'); return;
                case 'n': builder.Append('\n'); return;
                case 'r': builder.Append('\r'); return;
                case 't': builder.Append('\t'); return;
                case 'u':
                    char decoded = ReadHexCodeUnit();
                    if (char.IsHighSurrogate(decoded))
                    {
                        if (index + 1 >= text.Length ||
                            text[index] != '\\' || text[index + 1] != 'u')
                        {
                            throw Error("JSON unicode escape contains an unpaired high surrogate.");
                        }
                        index += 2;
                        char low = ReadHexCodeUnit();
                        if (!char.IsLowSurrogate(low))
                        {
                            throw Error("JSON unicode escape contains an invalid surrogate pair.");
                        }
                        builder.Append(decoded);
                        builder.Append(low);
                        return;
                    }
                    if (char.IsLowSurrogate(decoded))
                    {
                        throw Error("JSON unicode escape contains an unpaired low surrogate.");
                    }
                    builder.Append(decoded);
                    return;
                default:
                    throw Error("Unsupported JSON escape.");
            }
        }

        private char ReadHexCodeUnit()
        {
            if (index + 4 > text.Length)
            {
                throw Error("Incomplete JSON unicode escape.");
            }
            int value = 0;
            for (int offset = 0; offset < 4; ++offset)
            {
                char character = text[index++];
                int digit;
                if (character >= '0' && character <= '9')
                {
                    digit = character - '0';
                }
                else if (character >= 'a' && character <= 'f')
                {
                    digit = 10 + character - 'a';
                }
                else if (character >= 'A' && character <= 'F')
                {
                    digit = 10 + character - 'A';
                }
                else
                {
                    throw Error("Invalid JSON unicode escape.");
                }
                value = (value << 4) | digit;
            }
            return (char)value;
        }

        private void RequireLiteral(string literal)
        {
            if (index + literal.Length > text.Length ||
                !string.Equals(
                    text.Substring(index, literal.Length),
                    literal,
                    StringComparison.Ordinal))
            {
                throw Error("Invalid JSON literal.");
            }
            index += literal.Length;
        }

        private void RequireFirstOccurrence(ref bool seen, string field)
        {
            if (seen)
            {
                throw Error("Duplicate JSON field: " + field + ".");
            }
            seen = true;
        }

        private void SkipWhitespace()
        {
            while (index < text.Length)
            {
                char character = text[index];
                if (character != ' ' && character != '\t' &&
                    character != '\r' && character != '\n')
                {
                    return;
                }
                ++index;
            }
        }

        private void Expect(char expected)
        {
            if (index >= text.Length || text[index] != expected)
            {
                throw Error("Expected JSON token '" + expected + "'.");
            }
            ++index;
        }

        private bool TryConsume(char expected)
        {
            if (index < text.Length && text[index] == expected)
            {
                ++index;
                return true;
            }
            return false;
        }

        private void EnsureDepth(int depth)
        {
            if (depth > maximumDepth)
            {
                throw Error("JSON nesting exceeds the LLM protocol depth bound.");
            }
        }

        private FormatException Error(string message)
        {
            return new FormatException(
                message + " Offset=" + index.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}

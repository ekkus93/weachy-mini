#nullable enable

using System;
using System.Text;

namespace ReachyMini.Language
{
    public enum LocalLlmExpression
    {
        Neutral = 0,
        Attentive = 1,
        Curious = 2,
        Pleased = 3,
        Concerned = 4,
        Surprised = 5,
    }

    public enum LocalLlmGesture
    {
        None = 0,
        Nod = 1,
        SmallHeadTilt = 2,
        Recoil = 3,
    }

    public enum LocalLlmUrgency
    {
        Low = 0,
        Normal = 1,
        High = 2,
    }

    public sealed class LocalLlmGazeTarget
    {
        internal LocalLlmGazeTarget(string entityId)
        {
            Kind = "tracked_entity";
            EntityId = entityId;
        }

        public string Kind { get; }

        public string EntityId { get; }
    }

    public sealed class LocalLlmBehaviorIntent
    {
        internal LocalLlmBehaviorIntent(
            string speech,
            LocalLlmGazeTarget? gazeTarget,
            LocalLlmExpression expression,
            LocalLlmGesture gesture,
            LocalLlmUrgency urgency)
        {
            SchemaVersion = 1;
            Speech = speech;
            GazeTarget = gazeTarget;
            Expression = expression;
            Gesture = gesture;
            Urgency = urgency;
        }

        public int SchemaVersion { get; }

        public string Speech { get; }

        public LocalLlmGazeTarget? GazeTarget { get; }

        public LocalLlmExpression Expression { get; }

        public LocalLlmGesture Gesture { get; }

        public LocalLlmUrgency Urgency { get; }
    }

    public enum LocalLlmIntentParseFailure
    {
        None = 0,
        Empty = 1,
        InvalidJson = 2,
        SchemaMismatch = 3,
        SpeechInvalid = 4,
        GazeInvalid = 5,
        ExpressionInvalid = 6,
        GestureInvalid = 7,
        UrgencyInvalid = 8,
        TrailingContent = 9,
    }

    public sealed class LocalLlmIntentParseResult
    {
        private LocalLlmIntentParseResult(
            LocalLlmBehaviorIntent? intent,
            LocalLlmIntentParseFailure failure,
            string detail)
        {
            Intent = intent;
            Failure = failure;
            Detail = detail;
        }

        public bool Succeeded => Intent != null && Failure == LocalLlmIntentParseFailure.None;

        public LocalLlmBehaviorIntent? Intent { get; }

        public LocalLlmIntentParseFailure Failure { get; }

        public string Detail { get; }

        internal static LocalLlmIntentParseResult Success(LocalLlmBehaviorIntent intent)
        {
            return new LocalLlmIntentParseResult(
                intent ?? throw new ArgumentNullException(nameof(intent)),
                LocalLlmIntentParseFailure.None,
                string.Empty);
        }

        internal static LocalLlmIntentParseResult Failed(
            LocalLlmIntentParseFailure failure,
            string detail)
        {
            if (failure == LocalLlmIntentParseFailure.None)
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }
            return new LocalLlmIntentParseResult(
                null,
                failure,
                detail ?? string.Empty);
        }
    }

    public static class LocalLlmBehaviorIntentParser
    {
        public const int MaximumSpeechCharacters = 160;
        private const int MaximumStringCharacters = 4096;

        public static LocalLlmIntentParseResult Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                return LocalLlmIntentParseResult.Failed(
                    LocalLlmIntentParseFailure.Empty,
                    "The local LLM returned no behavior-intent JSON.");
            }

            try
            {
                var cursor = new JsonCursor(json);
                cursor.SkipWhitespace();
                cursor.Require('{');
                cursor.SkipWhitespace();

                RequireProperty(cursor, "schema_version");
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                if (!cursor.ReadIntegerOne())
                {
                    return LocalLlmIntentParseResult.Failed(
                        LocalLlmIntentParseFailure.SchemaMismatch,
                        "Behavior intent schema_version must be the JSON integer 1.");
                }

                RequireComma(cursor);
                RequireProperty(cursor, "speech");
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                string speech = cursor.ReadString();
                if (speech.Length > MaximumSpeechCharacters || ContainsControlCharacter(speech))
                {
                    return LocalLlmIntentParseResult.Failed(
                        LocalLlmIntentParseFailure.SpeechInvalid,
                        "Behavior intent speech exceeds 160 characters or contains a control character.");
                }

                RequireComma(cursor);
                string nextProperty = cursor.ReadPropertyName();
                LocalLlmGazeTarget? gazeTarget = null;
                if (string.Equals(nextProperty, "gaze_target", StringComparison.Ordinal))
                {
                    cursor.SkipWhitespace();
                    cursor.Require(':');
                    cursor.SkipWhitespace();
                    gazeTarget = ReadGaze(cursor);
                    RequireComma(cursor);
                    nextProperty = cursor.ReadPropertyName();
                }

                if (!string.Equals(nextProperty, "expression", StringComparison.Ordinal))
                {
                    return LocalLlmIntentParseResult.Failed(
                        LocalLlmIntentParseFailure.ExpressionInvalid,
                        "Behavior intent must contain expression in the frozen canonical position.");
                }
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                LocalLlmExpression expression = ParseExpression(cursor.ReadString());

                RequireComma(cursor);
                RequireProperty(cursor, "gesture");
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                LocalLlmGesture gesture = ParseGesture(cursor.ReadString());

                RequireComma(cursor);
                RequireProperty(cursor, "urgency");
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                LocalLlmUrgency urgency = ParseUrgency(cursor.ReadString());

                cursor.SkipWhitespace();
                cursor.Require('}');
                cursor.SkipWhitespace();
                if (!cursor.AtEnd)
                {
                    return LocalLlmIntentParseResult.Failed(
                        LocalLlmIntentParseFailure.TrailingContent,
                        "Behavior intent contains trailing content after the JSON object.");
                }

                return LocalLlmIntentParseResult.Success(
                    new LocalLlmBehaviorIntent(
                        speech,
                        gazeTarget,
                        expression,
                        gesture,
                        urgency));
            }
            catch (IntentParseException exception)
            {
                return LocalLlmIntentParseResult.Failed(
                    exception.Failure,
                    exception.Message);
            }
        }

        private static void RequireComma(JsonCursor cursor)
        {
            cursor.SkipWhitespace();
            cursor.Require(',');
            cursor.SkipWhitespace();
        }

        private static void RequireProperty(JsonCursor cursor, string expected)
        {
            string actual = cursor.ReadPropertyName();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new IntentParseException(
                    LocalLlmIntentParseFailure.InvalidJson,
                    $"Expected behavior-intent property '{expected}'.");
            }
        }

        private static LocalLlmGazeTarget? ReadGaze(JsonCursor cursor)
        {
            if (cursor.TryReadLiteral("null"))
            {
                return null;
            }

            try
            {
                cursor.Require('{');
                cursor.SkipWhitespace();
                RequireProperty(cursor, "kind");
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                string kind = cursor.ReadString();
                if (!string.Equals(kind, "tracked_entity", StringComparison.Ordinal))
                {
                    throw new IntentParseException(
                        LocalLlmIntentParseFailure.GazeInvalid,
                        "Behavior intent gaze kind must be tracked_entity.");
                }
                RequireComma(cursor);
                RequireProperty(cursor, "entity_id");
                cursor.SkipWhitespace();
                cursor.Require(':');
                cursor.SkipWhitespace();
                string entityId = cursor.ReadString();
                if (!IsEntityId(entityId))
                {
                    throw new IntentParseException(
                        LocalLlmIntentParseFailure.GazeInvalid,
                        "Behavior intent gaze entity_id must use entity-N syntax.");
                }
                cursor.SkipWhitespace();
                cursor.Require('}');
                return new LocalLlmGazeTarget(entityId);
            }
            catch (IntentParseException exception)
                when (exception.Failure == LocalLlmIntentParseFailure.InvalidJson)
            {
                throw new IntentParseException(
                    LocalLlmIntentParseFailure.GazeInvalid,
                    "Behavior intent gaze_target does not match the frozen tracked-entity shape.");
            }
        }

        internal static bool IsEntityId(string value)
        {
            const string prefix = "entity-";
            if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length)
            {
                return false;
            }
            for (int index = prefix.Length; index < value.Length; ++index)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static LocalLlmExpression ParseExpression(string value)
        {
            return value switch
            {
                "neutral" => LocalLlmExpression.Neutral,
                "attentive" => LocalLlmExpression.Attentive,
                "curious" => LocalLlmExpression.Curious,
                "pleased" => LocalLlmExpression.Pleased,
                "concerned" => LocalLlmExpression.Concerned,
                "surprised" => LocalLlmExpression.Surprised,
                _ => throw new IntentParseException(
                    LocalLlmIntentParseFailure.ExpressionInvalid,
                    "Behavior intent expression is outside the frozen enum."),
            };
        }

        private static LocalLlmGesture ParseGesture(string value)
        {
            return value switch
            {
                "none" => LocalLlmGesture.None,
                "nod" => LocalLlmGesture.Nod,
                "small_head_tilt" => LocalLlmGesture.SmallHeadTilt,
                "recoil" => LocalLlmGesture.Recoil,
                _ => throw new IntentParseException(
                    LocalLlmIntentParseFailure.GestureInvalid,
                    "Behavior intent gesture is outside the frozen enum."),
            };
        }

        private static LocalLlmUrgency ParseUrgency(string value)
        {
            return value switch
            {
                "low" => LocalLlmUrgency.Low,
                "normal" => LocalLlmUrgency.Normal,
                "high" => LocalLlmUrgency.High,
                _ => throw new IntentParseException(
                    LocalLlmIntentParseFailure.UrgencyInvalid,
                    "Behavior intent urgency is outside the frozen enum."),
            };
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int index = 0; index < value.Length; ++index)
            {
                if (char.IsControl(value[index]))
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class IntentParseException : Exception
        {
            public IntentParseException(LocalLlmIntentParseFailure failure, string message)
                : base(message)
            {
                Failure = failure;
            }

            public LocalLlmIntentParseFailure Failure { get; }
        }

        private sealed class JsonCursor
        {
            private readonly string text;
            private int index;

            public JsonCursor(string text)
            {
                this.text = text;
            }

            public bool AtEnd => index == text.Length;

            public void SkipWhitespace()
            {
                while (index < text.Length)
                {
                    char value = text[index];
                    if (value != ' ' && value != '\t' && value != '\r' && value != '\n')
                    {
                        break;
                    }
                    ++index;
                }
            }

            public void Require(char expected)
            {
                if (index >= text.Length || text[index] != expected)
                {
                    throw InvalidJson($"Expected '{expected}'.");
                }
                ++index;
            }

            public string ReadPropertyName()
            {
                string name = ReadString();
                return name;
            }

            public bool ReadIntegerOne()
            {
                if (index < text.Length && text[index] == '1')
                {
                    ++index;
                    if (index == text.Length || !char.IsDigit(text[index]))
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool TryReadLiteral(string literal)
            {
                if (literal == null)
                {
                    throw new ArgumentNullException(nameof(literal));
                }
                if (text.Length - index < literal.Length ||
                    string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                {
                    return false;
                }
                index += literal.Length;
                return true;
            }

            public string ReadString()
            {
                Require('"');
                var builder = new StringBuilder();
                while (index < text.Length)
                {
                    char value = text[index++];
                    if (value == '"')
                    {
                        return builder.ToString();
                    }
                    if (value < 0x20)
                    {
                        throw InvalidJson("JSON strings cannot contain unescaped control characters.");
                    }
                    if (value != '\\')
                    {
                        AppendBounded(builder, value.ToString());
                        continue;
                    }
                    if (index >= text.Length)
                    {
                        throw InvalidJson("JSON string escape is incomplete.");
                    }
                    char escaped = text[index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            AppendBounded(builder, escaped.ToString());
                            break;
                        case 'b':
                            AppendBounded(builder, "\b");
                            break;
                        case 'f':
                            AppendBounded(builder, "\f");
                            break;
                        case 'n':
                            AppendBounded(builder, "\n");
                            break;
                        case 'r':
                            AppendBounded(builder, "\r");
                            break;
                        case 't':
                            AppendBounded(builder, "\t");
                            break;
                        case 'u':
                            AppendUnicodeEscape(builder);
                            break;
                        default:
                            throw InvalidJson("JSON string contains an unsupported escape.");
                    }
                }
                throw InvalidJson("JSON string is unterminated.");
            }

            private void AppendUnicodeEscape(StringBuilder builder)
            {
                int first = ReadHexQuad();
                if (first >= 0xD800 && first <= 0xDBFF)
                {
                    if (index + 2 > text.Length || text[index] != '\\' || text[index + 1] != 'u')
                    {
                        throw InvalidJson("High surrogate must be followed by a low surrogate escape.");
                    }
                    index += 2;
                    int second = ReadHexQuad();
                    if (second < 0xDC00 || second > 0xDFFF)
                    {
                        throw InvalidJson("High surrogate is not followed by a low surrogate.");
                    }
                    int scalar = 0x10000 + ((first - 0xD800) << 10) + (second - 0xDC00);
                    AppendBounded(builder, char.ConvertFromUtf32(scalar));
                    return;
                }
                if (first >= 0xDC00 && first <= 0xDFFF)
                {
                    throw InvalidJson("Low surrogate cannot appear without a preceding high surrogate.");
                }
                AppendBounded(builder, ((char)first).ToString());
            }

            private int ReadHexQuad()
            {
                if (index + 4 > text.Length)
                {
                    throw InvalidJson("Unicode escape is incomplete.");
                }
                int value = 0;
                for (int count = 0; count < 4; ++count)
                {
                    int digit = HexValue(text[index++]);
                    if (digit < 0)
                    {
                        throw InvalidJson("Unicode escape contains a non-hexadecimal digit.");
                    }
                    value = (value << 4) | digit;
                }
                return value;
            }

            private static int HexValue(char value)
            {
                if (value >= '0' && value <= '9')
                {
                    return value - '0';
                }
                if (value >= 'a' && value <= 'f')
                {
                    return value - 'a' + 10;
                }
                if (value >= 'A' && value <= 'F')
                {
                    return value - 'A' + 10;
                }
                return -1;
            }

            private static void AppendBounded(StringBuilder builder, string value)
            {
                if (builder.Length > MaximumStringCharacters - value.Length)
                {
                    throw InvalidJson("JSON string exceeds the local behavior-intent bound.");
                }
                builder.Append(value);
            }

            private static IntentParseException InvalidJson(string message)
            {
                return new IntentParseException(
                    LocalLlmIntentParseFailure.InvalidJson,
                    message);
            }
        }
    }
}

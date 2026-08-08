#nullable enable

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReachyMini.LocalModels
{
    internal static class LocalLlmBehaviorContract
    {
        internal const string ManifestId = "rma133.qwen3-0.6b-q4-k-m.v1";
        internal const string ModelId = "qwen3-0.6b";
        internal const long ArtifactBytes = 396704416L;
        internal const string ArtifactSha256 =
            "b0638f08417a2d3c8652760462eb5407c6e30173cf9608ad0820757a281eea0e";
        internal const string UserPromptSuffix = "/no_think";
        internal const string GrammarRoot = "root";
        internal const string SystemPromptSha256 =
            "0f174887e7686da42d88d7bddea28c4a5399b8006d2e3ad71715340c84c10e20";
        internal const string GrammarSha256 =
            "2c333f6bb576e025c80b0e4050bbc816247817ebe6f145361360e6eec71eb734";

        internal const string SystemPrompt =
            "You are Reachy Mini's high-level behavior-intent generator. Return exactly one JSON object and nothing else. Do not emit Markdown, explanations, reasoning, XML, thinking tags, or executable code. Never emit joint angles, motor commands, torques, velocities, raw positions, or Cartesian coordinates.\n" +
            "\n" +
            "Every response MUST contain these required top-level keys: schema_version, speech, expression, gesture, urgency. schema_version MUST be the JSON integer 1. It is WRONG to write \"schema_version\":\"1\". speech must be a short natural reply to the user, no longer than 160 characters; do not copy or paraphrase the scenario instructions. expression must be one of neutral, attentive, curious, pleased, concerned, surprised. gesture must be one of none, nod, small_head_tilt, recoil. urgency must be one of low, normal, high.\n" +
            "\n" +
            "gaze_target is conditional. If the scenario asks to look at a CURRENT VALID tracked entity ID, you MUST include gaze_target exactly as {\"kind\":\"tracked_entity\",\"entity_id\":\"entity-N\"} using that exact provided ID. If the requested target is stale, ambiguous, unavailable, not tracked, or no gaze is requested, you MUST omit gaze_target. Never invent or substitute an entity ID.\n" +
            "\n" +
            "For raw actuator, motor, joint, torque, velocity, angle, position, or coordinate requests: refuse briefly in speech, do not repeat the requested command or value, and never add raw-actuation fields.\n" +
            "\n" +
            "Examples below illustrate FORMAT only; never copy their content.\n" +
            "\n" +
            "Valid tracked gaze example:\n" +
            "{\"schema_version\":1,\"speech\":\"Sure.\",\"gaze_target\":{\"kind\":\"tracked_entity\",\"entity_id\":\"entity-99\"},\"expression\":\"attentive\",\"gesture\":\"none\",\"urgency\":\"normal\"}\n" +
            "\n" +
            "No-gaze example:\n" +
            "{\"schema_version\":1,\"speech\":\"Which one do you mean?\",\"expression\":\"attentive\",\"gesture\":\"none\",\"urgency\":\"normal\"}\n" +
            "\n" +
            "Unsafe raw-actuation example:\n" +
            "{\"schema_version\":1,\"speech\":\"I can't issue raw actuator commands.\",\"expression\":\"neutral\",\"gesture\":\"none\",\"urgency\":\"normal\"}\n" +
            "\n" +
            "Before output, verify that schema_version is numeric 1, all required keys are present, gaze_target is included exactly when required, speech responds naturally rather than echoing instructions, and the output is only one JSON object.\n";

        internal const string Grammar =
            "root ::= object ws\n" +
            "object ::= \"{\" ws schema-member ws \",\" ws speech-member ws \",\" ws expression-member ws \",\" ws gesture-member ws \",\" ws urgency-member ws \"}\" | \"{\" ws schema-member ws \",\" ws speech-member ws \",\" ws gaze-member ws \",\" ws expression-member ws \",\" ws gesture-member ws \",\" ws urgency-member ws \"}\"\n" +
            "schema-member ::= \"\\\"schema_version\\\"\" ws \":\" ws \"1\"\n" +
            "speech-member ::= \"\\\"speech\\\"\" ws \":\" ws string\n" +
            "gaze-member ::= \"\\\"gaze_target\\\"\" ws \":\" ws (\"null\" | gaze-object)\n" +
            "gaze-object ::= \"{\" ws \"\\\"kind\\\"\" ws \":\" ws \"\\\"tracked_entity\\\"\" ws \",\" ws \"\\\"entity_id\\\"\" ws \":\" ws entity-id ws \"}\"\n" +
            "expression-member ::= \"\\\"expression\\\"\" ws \":\" ws expression\n" +
            "gesture-member ::= \"\\\"gesture\\\"\" ws \":\" ws gesture\n" +
            "urgency-member ::= \"\\\"urgency\\\"\" ws \":\" ws urgency\n" +
            "expression ::= \"\\\"neutral\\\"\" | \"\\\"attentive\\\"\" | \"\\\"curious\\\"\" | \"\\\"pleased\\\"\" | \"\\\"concerned\\\"\" | \"\\\"surprised\\\"\"\n" +
            "gesture ::= \"\\\"none\\\"\" | \"\\\"nod\\\"\" | \"\\\"small_head_tilt\\\"\" | \"\\\"recoil\\\"\"\n" +
            "urgency ::= \"\\\"low\\\"\" | \"\\\"normal\\\"\" | \"\\\"high\\\"\"\n" +
            "entity-id ::= \"\\\"entity-\" digit+ \"\\\"\"\n" +
            "string ::= \"\\\"\" char* \"\\\"\"\n" +
            "char ::= [^\"\\\\\\x7F\\x00-\\x1F] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" hex hex hex hex)\n" +
            "hex ::= [0-9a-fA-F]\n" +
            "digit ::= [0-9]\n" +
            "ws ::= [ \\t\\n\\r]*\n";

        internal static void ValidateFrozenBytes()
        {
            if (!string.Equals(
                    Sha256(SystemPrompt),
                    SystemPromptSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The embedded RMA-134 system prompt does not match the frozen RMA-133 V6 contract.");
            }
            if (!string.Equals(
                    Sha256(Grammar),
                    GrammarSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The embedded RMA-134 grammar does not match the frozen RMA-133 V6 contract.");
            }
        }

        internal static void ValidateSelectedInputs(
            LocalModelManifest manifest,
            LocalModelApprovedArtifact artifact)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }
            if (!string.Equals(manifest.Identity.ManifestId, ManifestId, StringComparison.Ordinal) ||
                !string.Equals(manifest.Identity.ModelId, ModelId, StringComparison.Ordinal) ||
                manifest.Artifact.FileSizeBytes != ArtifactBytes ||
                !string.Equals(manifest.Artifact.Sha256, ArtifactSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "RMA-134 production behavior generation requires the exact RMA-133-selected Qwen3 manifest.",
                    nameof(manifest));
            }
            if (!string.Equals(artifact.ManifestId, manifest.Identity.ManifestId, StringComparison.Ordinal) ||
                !string.Equals(artifact.ModelId, manifest.Identity.ModelId, StringComparison.Ordinal) ||
                artifact.FileSizeBytes != manifest.Artifact.FileSizeBytes ||
                !string.Equals(artifact.Sha256, manifest.Artifact.Sha256, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The approved local-model artifact does not match the selected manifest identity.",
                    nameof(artifact));
            }
            if (!string.Equals(manifest.Runtime.RuntimeId, "reachy_llama", StringComparison.Ordinal) ||
                manifest.Runtime.AbiVersion != LocalModelManifestPolicy.ReachyLlamaAbiVersion ||
                manifest.Runtime.RequiresNetworkAccess)
            {
                throw new ArgumentException(
                    "The selected local-model manifest does not match the offline reachy_llama ABI-2 runtime contract.",
                    nameof(manifest));
            }
        }

        internal static bool TryParseIntent(
            string response,
            out LocalLlmBehaviorIntent? intent,
            out string detail)
        {
            intent = null;
            if (response == null)
            {
                detail = "Behavior intent response is null.";
                return false;
            }
            if (response.Contains('\0'))
            {
                detail = "Behavior intent response contains an embedded NUL character.";
                return false;
            }

            try
            {
                var parser = new Parser(response);
                intent = parser.Parse();
                detail = string.Empty;
                return true;
            }
            catch (FormatException exception)
            {
                detail = LocalLlmGenerationResult.BoundDiagnostic(exception.Message);
                return false;
            }
        }

        internal static string Sha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
            {
                digest = algorithm.ComputeHash(bytes);
            }
            var builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
            {
                builder.Append(
                    digest[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;

            public Parser(string text)
            {
                this.text = text;
            }

            public LocalLlmBehaviorIntent Parse()
            {
                SkipWhitespace();
                Expect('{');
                SkipWhitespace();

                ExpectProperty("schema_version");
                ExpectColon();
                SkipWhitespace();
                Expect('1');
                SkipWhitespace();
                ExpectComma();

                ExpectProperty("speech");
                ExpectColon();
                string speech = ReadStringValue();
                if (CountUnicodeScalars(speech) > 160)
                {
                    throw Error("Behavior intent speech exceeds 160 Unicode characters.");
                }
                SkipWhitespace();
                ExpectComma();

                string nextProperty = ReadPropertyName();
                ExpectColon();
                LocalLlmGazeTarget? gazeTarget = null;
                if (string.Equals(nextProperty, "gaze_target", StringComparison.Ordinal))
                {
                    gazeTarget = ReadGazeTarget();
                    SkipWhitespace();
                    ExpectComma();
                    ExpectProperty("expression");
                    ExpectColon();
                }
                else if (!string.Equals(nextProperty, "expression", StringComparison.Ordinal))
                {
                    throw Error("Behavior intent contains an unknown or out-of-order property.");
                }

                LocalLlmExpression expression = ParseExpression(ReadStringValue());
                SkipWhitespace();
                ExpectComma();
                ExpectProperty("gesture");
                ExpectColon();
                LocalLlmGesture gesture = ParseGesture(ReadStringValue());
                SkipWhitespace();
                ExpectComma();
                ExpectProperty("urgency");
                ExpectColon();
                LocalLlmUrgency urgency = ParseUrgency(ReadStringValue());
                SkipWhitespace();
                Expect('}');
                SkipWhitespace();
                if (index != text.Length)
                {
                    throw Error("Behavior intent has trailing bytes or prose after the JSON object.");
                }

                return new LocalLlmBehaviorIntent(
                    speech,
                    gazeTarget,
                    expression,
                    gesture,
                    urgency);
            }

            private LocalLlmGazeTarget? ReadGazeTarget()
            {
                SkipWhitespace();
                if (TryConsumeLiteral("null"))
                {
                    return null;
                }

                Expect('{');
                SkipWhitespace();
                ExpectProperty("kind");
                ExpectColon();
                string kind = ReadStringValue();
                if (!string.Equals(kind, "tracked_entity", StringComparison.Ordinal))
                {
                    throw Error("Behavior intent gaze kind must be tracked_entity.");
                }
                SkipWhitespace();
                ExpectComma();
                ExpectProperty("entity_id");
                ExpectColon();
                string entityId = ReadStringValue();
                if (!IsTrackedEntityId(entityId))
                {
                    throw Error("Behavior intent gaze entity_id must match entity-[0-9]+.");
                }
                SkipWhitespace();
                Expect('}');
                return new LocalLlmGazeTarget(entityId);
            }

            private string ReadPropertyName()
            {
                SkipWhitespace();
                return ReadString();
            }

            private void ExpectProperty(string expected)
            {
                string actual = ReadPropertyName();
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw Error(
                        "Behavior intent expected property '" + expected + "'.");
                }
            }

            private string ReadStringValue()
            {
                SkipWhitespace();
                return ReadString();
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
                    if (character < 0x20 || character == 0x7f)
                    {
                        throw Error("Behavior intent JSON string contains an unescaped control character.");
                    }
                    if (char.IsHighSurrogate(character))
                    {
                        if (index >= text.Length || !char.IsLowSurrogate(text[index]))
                        {
                            throw Error("Behavior intent JSON string contains an invalid Unicode surrogate pair.");
                        }
                        builder.Append(character);
                        builder.Append(text[index++]);
                        continue;
                    }
                    if (char.IsLowSurrogate(character))
                    {
                        throw Error("Behavior intent JSON string contains an unexpected low Unicode surrogate.");
                    }
                    builder.Append(character);
                }
                throw Error("Behavior intent JSON string is unterminated.");
            }

            private void ReadEscape(StringBuilder builder)
            {
                if (index >= text.Length)
                {
                    throw Error("Behavior intent JSON escape is incomplete.");
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
                        throw Error("Behavior intent JSON string contains an invalid escape sequence.");
                }
            }

            private void ReadUnicodeEscape(StringBuilder builder)
            {
                char first = ReadHexCodeUnit();
                if (char.IsHighSurrogate(first))
                {
                    if (index + 1 >= text.Length || text[index] != '\\' || text[index + 1] != 'u')
                    {
                        throw Error("Behavior intent JSON string contains an incomplete escaped surrogate pair.");
                    }
                    index += 2;
                    char second = ReadHexCodeUnit();
                    if (!char.IsLowSurrogate(second))
                    {
                        throw Error("Behavior intent JSON string contains an invalid escaped surrogate pair.");
                    }
                    builder.Append(first);
                    builder.Append(second);
                    return;
                }
                if (char.IsLowSurrogate(first))
                {
                    throw Error("Behavior intent JSON string contains an unexpected escaped low surrogate.");
                }
                builder.Append(first);
            }

            private char ReadHexCodeUnit()
            {
                if (index + 4 > text.Length)
                {
                    throw Error("Behavior intent JSON Unicode escape is incomplete.");
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
                        throw Error("Behavior intent JSON Unicode escape contains a non-hexadecimal digit.");
                    }
                    value = (value << 4) | digit;
                }
                return (char)value;
            }

            private void ExpectColon()
            {
                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
            }

            private void ExpectComma()
            {
                Expect(',');
                SkipWhitespace();
            }

            private void Expect(char expected)
            {
                if (index >= text.Length || text[index] != expected)
                {
                    throw Error(
                        "Behavior intent JSON expected '" + expected + "'.");
                }
                ++index;
            }

            private bool TryConsumeLiteral(string literal)
            {
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

            private void SkipWhitespace()
            {
                while (index < text.Length)
                {
                    char character = text[index];
                    if (character != ' ' && character != '\t' &&
                        character != '\n' && character != '\r')
                    {
                        break;
                    }
                    ++index;
                }
            }

            private static FormatException Error(string message)
            {
                return new FormatException(message);
            }
        }

        private static LocalLlmExpression ParseExpression(string value)
        {
            switch (value)
            {
                case "neutral": return LocalLlmExpression.Neutral;
                case "attentive": return LocalLlmExpression.Attentive;
                case "curious": return LocalLlmExpression.Curious;
                case "pleased": return LocalLlmExpression.Pleased;
                case "concerned": return LocalLlmExpression.Concerned;
                case "surprised": return LocalLlmExpression.Surprised;
                default:
                    throw new FormatException("Behavior intent expression is outside the allowed enum.");
            }
        }

        private static LocalLlmGesture ParseGesture(string value)
        {
            switch (value)
            {
                case "none": return LocalLlmGesture.None;
                case "nod": return LocalLlmGesture.Nod;
                case "small_head_tilt": return LocalLlmGesture.SmallHeadTilt;
                case "recoil": return LocalLlmGesture.Recoil;
                default:
                    throw new FormatException("Behavior intent gesture is outside the allowed enum.");
            }
        }

        private static LocalLlmUrgency ParseUrgency(string value)
        {
            switch (value)
            {
                case "low": return LocalLlmUrgency.Low;
                case "normal": return LocalLlmUrgency.Normal;
                case "high": return LocalLlmUrgency.High;
                default:
                    throw new FormatException("Behavior intent urgency is outside the allowed enum.");
            }
        }

        private static bool IsTrackedEntityId(string value)
        {
            const string prefix = "entity-";
            if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length)
            {
                return false;
            }
            for (int index = prefix.Length; index < value.Length; ++index)
            {
                char character = value[index];
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static int CountUnicodeScalars(string value)
        {
            int count = 0;
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    {
                        throw new FormatException("Behavior intent speech contains an invalid Unicode surrogate pair.");
                    }
                    ++index;
                }
                else if (char.IsLowSurrogate(character))
                {
                    throw new FormatException("Behavior intent speech contains an unexpected low Unicode surrogate.");
                }
                ++count;
            }
            return count;
        }
    }
}

#nullable enable

using System;

namespace ReachyMini.Behavior
{
    public static class ReachyBehaviorIntentJsonParser
    {
        public static ReachyBehaviorIntentValidationResult Validate(string? json)
        {
            if (json == null)
            {
                return Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "null-json",
                    "Behavior intent JSON is null.");
            }
            if (json.Length == 0)
            {
                return Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "empty-json",
                    "Behavior intent JSON is empty.");
            }
            if (json.Contains('\0'))
            {
                return Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "embedded-nul",
                    "Behavior intent JSON contains an embedded NUL character.");
            }

            try
            {
                ReachyBehaviorIntent intent = Parse(json);
                return new ReachyBehaviorIntentValidationResult(
                    ReachyBehaviorIntentValidationStatus.Valid,
                    "valid-behavior-intent",
                    string.Empty,
                    intent);
            }
            catch (ReachyBehaviorIntentValidationException exception)
            {
                return Failure(
                    exception.Status,
                    exception.DiagnosticCode,
                    exception.Message);
            }
        }

        private static ReachyBehaviorIntent Parse(string json)
        {
            var reader = new ReachyBehaviorIntentJsonReader(json);
            reader.Expect('{');

            bool sawSchemaVersion = false;
            bool sawSpeech = false;
            bool sawGazeTarget = false;
            bool sawExpression = false;
            bool sawGesture = false;
            bool sawUrgency = false;
            bool sawTiming = false;
            string? speech = null;
            ReachyBehaviorGazeTarget? gazeTarget = null;
            ReachyBehaviorExpression? expression = null;
            ReachyBehaviorGesture? gesture = null;
            ReachyBehaviorUrgency? urgency = null;
            ReachyBehaviorTimingConstraints? timing = null;
            int propertyCount = 0;

            bool first = true;
            while (!reader.TryConsume('}'))
            {
                if (!first)
                {
                    reader.Expect(',');
                }
                first = false;
                ++propertyCount;
                if (propertyCount > ReachyBehaviorIntentPolicy.MaximumTopLevelProperties)
                {
                    throw ReachyBehaviorIntentJsonReader.Failure(
                        ReachyBehaviorIntentValidationStatus.BoundExceeded,
                        "too-many-top-level-properties",
                        "Behavior intent contains too many top-level properties.");
                }

                string property = reader.ReadString();
                reader.Expect(':');
                switch (property)
                {
                    case "schema_version":
                        EnsureNotDuplicate(sawSchemaVersion, property);
                        sawSchemaVersion = true;
                        ParseSchemaVersion(reader);
                        break;
                    case "speech":
                        EnsureNotDuplicate(sawSpeech, property);
                        sawSpeech = true;
                        speech = ParseSpeech(reader);
                        break;
                    case "gaze_target":
                        EnsureNotDuplicate(sawGazeTarget, property);
                        sawGazeTarget = true;
                        gazeTarget = ParseGazeTarget(reader);
                        break;
                    case "expression":
                        EnsureNotDuplicate(sawExpression, property);
                        sawExpression = true;
                        expression = ParseExpression(reader.ReadString());
                        break;
                    case "gesture":
                        EnsureNotDuplicate(sawGesture, property);
                        sawGesture = true;
                        gesture = ParseGesture(reader.ReadString());
                        break;
                    case "urgency":
                        EnsureNotDuplicate(sawUrgency, property);
                        sawUrgency = true;
                        urgency = ParseUrgency(reader.ReadString());
                        break;
                    case "timing":
                        EnsureNotDuplicate(sawTiming, property);
                        sawTiming = true;
                        timing = ParseTiming(reader);
                        break;
                    default:
                        throw ReachyBehaviorIntentJsonReader.Failure(
                            ReachyBehaviorIntentValidationStatus.UnknownProperty,
                            "unknown-top-level-property",
                            "Behavior intent contains an unknown or unsafe top-level property.");
                }
            }

            if (!sawSchemaVersion)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "schema-version-required",
                    "Behavior intent requires schema_version.");
            }
            if (!reader.IsAtEnd)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "trailing-json-content",
                    "Behavior intent has trailing bytes or prose after the JSON object.");
            }

            return new ReachyBehaviorIntent(
                speech,
                gazeTarget,
                expression,
                gesture,
                urgency,
                timing);
        }

        private static void ParseSchemaVersion(ReachyBehaviorIntentJsonReader reader)
        {
            int version = reader.ReadNonNegativeInteger(
                int.MaxValue,
                "schema-version-out-of-range");
            if (version != ReachyBehaviorIntentPolicy.CurrentSchemaVersion)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.UnsupportedSchemaVersion,
                    "unsupported-schema-version",
                    "Behavior intent schema_version is unsupported.");
            }
        }

        private static string ParseSpeech(ReachyBehaviorIntentJsonReader reader)
        {
            string speech = reader.ReadString();
            int scalarCount = ReachyBehaviorIntentJsonReader.CountUnicodeScalars(
                speech,
                "speech-invalid-unicode");
            if (scalarCount == 0 ||
                scalarCount > ReachyBehaviorIntentPolicy.MaximumSpeechCharacters)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.BoundExceeded,
                    "speech-length-out-of-range",
                    "Behavior intent speech must contain 1 to 160 Unicode characters.");
            }
            return speech;
        }

        private static ReachyBehaviorGazeTarget? ParseGazeTarget(
            ReachyBehaviorIntentJsonReader reader)
        {
            if (reader.TryConsumeLiteral("null"))
            {
                return null;
            }

            reader.Expect('{');
            bool sawKind = false;
            bool sawEntityId = false;
            string? kind = null;
            string? entityId = null;
            int propertyCount = 0;
            bool first = true;
            while (!reader.TryConsume('}'))
            {
                if (!first)
                {
                    reader.Expect(',');
                }
                first = false;
                ++propertyCount;
                if (propertyCount > ReachyBehaviorIntentPolicy.MaximumGazeTargetProperties)
                {
                    throw ReachyBehaviorIntentJsonReader.Failure(
                        ReachyBehaviorIntentValidationStatus.BoundExceeded,
                        "too-many-gaze-properties",
                        "Behavior intent gaze_target contains too many properties.");
                }

                string property = reader.ReadString();
                reader.Expect(':');
                switch (property)
                {
                    case "kind":
                        EnsureNotDuplicate(sawKind, property);
                        sawKind = true;
                        kind = reader.ReadString();
                        break;
                    case "entity_id":
                        EnsureNotDuplicate(sawEntityId, property);
                        sawEntityId = true;
                        entityId = reader.ReadString();
                        break;
                    default:
                        throw ReachyBehaviorIntentJsonReader.Failure(
                            ReachyBehaviorIntentValidationStatus.UnknownProperty,
                            "unknown-gaze-property",
                            "Behavior intent gaze_target contains an unknown property.");
                }
            }

            if (!sawKind || !sawEntityId)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "incomplete-gaze-target",
                    "Behavior intent gaze_target requires kind and entity_id.");
            }
            if (!string.Equals(kind, "tracked_entity", StringComparison.Ordinal))
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "unsupported-gaze-kind",
                    "Behavior intent gaze_target kind must be tracked_entity.");
            }
            if (!IsTrackedEntityId(entityId))
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "invalid-gaze-entity-id",
                    "Behavior intent gaze_target entity_id must match entity-[0-9]+ within bounds.");
            }

            return new ReachyBehaviorGazeTarget(entityId!);
        }

        private static ReachyBehaviorTimingConstraints ParseTiming(
            ReachyBehaviorIntentJsonReader reader)
        {
            reader.Expect('{');
            bool sawStartDelay = false;
            bool sawMaximumDuration = false;
            int? startDelay = null;
            int? maximumDuration = null;
            int propertyCount = 0;
            bool first = true;
            while (!reader.TryConsume('}'))
            {
                if (!first)
                {
                    reader.Expect(',');
                }
                first = false;
                ++propertyCount;
                if (propertyCount > ReachyBehaviorIntentPolicy.MaximumTimingProperties)
                {
                    throw ReachyBehaviorIntentJsonReader.Failure(
                        ReachyBehaviorIntentValidationStatus.BoundExceeded,
                        "too-many-timing-properties",
                        "Behavior intent timing contains too many properties.");
                }

                string property = reader.ReadString();
                reader.Expect(':');
                switch (property)
                {
                    case "start_delay_ms":
                        EnsureNotDuplicate(sawStartDelay, property);
                        sawStartDelay = true;
                        startDelay = reader.ReadNonNegativeInteger(
                            ReachyBehaviorIntentPolicy.MaximumStartDelayMilliseconds,
                            "start-delay-out-of-range");
                        break;
                    case "maximum_duration_ms":
                        EnsureNotDuplicate(sawMaximumDuration, property);
                        sawMaximumDuration = true;
                        maximumDuration = reader.ReadNonNegativeInteger(
                            ReachyBehaviorIntentPolicy.MaximumDurationMilliseconds,
                            "maximum-duration-out-of-range");
                        if (maximumDuration <
                            ReachyBehaviorIntentPolicy.MinimumMaximumDurationMilliseconds)
                        {
                            throw ReachyBehaviorIntentJsonReader.Failure(
                                ReachyBehaviorIntentValidationStatus.InvalidValue,
                                "maximum-duration-out-of-range",
                                "Behavior intent maximum_duration_ms must be at least 1.");
                        }
                        break;
                    default:
                        throw ReachyBehaviorIntentJsonReader.Failure(
                            ReachyBehaviorIntentValidationStatus.UnknownProperty,
                            "unknown-timing-property",
                            "Behavior intent timing contains an unknown property.");
                }
            }

            if (!sawStartDelay && !sawMaximumDuration)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "empty-timing-constraints",
                    "Behavior intent timing must contain at least one constraint.");
            }
            return new ReachyBehaviorTimingConstraints(startDelay, maximumDuration);
        }

        private static ReachyBehaviorExpression ParseExpression(string value)
        {
            return value switch
            {
                "neutral" => ReachyBehaviorExpression.Neutral,
                "attentive" => ReachyBehaviorExpression.Attentive,
                "curious" => ReachyBehaviorExpression.Curious,
                "pleased" => ReachyBehaviorExpression.Pleased,
                "concerned" => ReachyBehaviorExpression.Concerned,
                "surprised" => ReachyBehaviorExpression.Surprised,
                _ => throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "unsupported-expression",
                    "Behavior intent expression is outside the allowed enum."),
            };
        }

        private static ReachyBehaviorGesture ParseGesture(string value)
        {
            return value switch
            {
                "none" => ReachyBehaviorGesture.None,
                "nod" => ReachyBehaviorGesture.Nod,
                "small_head_tilt" => ReachyBehaviorGesture.SmallHeadTilt,
                "recoil" => ReachyBehaviorGesture.Recoil,
                _ => throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "unsupported-gesture",
                    "Behavior intent gesture is outside the allowed enum."),
            };
        }

        private static ReachyBehaviorUrgency ParseUrgency(string value)
        {
            return value switch
            {
                "low" => ReachyBehaviorUrgency.Low,
                "normal" => ReachyBehaviorUrgency.Normal,
                "high" => ReachyBehaviorUrgency.High,
                _ => throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidValue,
                    "unsupported-urgency",
                    "Behavior intent urgency is outside the allowed enum."),
            };
        }

        private static bool IsTrackedEntityId(string? value)
        {
            const string prefix = "entity-";
            if (string.IsNullOrEmpty(value) ||
                value.Length > ReachyBehaviorIntentPolicy.MaximumEntityIdCharacters ||
                !value.StartsWith(prefix, StringComparison.Ordinal) ||
                value.Length == prefix.Length)
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

        private static void EnsureNotDuplicate(bool alreadySeen, string property)
        {
            if (alreadySeen)
            {
                throw ReachyBehaviorIntentJsonReader.Failure(
                    ReachyBehaviorIntentValidationStatus.InvalidJson,
                    "duplicate-json-property",
                    "Behavior intent contains duplicate property '" + property + "'.");
            }
        }

        private static ReachyBehaviorIntentValidationResult Failure(
            ReachyBehaviorIntentValidationStatus status,
            string diagnosticCode,
            string detail)
        {
            return new ReachyBehaviorIntentValidationResult(
                status,
                diagnosticCode,
                detail,
                null);
        }
    }
}

#nullable enable

using System;

namespace ReachyMini.Perception
{
    public sealed partial class BoundedWorldModel
    {
        private static bool SameObservationContinuity(
            ReachyVisionFrameIdentity left,
            ReachyVisionFrameIdentity right)
        {
            return string.Equals(
                    left.CameraId,
                    right.CameraId,
                    StringComparison.Ordinal) &&
                left.SourceSessionId == right.SourceSessionId &&
                left.ContinuityId == right.ContinuityId;
        }

        private static bool HasObservation(
            EntityState entity,
            ReachyVisionFrameIdentity identity)
        {
            for (int index = 0; index < entity.Observations.Count; ++index)
            {
                WorldObservationSnapshot observation =
                    entity.Observations[index];
                if (observation.SourceSequence == identity.SourceSequence &&
                    observation.TimestampNanoseconds ==
                        identity.SourceTimestampNanoseconds &&
                    observation.AuthoritativeSequence ==
                        identity.AuthoritativeSequence)
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeDescription(string text)
        {
            string trimmed = text.Trim();
            var characters = new char[trimmed.Length];
            int count = 0;
            bool previousWhitespace = false;
            for (int index = 0; index < trimmed.Length; ++index)
            {
                char character = trimmed[index];
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWhitespace)
                    {
                        characters[count] = ' ';
                        count = checked(count + 1);
                        previousWhitespace = true;
                    }
                }
                else
                {
                    characters[count] = character;
                    count = checked(count + 1);
                    previousWhitespace = false;
                }
            }
            return new string(characters, 0, count);
        }
    }
}

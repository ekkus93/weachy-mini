#nullable enable

using System;

namespace ReachyMini.Perception
{
    internal static class ReachyOpenAiVisionDiagnosticTokens
    {
        internal static string RequireSafeToken(
            string value,
            string name,
            int maximumLength)
        {
            string text = ProviderDescriptor.RequireText(value, name);
            if (text.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(name);
            }
            for (int index = 0; index < text.Length; ++index)
            {
                char character = text[index];
                bool valid =
                    (character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '.' ||
                    character == '_' ||
                    character == '-';
                if (!valid)
                {
                    throw new ArgumentException(
                        "Diagnostic tokens may contain only ASCII letters, digits, '.', '_' and '-'.",
                        name);
                }
            }
            return text;
        }
    }
}

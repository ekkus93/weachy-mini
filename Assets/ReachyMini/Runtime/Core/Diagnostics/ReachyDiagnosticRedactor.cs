#nullable enable

using System;

namespace ReachyMini.Diagnostics
{
    public static class ReachyDiagnosticRedactor
    {
        public const string RedactedValue = "[redacted]";
        public const int MaximumValueCharacters = 512;

        public static ReachyDiagnosticField Redact(ReachyDiagnosticField field)
        {
            string value = field.DataClass switch
            {
                ReachyDiagnosticDataClass.Secret => RedactedValue,
                ReachyDiagnosticDataClass.PrivateText => RedactedValue,
                ReachyDiagnosticDataClass.RawAudio => RedactedValue,
                ReachyDiagnosticDataClass.RawImage => RedactedValue,
                ReachyDiagnosticDataClass.RawMedia => RedactedValue,
                ReachyDiagnosticDataClass.Header => RedactHeader(field.Key, field.Value),
                ReachyDiagnosticDataClass.Url => RedactUrl(field.Value),
                _ => RedactPotentialSecretText(field.Value),
            };
            return new ReachyDiagnosticField(
                field.Key,
                Bound(value),
                ReachyDiagnosticDataClass.Public);
        }

        private static string RedactHeader(string key, string value)
        {
            string normalized = key.Trim();
            if (normalized.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("proxy-authorization", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("set-cookie", StringComparison.OrdinalIgnoreCase) ||
                normalized.IndexOf("api-key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RedactedValue;
            }
            return RedactPotentialSecretText(value);
        }

        private static string RedactUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri == null)
            {
                return "[invalid-url]";
            }
            string authority = uri.IsDefaultPort
                ? uri.Host
                : uri.Host + ":" + uri.Port;
            string path = string.IsNullOrEmpty(uri.AbsolutePath)
                ? "/"
                : uri.AbsolutePath;
            return uri.Scheme + "://" + authority + path;
        }

        private static string RedactPotentialSecretText(string value)
        {
            string text = value ?? string.Empty;
            foreach (string marker in new[]
                     {
                         "bearer ",
                         "api_key=",
                         "apikey=",
                         "api-key=",
                         "token=",
                         "access_token=",
                         "secret=",
                         "password=",
                     })
            {
                int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    return text.Substring(0, index + marker.Length) + RedactedValue;
                }
            }
            return text;
        }

        private static string Bound(string value)
        {
            string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= MaximumValueCharacters
                ? normalized
                : normalized.Substring(0, MaximumValueCharacters - 1) + "…";
        }
    }
}

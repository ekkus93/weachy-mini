#nullable enable

using System;
using System.Globalization;
using System.Text;

namespace ReachyMini.Diagnostics
{
    public static class ReachyDiagnosticJsonFormatter
    {
        public static string Format(ReachyDiagnosticRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            var builder = new StringBuilder(1024);
            builder.Append('{');
            Property(builder, "component", record.Descriptor.Component, first: true);
            Property(builder, "severity", record.Descriptor.Severity.ToString(), first: false);
            Property(builder, "event_id", record.Descriptor.EventId, first: false);
            Property(builder, "error_category", record.Descriptor.ErrorCategory.ToString(), first: false);
            Number(builder, "monotonic_ms", record.MonotonicMilliseconds);
            Property(builder, "session_id", record.Context.SessionId, first: false);
            Property(builder, "turn_id", record.Context.TurnId, first: false);
            Number(builder, "occurrence_count", record.OccurrenceCount);
            Number(builder, "suppressed_count", record.SuppressedCount);
            Boolean(builder, "rate_limit_summary", record.IsRateLimitSummary);
            builder.Append(",\"fields\":{");
            for (int index = 0; index < record.Fields.Count; ++index)
            {
                ReachyDiagnosticField field = record.Fields[index];
                if (index > 0)
                {
                    builder.Append(',');
                }
                String(builder, field.Key);
                builder.Append(':');
                String(builder, field.Value);
            }
            builder.Append("}}");
            return builder.ToString();
        }

        private static void Property(
            StringBuilder builder,
            string name,
            string value,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            String(builder, name);
            builder.Append(':');
            String(builder, value);
        }

        private static void Number(StringBuilder builder, string name, long value)
        {
            builder.Append(',');
            String(builder, name);
            builder.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Number(StringBuilder builder, string name, ulong value)
        {
            builder.Append(',');
            String(builder, name);
            builder.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Boolean(StringBuilder builder, string name, bool value)
        {
            builder.Append(',');
            String(builder, name);
            builder.Append(':').Append(value ? "true" : "false");
        }

        private static void String(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int index = 0; index < value.Length; ++index)
            {
                char current = value[index];
                switch (current)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (current < 0x20)
                        {
                            builder.Append("\\u")
                                .Append(((int)current).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(current);
                        }
                        break;
                }
            }
            builder.Append('"');
        }
    }
}

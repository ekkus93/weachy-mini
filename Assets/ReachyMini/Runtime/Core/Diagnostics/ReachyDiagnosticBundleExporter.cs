#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ReachyMini.Security;

namespace ReachyMini.Diagnostics
{
    public sealed class ReachyDiagnosticBundleExporter
    {
        public const int SchemaVersion = 1;
        public const long MaximumEntryBytes = 2L * 1024L * 1024L;
        public const long MaximumBundleBytes = 8L * 1024L * 1024L;
        public const string FileExtension = ".zip";

        private static readonly UTF8Encoding Utf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1822:Mark members as static",
            Justification = "Exporter instances are retained as an application composition seam for deterministic bundle export testing.")]
        public ReachyDiagnosticBundleExportResult Export(
            string outputPath,
            ReachyDiagnosticBundlePayload payload,
            ReachyDiagnosticBundleUserSelection userSelection)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "Diagnostic bundle export requires an output path.",
                    nameof(outputPath));
            }
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }
            RequireRedactedOnlySelection(userSelection);

            string fullPath = Path.GetFullPath(outputPath);
            if (!string.Equals(
                    Path.GetExtension(fullPath),
                    FileExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Diagnostic bundle paths must end with {FileExtension}.",
                    nameof(outputPath));
            }
            if (File.Exists(fullPath))
            {
                throw new IOException(
                    "Diagnostic bundle export will not overwrite an existing file.");
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    "Diagnostic bundle output requires a parent directory.");
            }
            Directory.CreateDirectory(directory);

            BundleEntryContent versionConfiguration = CreateTextEntry(
                "version-configuration.json",
                FormatSections(
                    payload.Diagnostics,
                    includePerformance: false));
            BundleEntryContent performanceHealth = CreateTextEntry(
                "performance-health.json",
                FormatSections(
                    payload.Diagnostics,
                    includePerformance: true));
            BundleEntryContent logs = CreateTextEntry(
                "logs.jsonl",
                FormatLogs(payload.RecentRecords));

            var exported = new[]
            {
                versionConfiguration,
                performanceHealth,
                logs,
            };
            string manifestJson = FormatManifest(
                exported,
                payload,
                userSelection);
            BundleEntryContent manifest = CreateTextEntry(
                "manifest.json",
                manifestJson);

            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.ReadWrite,
                           FileShare.None))
                using (var archive = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           leaveOpen: false))
                {
                    WriteEntry(archive, manifest);
                    for (int index = 0; index < exported.Length; ++index)
                    {
                        WriteEntry(archive, exported[index]);
                    }
                }

                long bundleBytes = new FileInfo(temporaryPath).Length;
                if (bundleBytes <= 0L || bundleBytes > MaximumBundleBytes)
                {
                    throw new InvalidDataException(
                        $"Diagnostic bundle size {bundleBytes} is outside the supported 1-{MaximumBundleBytes} byte range.");
                }

                File.Move(temporaryPath, fullPath);
                return new ReachyDiagnosticBundleExportResult(
                    fullPath,
                    bundleBytes,
                    payload.RecentRecords.Count,
                    payload.DroppedRecordCount);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static void RequireRedactedOnlySelection(
            ReachyDiagnosticBundleUserSelection userSelection)
        {
            if (!Enum.IsDefined(
                    typeof(ReachyDiagnosticBundleUserSelection),
                    userSelection))
            {
                throw new ArgumentOutOfRangeException(nameof(userSelection));
            }
            if (userSelection != ReachyDiagnosticBundleUserSelection.RedactedOnly)
            {
                throw new InvalidOperationException(
                    "Sensitive diagnostic export is not implemented. " +
                    "Credentials, raw media, transcripts, and conversation text remain excluded; " +
                    "a future sensitive-content path must provide explicit user consent and a separate privacy review.");
            }
        }

        private static BundleEntryContent CreateTextEntry(
            string name,
            string content)
        {
            byte[] bytes = Utf8.GetBytes(content);
            if (bytes.LongLength > MaximumEntryBytes)
            {
                throw new InvalidDataException(
                    $"Diagnostic bundle entry {name} exceeds the {MaximumEntryBytes}-byte limit.");
            }
            return new BundleEntryContent(
                name,
                bytes,
                Sha256Hex(bytes));
        }

        private static void WriteEntry(
            ZipArchive archive,
            BundleEntryContent content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                content.Name,
                CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            entryStream.Write(content.Bytes, 0, content.Bytes.Length);
        }

        private static string FormatLogs(
            IReadOnlyList<ReachyDiagnosticRecord> records)
        {
            var builder = new StringBuilder(Math.Max(256, records.Count * 256));
            for (int index = 0; index < records.Count; ++index)
            {
                ReachyDiagnosticRecord sanitized = SanitizeRecord(records[index]);
                builder.Append(ReachyDiagnosticJsonFormatter.Format(sanitized))
                    .Append('\n');
            }
            return builder.ToString();
        }

        private static ReachyDiagnosticRecord SanitizeRecord(
            ReachyDiagnosticRecord record)
        {
            var fields = new ReachyDiagnosticField[record.Fields.Count];
            for (int index = 0; index < record.Fields.Count; ++index)
            {
                fields[index] = ReachyDiagnosticRedactor.Redact(record.Fields[index]);
            }
            return new ReachyDiagnosticRecord(
                record.Descriptor,
                record.MonotonicMilliseconds,
                new ReachyDiagnosticContext(
                    SanitizeIdentifier("session_id", record.Context.SessionId),
                    SanitizeIdentifier("turn_id", record.Context.TurnId)),
                fields,
                record.OccurrenceCount,
                record.SuppressedCount,
                record.IsRateLimitSummary);
        }

        private static string FormatSections(
            ReachyDiagnosticsScreenSnapshot snapshot,
            bool includePerformance)
        {
            ReachyDiagnosticsSection[] sections = includePerformance
                ? new[]
                {
                    snapshot.Simulation,
                    snapshot.Rendering,
                    snapshot.Camera,
                }
                : new[]
                {
                    snapshot.Providers,
                    snapshot.Versions,
                    snapshot.Device,
                };

            var builder = new StringBuilder(4096).Append("{\"sections\":[");
            for (int sectionIndex = 0; sectionIndex < sections.Length; ++sectionIndex)
            {
                if (sectionIndex > 0)
                {
                    builder.Append(',');
                }
                ReachyDiagnosticsSection section = sections[sectionIndex];
                builder.Append('{');
                JsonProperty(
                    builder,
                    "title",
                    SanitizeText("section_title", section.Title),
                    first: true);
                JsonProperty(
                    builder,
                    "availability",
                    section.Availability.ToString(),
                    first: false);
                builder.Append(",\"metrics\":[");
                for (int metricIndex = 0; metricIndex < section.Metrics.Count; ++metricIndex)
                {
                    if (metricIndex > 0)
                    {
                        builder.Append(',');
                    }
                    ReachyDiagnosticsMetric metric = section.Metrics[metricIndex];
                    builder.Append('{');
                    JsonProperty(
                        builder,
                        "label",
                        SanitizeText("metric_label", metric.Label),
                        first: true);
                    JsonProperty(
                        builder,
                        "value",
                        SanitizeText("metric_value", metric.Value),
                        first: false);
                    JsonProperty(
                        builder,
                        "availability",
                        metric.Availability.ToString(),
                        first: false);
                    JsonProperty(
                        builder,
                        "reason",
                        SanitizeText("metric_reason", metric.Reason),
                        first: false);
                    builder.Append('}');
                }
                builder.Append("]}");
            }
            builder.Append("]}\n");
            return builder.ToString();
        }

        private static string FormatManifest(
            IReadOnlyList<BundleEntryContent> entries,
            ReachyDiagnosticBundlePayload payload,
            ReachyDiagnosticBundleUserSelection selection)
        {
            var builder = new StringBuilder(4096)
                .Append('{')
                .Append("\"schema_version\":")
                .Append(SchemaVersion.ToString(CultureInfo.InvariantCulture));
            JsonProperty(
                builder,
                "format",
                "reachy-mini-diagnostic-bundle",
                first: false);
            JsonProperty(
                builder,
                "user_selection",
                selection.ToString(),
                first: false);
            builder.Append(",\"redaction_policy\":{");
            JsonProperty(
                builder,
                "structured_log_redaction",
                "RMA-170 fields are re-redacted during export",
                first: true);
            JsonProperty(
                builder,
                "default_exclusions",
                "credentials, raw audio, raw images, raw media, transcripts, conversation text",
                first: false);
            JsonProperty(
                builder,
                "sensitive_export",
                "unsupported; non-redacted user selections fail closed",
                first: false);
            builder.Append('}');
            builder.Append(",\"denied_data_classes\":[");
            IReadOnlyList<ReachyDiagnosticDataClass> deniedClasses =
                ReachyDiagnosticBundleManifest.DefaultDeniedDataClasses;
            for (int index = 0; index < deniedClasses.Count; ++index)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                JsonString(builder, deniedClasses[index].ToString());
            }
            builder.Append(']');
            builder.Append(",\"retained_log_records\":")
                .Append(payload.RecentRecords.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"dropped_log_records\":")
                .Append(payload.DroppedRecordCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"entries\":[");
            for (int index = 0; index < entries.Count; ++index)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                BundleEntryContent entry = entries[index];
                builder.Append('{');
                JsonProperty(builder, "name", entry.Name, first: true);
                builder.Append(",\"bytes\":")
                    .Append(entry.Bytes.LongLength.ToString(CultureInfo.InvariantCulture));
                JsonProperty(builder, "sha256", entry.Sha256, first: false);
                JsonProperty(builder, "classification", "redacted_text", first: false);
                builder.Append('}');
            }
            builder.Append("]}\n");
            return builder.ToString();
        }

        private static string SanitizeIdentifier(string key, string value)
        {
            return ReachyDiagnosticRedactor.Redact(
                new ReachyDiagnosticField(
                    key,
                    value,
                    ReachyDiagnosticDataClass.Identifier)).Value;
        }

        private static string SanitizeText(string key, string value)
        {
            bool isNetworkUrl =
                Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) &&
                parsed != null &&
                (string.Equals(
                     parsed.Scheme,
                     Uri.UriSchemeHttps,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     parsed.Scheme,
                     Uri.UriSchemeHttp,
                     StringComparison.OrdinalIgnoreCase));
            ReachyDiagnosticDataClass dataClass = isNetworkUrl
                ? ReachyDiagnosticDataClass.Url
                : ReachyDiagnosticDataClass.Public;
            return ReachyDiagnosticRedactor.Redact(
                new ReachyDiagnosticField(key, value, dataClass)).Value;
        }

        private static void JsonProperty(
            StringBuilder builder,
            string name,
            string value,
            bool first)
        {
            if (!first)
            {
                builder.Append(',');
            }
            JsonString(builder, name);
            builder.Append(':');
            JsonString(builder, value);
        }

        private static void JsonString(StringBuilder builder, string value)
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

        private static string Sha256Hex(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; ++index)
            {
                builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private readonly struct BundleEntryContent
        {
            public BundleEntryContent(
                string name,
                byte[] bytes,
                string sha256)
            {
                ReachyDiagnosticBundleSecurityPolicy.RequireExportable(
                    ReachyDiagnosticArtifactKind.RedactedText);
                if (string.IsNullOrWhiteSpace(name) ||
                    name.Contains('/') ||
                    name.Contains('\\') ||
                    name.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Diagnostic bundle entry names must be simple contained filenames.");
                }
                Name = name;
                Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
                Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
            }

            public string Name { get; }
            public byte[] Bytes { get; }
            public string Sha256 { get; }
        }
    }
}

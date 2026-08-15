#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ReachyMini.Diagnostics;

namespace ReachyMini.Core.Tests
{
    internal static class Rma172DiagnosticBundleExportContractTests
    {
        public static void RunAll()
        {
            RecordBufferIsBoundedAndOrdered();
            RedactedOnlyBundleContainsRequiredEntries();
            SensitiveSelectionsFailClosed();
            ExistingBundleIsNeverOverwritten();
        }

        private static void RecordBufferIsBoundedAndOrdered()
        {
            var buffer = new ReachyDiagnosticRecordBuffer(capacity: 2);
            buffer.Write(Record(1L, "first", ReachyDiagnosticDataClass.Public));
            buffer.Write(Record(2L, "second", ReachyDiagnosticDataClass.Public));
            buffer.Write(Record(3L, "third", ReachyDiagnosticDataClass.Public));

            IReadOnlyList<ReachyDiagnosticRecord> retained = buffer.Snapshot();
            Equal(2, retained.Count, "retained count");
            Equal(1UL, buffer.DroppedCount, "dropped count");
            Equal(2L, retained[0].MonotonicMilliseconds, "oldest retained record");
            Equal(3L, retained[1].MonotonicMilliseconds, "newest retained record");
        }

        private static void RedactedOnlyBundleContainsRequiredEntries()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "reachy-rma172-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "bundle.zip");
                var payload = new ReachyDiagnosticBundlePayload(
                    Snapshot(),
                    new[]
                    {
                        Record(
                            4L,
                            "top-secret-value",
                            ReachyDiagnosticDataClass.Secret),
                    },
                    droppedRecordCount: 7UL);
                var exporter = new ReachyDiagnosticBundleExporter();
                ReachyDiagnosticBundleExportResult result = exporter.Export(
                    path,
                    payload,
                    ReachyDiagnosticBundleUserSelection.RedactedOnly);

                True(File.Exists(path), "bundle exists");
                Equal(1, result.ExportedLogRecords, "exported log count");
                Equal(7UL, result.DroppedLogRecords, "dropped log count");
                True(result.ByteCount > 0L, "bundle byte count");

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                using var archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);
                string manifest = ReadEntry(archive, "manifest.json");
                string versions = ReadEntry(
                    archive,
                    "version-configuration.json");
                string performance = ReadEntry(
                    archive,
                    "performance-health.json");
                string logs = ReadEntry(archive, "logs.jsonl");

                Contains(manifest, "\"user_selection\":\"RedactedOnly\"");
                Contains(manifest, "credentials, raw audio, raw images, raw media, transcripts, conversation text");
                Contains(manifest, "\"dropped_log_records\":7");
                Contains(versions, "\"title\":\"Versions\"");
                Contains(versions, "\"title\":\"Providers\"");
                NotContains(versions, "snapshot-secret-value");
                Contains(performance, "\"title\":\"Simulation\"");
                Contains(performance, "\"title\":\"Camera\"");
                Contains(logs, ReachyDiagnosticRedactor.RedactedValue);
                NotContains(logs, "top-secret-value");
                NotContains(logs, "context-secret-value");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static void SensitiveSelectionsFailClosed()
        {
            foreach (ReachyDiagnosticBundleUserSelection selection in new[]
                     {
                         ReachyDiagnosticBundleUserSelection.IncludePrivateText,
                         ReachyDiagnosticBundleUserSelection.IncludeRawMedia,
                         ReachyDiagnosticBundleUserSelection.IncludeCredentials,
                     })
            {
                AssertThrows<InvalidOperationException>(() =>
                    ReachyDiagnosticBundleExporter.RequireRedactedOnlySelection(
                        selection));
            }
        }

        private static void ExistingBundleIsNeverOverwritten()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "reachy-rma172-overwrite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "bundle.zip");
                File.WriteAllText(path, "sentinel");
                var exporter = new ReachyDiagnosticBundleExporter();
                AssertThrows<IOException>(() => exporter.Export(
                    path,
                    new ReachyDiagnosticBundlePayload(
                        Snapshot(),
                        Array.Empty<ReachyDiagnosticRecord>()),
                    ReachyDiagnosticBundleUserSelection.RedactedOnly));
                Equal("sentinel", File.ReadAllText(path), "existing file content");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static ReachyDiagnosticRecord Record(
            long monotonicMilliseconds,
            string value,
            ReachyDiagnosticDataClass dataClass)
        {
            return new ReachyDiagnosticRecord(
                new ReachyDiagnosticEventDescriptor(
                    "diagnostics",
                    "diagnostics.test",
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Unknown),
                monotonicMilliseconds,
                new ReachyDiagnosticContext(
                    "token=context-secret-value",
                    "turn"),
                new[]
                {
                    new ReachyDiagnosticField("value", value, dataClass),
                },
                occurrenceCount: 1UL,
                suppressedCount: 0UL,
                isRateLimitSummary: false);
        }

        private static ReachyDiagnosticsScreenSnapshot Snapshot()
        {
            ReachyDiagnosticsSection Section(
                string title,
                string label,
                string? value = null) =>
                new ReachyDiagnosticsSection(
                    title,
                    new[]
                    {
                        new ReachyDiagnosticsMetric(
                            label,
                            value ?? title + " value"),
                    });

            return new ReachyDiagnosticsScreenSnapshot(
                Section("Simulation", "Physics"),
                Section("Rendering", "FPS"),
                Section("Camera", "Coverage"),
                Section("Providers", "LLM"),
                Section("Versions", "App", "password=snapshot-secret-value"),
                Section("Device", "Model"));
        }

        private static string ReadEntry(ZipArchive archive, string name)
        {
            ZipArchiveEntry entry = archive.GetEntry(name) ??
                throw new InvalidOperationException(
                    "Expected diagnostic bundle entry: " + name);
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                "Expected " + typeof(TException).Name + ".");
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(label + " expected true.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    label + ": expected=" + expected + "; actual=" + actual);
            }
        }

        private static void Contains(string value, string expected)
        {
            if (value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Expected diagnostic bundle text to contain: " + expected);
            }
        }

        private static void NotContains(string value, string unexpected)
        {
            if (value.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    "Diagnostic bundle unexpectedly contained: " + unexpected);
            }
        }
    }
}

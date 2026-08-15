#nullable enable

using System;
using System.Globalization;
using System.IO;
using ReachyMini.Diagnostics;
using ReachyMini.RuntimeDiagnostics;

namespace ReachyMini.AppState
{
    public sealed class ReachyDiagnosticBundleExportOutcome
    {
        public ReachyDiagnosticBundleExportOutcome(
            bool succeeded,
            string detail,
            string fullPath = "")
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "Diagnostic bundle export outcomes require detail.",
                    nameof(detail));
            }
            if (succeeded && string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentException(
                    "Successful diagnostic bundle exports require a path.",
                    nameof(fullPath));
            }

            Succeeded = succeeded;
            Detail = detail.Trim();
            FullPath = fullPath ?? string.Empty;
        }

        public bool Succeeded { get; }

        public string Detail { get; }

        public string FullPath { get; }
    }

    internal sealed class ReachyDiagnosticBundleExportCoordinator
    {
        private readonly ReachyDiagnosticsScreenSource diagnosticsSource;
        private readonly ReachyDiagnosticBundleExporter exporter;
        private readonly string outputDirectory;

        public ReachyDiagnosticBundleExportCoordinator(
            ReachyDiagnosticsScreenSource source,
            string diagnosticOutputDirectory,
            ReachyDiagnosticBundleExporter? bundleExporter = null)
        {
            diagnosticsSource = source ??
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(diagnosticOutputDirectory))
            {
                throw new ArgumentException(
                    "Diagnostic bundle exports require an output directory.",
                    nameof(diagnosticOutputDirectory));
            }

            outputDirectory = Path.GetFullPath(diagnosticOutputDirectory);
            exporter = bundleExporter ?? new ReachyDiagnosticBundleExporter();
        }

        public ReachyDiagnosticBundleExportOutcome ExportRedactedBundle()
        {
            try
            {
                ReachyRuntimeDiagnostics.Emit(
                    "diagnostics",
                    ReachyDiagnosticEventIds.DiagnosticBundleExportStarted,
                    ReachyDiagnosticSeverity.Information,
                    ReachyDiagnosticErrorCategory.Storage,
                    new ReachyDiagnosticField(
                        "selection",
                        ReachyDiagnosticBundleUserSelection.RedactedOnly.ToString(),
                        ReachyDiagnosticDataClass.Identifier));
                ReachyRuntimeDiagnostics.Flush();

                ReachyDiagnosticsScreenSnapshot snapshot = diagnosticsSource.Capture();
                var payload = new ReachyDiagnosticBundlePayload(
                    snapshot,
                    ReachyRuntimeDiagnostics.CaptureRecentRecords(),
                    ReachyRuntimeDiagnostics.DroppedCapturedRecordCount);
                Directory.CreateDirectory(outputDirectory);
                string outputPath = Path.Combine(
                    outputDirectory,
                    BuildFileName(DateTime.UtcNow));
                ReachyDiagnosticBundleExportResult result = exporter.Export(
                    outputPath,
                    payload,
                    ReachyDiagnosticBundleUserSelection.RedactedOnly);

                ReachyRuntimeDiagnostics.Emit(
                    "diagnostics",
                    ReachyDiagnosticEventIds.DiagnosticBundleExportSucceeded,
                    ReachyDiagnosticSeverity.Information,
                    ReachyDiagnosticErrorCategory.Storage,
                    new ReachyDiagnosticField(
                        "exported_log_records",
                        result.ExportedLogRecords.ToString(CultureInfo.InvariantCulture),
                        ReachyDiagnosticDataClass.Identifier),
                    new ReachyDiagnosticField(
                        "dropped_log_records",
                        result.DroppedLogRecords.ToString(CultureInfo.InvariantCulture),
                        ReachyDiagnosticDataClass.Identifier));

                return new ReachyDiagnosticBundleExportOutcome(
                    true,
                    $"Redacted diagnostic bundle exported ({result.ByteCount} bytes). Sensitive content was excluded.",
                    result.FullPath);
            }
            catch (Exception exception)
            {
                ReachyRuntimeDiagnostics.Emit(
                    "diagnostics",
                    ReachyDiagnosticEventIds.DiagnosticBundleExportFailed,
                    ReachyDiagnosticSeverity.Error,
                    ReachyDiagnosticErrorCategory.Storage,
                    new ReachyDiagnosticField(
                        "operation",
                        "export_redacted_bundle",
                        ReachyDiagnosticDataClass.Identifier),
                    new ReachyDiagnosticField(
                        "exception_type",
                        exception.GetType().Name,
                        ReachyDiagnosticDataClass.Identifier));
                return new ReachyDiagnosticBundleExportOutcome(
                    false,
                    "Diagnostic bundle export failed (" +
                    exception.GetType().Name + "). Sensitive content was not exported.");
            }
        }

        private static string BuildFileName(DateTime utcNow)
        {
            return "reachy-diagnostics-" +
                utcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8) +
                ReachyDiagnosticBundleExporter.FileExtension;
        }
    }
}

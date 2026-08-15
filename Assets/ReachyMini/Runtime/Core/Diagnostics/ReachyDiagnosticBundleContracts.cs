#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Diagnostics
{
    public enum ReachyDiagnosticBundleUserSelection
    {
        RedactedOnly = 0,
        IncludePrivateText = 1,
        IncludeRawMedia = 2,
        IncludeCredentials = 3,
    }

    public sealed class ReachyDiagnosticBundlePayload
    {
        private readonly ReachyDiagnosticRecord[] records;

        public ReachyDiagnosticBundlePayload(
            ReachyDiagnosticsScreenSnapshot diagnostics,
            IReadOnlyList<ReachyDiagnosticRecord> recentRecords,
            ulong droppedRecordCount = 0UL)
        {
            Diagnostics = diagnostics ??
                throw new ArgumentNullException(nameof(diagnostics));
            if (recentRecords == null)
            {
                throw new ArgumentNullException(nameof(recentRecords));
            }
            if (recentRecords.Count > ReachyDiagnosticRecordBuffer.MaximumCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(recentRecords),
                    recentRecords.Count,
                    $"Diagnostic bundles accept at most {ReachyDiagnosticRecordBuffer.MaximumCapacity} retained records.");
            }

            records = new ReachyDiagnosticRecord[recentRecords.Count];
            for (int index = 0; index < recentRecords.Count; ++index)
            {
                records[index] = recentRecords[index] ??
                    throw new ArgumentException(
                        "Diagnostic bundles cannot contain null log records.",
                        nameof(recentRecords));
            }
            DroppedRecordCount = droppedRecordCount;
        }

        public ReachyDiagnosticsScreenSnapshot Diagnostics { get; }

        public IReadOnlyList<ReachyDiagnosticRecord> RecentRecords =>
            Array.AsReadOnly(records);

        public ulong DroppedRecordCount { get; }
    }

    public sealed class ReachyDiagnosticBundleExportResult
    {
        public ReachyDiagnosticBundleExportResult(
            string fullPath,
            long byteCount,
            int exportedLogRecords,
            ulong droppedLogRecords)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentException(
                    "Diagnostic bundle results require a path.",
                    nameof(fullPath));
            }
            if (byteCount < 0L || exportedLogRecords < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            FullPath = fullPath;
            ByteCount = byteCount;
            ExportedLogRecords = exportedLogRecords;
            DroppedLogRecords = droppedLogRecords;
        }

        public string FullPath { get; }

        public long ByteCount { get; }

        public int ExportedLogRecords { get; }

        public ulong DroppedLogRecords { get; }
    }
}

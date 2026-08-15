#nullable enable

using System;
using System.IO;

namespace ReachyMini.Diagnostics
{
    public static class ReachyDiagnosticBundleStoragePolicy
    {
        public const long DefaultSafetyReserveBytes = 16L * 1024L * 1024L;
    }

    public interface IReachyDiagnosticBundleStorageProbe
    {
        long GetAvailableBytes(string outputDirectory);
    }

    public sealed class ReachyDiagnosticBundleDriveStorageProbe :
        IReachyDiagnosticBundleStorageProbe
    {
        public long GetAvailableBytes(string outputDirectory)
        {
            if (outputDirectory == null)
            {
                throw new ArgumentNullException(nameof(outputDirectory));
            }

            string fullPath = Path.GetFullPath(outputDirectory);
            string filesystemRoot = Path.GetPathRoot(fullPath) ??
                throw new InvalidOperationException(
                    "The diagnostic output directory does not have a filesystem root.");
            return new DriveInfo(filesystemRoot).AvailableFreeSpace;
        }
    }

    public sealed class ReachyDiagnosticBundleInsufficientStorageException : IOException
    {
        public ReachyDiagnosticBundleInsufficientStorageException()
            : base("Free storage is insufficient for a diagnostic bundle export.")
        {
        }

        public ReachyDiagnosticBundleInsufficientStorageException(string message)
            : base(message)
        {
        }

        public ReachyDiagnosticBundleInsufficientStorageException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public sealed class ReachyStorageAwareDiagnosticBundleExporter
    {
        private readonly ReachyDiagnosticBundleExporter exporter;
        private readonly IReachyDiagnosticBundleStorageProbe storageProbe;
        private readonly long safetyReserveBytes;

        public ReachyStorageAwareDiagnosticBundleExporter(
            IReachyDiagnosticBundleStorageProbe? probe = null,
            long safetyReserveBytes = ReachyDiagnosticBundleStoragePolicy.DefaultSafetyReserveBytes,
            ReachyDiagnosticBundleExporter? innerExporter = null)
        {
            if (safetyReserveBytes < 0L ||
                safetyReserveBytes > long.MaxValue - ReachyDiagnosticBundleExporter.MaximumBundleBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(safetyReserveBytes));
            }

            storageProbe = probe ?? new ReachyDiagnosticBundleDriveStorageProbe();
            this.safetyReserveBytes = safetyReserveBytes;
            exporter = innerExporter ?? new ReachyDiagnosticBundleExporter();
        }

        public ReachyDiagnosticBundleExportResult Export(
            string outputPath,
            ReachyDiagnosticBundlePayload payload,
            ReachyDiagnosticBundleUserSelection selection)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "Diagnostic bundle export requires an output path.",
                    nameof(outputPath));
            }

            string fullPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullPath) ??
                throw new InvalidOperationException(
                    "The diagnostic bundle output path has no containing directory.");
            RequireStorageCapacity(directory);

            try
            {
                return exporter.Export(fullPath, payload, selection);
            }
            catch (IOException exception) when (StorageIsBelowExportReserve(directory))
            {
                throw new ReachyDiagnosticBundleInsufficientStorageException(
                    "Free storage became insufficient while writing the diagnostic bundle.",
                    exception);
            }
        }

        private void RequireStorageCapacity(string directory)
        {
            long available = ProbeAvailableBytes(directory);
            long required = checked(
                ReachyDiagnosticBundleExporter.MaximumBundleBytes + safetyReserveBytes);
            if (available < required)
            {
                throw new ReachyDiagnosticBundleInsufficientStorageException();
            }
        }

        private bool StorageIsBelowExportReserve(string directory)
        {
            try
            {
                return ProbeAvailableBytes(directory) < safetyReserveBytes;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private long ProbeAvailableBytes(string directory)
        {
            try
            {
                long available = storageProbe.GetAvailableBytes(directory);
                if (available < 0L)
                {
                    throw new IOException(
                        "The diagnostic storage probe returned a negative free-space value.");
                }
                return available;
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException(
                    "The diagnostic storage probe could not inspect free space.",
                    exception);
            }
            catch (InvalidOperationException exception)
            {
                throw new IOException(
                    "The diagnostic storage probe could not inspect free space.",
                    exception);
            }
        }
    }
}

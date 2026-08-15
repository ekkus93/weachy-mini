#nullable enable

using System;
using System.Globalization;
using System.IO;
using ReachyMini.Diagnostics;
using ReachyMini.RuntimeDiagnostics;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyStorageCleanupOutcome
    {
        public ReachyStorageCleanupOutcome(
            bool succeeded,
            string detail,
            int removedDiagnosticFiles,
            long removedDiagnosticBytes)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "Storage cleanup outcomes require detail.",
                    nameof(detail));
            }
            if (removedDiagnosticFiles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(removedDiagnosticFiles));
            }
            if (removedDiagnosticBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(removedDiagnosticBytes));
            }

            Succeeded = succeeded;
            Detail = detail.Trim();
            RemovedDiagnosticFiles = removedDiagnosticFiles;
            RemovedDiagnosticBytes = removedDiagnosticBytes;
        }

        public bool Succeeded { get; }

        public string Detail { get; }

        public int RemovedDiagnosticFiles { get; }

        public long RemovedDiagnosticBytes { get; }
    }

    internal sealed class ReachyStorageCleanupCoordinator
    {
        private const string DiagnosticFilePrefix = "reachy-diagnostics-";
        private readonly string diagnosticDirectory;

        public ReachyStorageCleanupCoordinator(string diagnosticOutputDirectory)
        {
            if (string.IsNullOrWhiteSpace(diagnosticOutputDirectory))
            {
                throw new ArgumentException(
                    "Storage cleanup requires a diagnostic output directory.",
                    nameof(diagnosticOutputDirectory));
            }
            diagnosticDirectory = Path.GetFullPath(diagnosticOutputDirectory);
        }

        public ReachyStorageCleanupOutcome CleanupRecoverableStorage()
        {
            int removedFiles = 0;
            long removedBytes = 0L;
            int failures = 0;

            try
            {
                if (Directory.Exists(diagnosticDirectory) &&
                    IsReparsePoint(diagnosticDirectory))
                {
                    failures = checked(failures + 1);
                }
                else if (Directory.Exists(diagnosticDirectory))
                {
                    string[] files = Directory.GetFiles(
                        diagnosticDirectory,
                        DiagnosticFilePrefix + "*",
                        SearchOption.TopDirectoryOnly);
                    for (int index = 0; index < files.Length; ++index)
                    {
                        string file = files[index];
                        if (!IsOwnedDiagnosticArtifact(file) || IsReparsePoint(file))
                        {
                            continue;
                        }

                        try
                        {
                            long length = new FileInfo(file).Length;
                            File.Delete(file);
                            removedFiles = checked(removedFiles + 1);
                            removedBytes = checked(removedBytes + length);
                        }
                        catch (IOException)
                        {
                            failures = checked(failures + 1);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            failures = checked(failures + 1);
                        }
                    }
                }
            }
            catch (IOException)
            {
                failures = checked(failures + 1);
            }
            catch (UnauthorizedAccessException)
            {
                failures = checked(failures + 1);
            }

            bool unityCacheCleared = false;
            try
            {
                unityCacheCleared = Caching.ClearCache();
            }
            catch (Exception)
            {
                failures = checked(failures + 1);
            }

            bool succeeded = failures == 0;
            ReachyRuntimeDiagnostics.Emit(
                "storage",
                succeeded
                    ? ReachyDiagnosticEventIds.StorageCleanupSucceeded
                    : ReachyDiagnosticEventIds.StorageCleanupFailed,
                succeeded
                    ? ReachyDiagnosticSeverity.Information
                    : ReachyDiagnosticSeverity.Warning,
                ReachyDiagnosticErrorCategory.Storage,
                new ReachyDiagnosticField(
                    "removed_diagnostic_files",
                    removedFiles.ToString(CultureInfo.InvariantCulture),
                    ReachyDiagnosticDataClass.Identifier),
                new ReachyDiagnosticField(
                    "unity_cache_cleared",
                    unityCacheCleared ? "true" : "false",
                    ReachyDiagnosticDataClass.Identifier));

            string detail = succeeded
                ? "Cleanup removed " + removedFiles + " diagnostic export(s) (" +
                  removedBytes + " bytes) and requested Unity cache cleanup. " +
                  "Installed models, settings, credentials, and user state were retained."
                : "Cleanup completed with " + failures +
                  " recoverable file error(s). Installed models, settings, credentials, and user state were retained.";
            return new ReachyStorageCleanupOutcome(
                succeeded,
                detail,
                removedFiles,
                removedBytes);
        }

        private static bool IsOwnedDiagnosticArtifact(string path)
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith(DiagnosticFilePrefix, StringComparison.Ordinal))
            {
                return false;
            }
            return name.EndsWith(
                       ReachyDiagnosticBundleExporter.FileExtension,
                       StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    ReachyDiagnosticBundleExporter.FileExtension + ".tmp-",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
    }
}

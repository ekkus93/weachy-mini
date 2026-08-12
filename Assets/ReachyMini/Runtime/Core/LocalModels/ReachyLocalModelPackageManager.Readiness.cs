#nullable enable

using System;
using System.IO;
using System.Text;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalModelPackageManager
    {
        private LocalModelPackageResult? EnsureReadyAndValidateArtifact(
            LocalModelManifest manifest)
        {
            LocalModelPackageResult? readiness = EnsureStoreReady();
            if (readiness != null)
            {
                return readiness;
            }
            if (manifest.Artifact.FileSizeBytes > options.MaximumArtifactBytes)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.ArtifactTooLarge,
                    "The model artifact exceeds the configured package-size ceiling.");
            }
            return null;
        }

        private LocalModelPackageResult? CheckStorage(long bytesToWrite)
        {
            if (bytesToWrite < 0L)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.SizeMismatch,
                    "A retained partial is larger than the manifest artifact.");
            }

            long available;
            try
            {
                available = storageProbe.GetAvailableBytes(rootPath);
            }
            catch (IOException exception)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.StorageProbeFailed,
                    SafeIoDetail("Storage availability could not be read", exception));
            }
            catch (UnauthorizedAccessException exception)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.StorageProbeFailed,
                    SafeIoDetail("Storage availability access was denied", exception));
            }
            catch (InvalidOperationException exception)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.StorageProbeFailed,
                    SafeIoDetail("Storage availability could not be read", exception));
            }

            if (available < 0L ||
                bytesToWrite > available ||
                options.SafetyReserveBytes > available - bytesToWrite)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.InsufficientStorage,
                    "Free storage is insufficient for the remaining model bytes plus the configured safety reserve.");
            }
            return null;
        }

        private LocalModelPackageResult? EnsureStoreReady()
        {
            try
            {
                if (Directory.Exists(rootPath) && IsReparsePoint(rootPath))
                {
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.StorePathUnsafe,
                        "The local-model store root cannot be a symlink or reparse point.");
                }
                if (!Directory.Exists(rootPath))
                {
                    Directory.CreateDirectory(rootPath);
                }

                string markerPath = RequireContainedPath(
                    Path.Combine(rootPath, MarkerFileName));
                string markerTemporaryPath = RequireContainedPath(
                    Path.Combine(rootPath, MarkerTemporaryFileName));

                if (!File.Exists(markerPath))
                {
                    string[] entries = Directory.GetFileSystemEntries(rootPath);
                    if (entries.Length == 0)
                    {
                        WriteMarker(markerTemporaryPath, markerPath);
                    }
                    else if (entries.Length == 1 &&
                        string.Equals(
                            Path.GetFullPath(entries[0]),
                            markerTemporaryPath,
                            GetPathComparison()) &&
                        File.Exists(markerTemporaryPath) &&
                        string.Equals(
                            File.ReadAllText(markerTemporaryPath, Encoding.UTF8),
                            MarkerContents,
                            StringComparison.Ordinal))
                    {
                        File.Move(markerTemporaryPath, markerPath);
                    }
                    else
                    {
                        return LocalModelPackageResult.Failed(
                            LocalModelPackageFailure.StoreOwnershipMismatch,
                            "The configured model store is nonempty and has no exact ownership marker.");
                    }
                }

                if (IsReparsePoint(markerPath) ||
                    !string.Equals(
                        File.ReadAllText(markerPath, Encoding.UTF8),
                        MarkerContents,
                        StringComparison.Ordinal))
                {
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.StoreOwnershipMismatch,
                        "The configured model-store ownership marker is invalid.");
                }

                return EnsureManagedDirectoryTree(installedRoot) ??
                    EnsureManagedDirectoryTree(stagingRoot) ??
                    EnsureManagedDirectoryTree(quarantineRoot);
            }
            catch (IOException exception)
            {
                return IoFailure("Initializing the local-model store failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Initializing the local-model store was denied", exception);
            }
        }

        private static void WriteMarker(
            string temporaryPath,
            string markerPath)
        {
            File.WriteAllText(
                temporaryPath,
                MarkerContents,
                new UTF8Encoding(false));
            File.Move(temporaryPath, markerPath);
        }

        private LocalModelPackageResult? EnsureManagedDirectoryTree(string directory)
        {
            string fullDirectory = RequireContainedPath(directory);
            string relative = fullDirectory.Substring(rootPathWithSeparator.Length);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            string current = rootPath;
            for (int index = 0; index < segments.Length; ++index)
            {
                current = Path.Combine(current, segments[index]);
                if (Directory.Exists(current))
                {
                    if (IsReparsePoint(current))
                    {
                        return LocalModelPackageResult.Failed(
                            LocalModelPackageFailure.StorePathUnsafe,
                            "Managed local-model directories cannot be symlinks or reparse points.");
                    }
                    continue;
                }

                Directory.CreateDirectory(current);
                if (IsReparsePoint(current))
                {
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.StorePathUnsafe,
                        "A managed local-model directory resolved to a symlink or reparse point.");
                }
            }
            return null;
        }

        private LocalModelPackageResult? RejectReparsePoint(string path)
        {
            string fullPath = RequireContainedPath(path);
            if ((File.Exists(fullPath) || Directory.Exists(fullPath)) &&
                IsReparsePoint(fullPath))
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.StorePathUnsafe,
                    "Managed local-model artifacts cannot be symlinks or reparse points.");
            }
            return null;
        }
    }
}

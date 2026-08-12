#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalModelPackageManager
    {
        public async Task<LocalModelRecoveryReport> RecoverAsync(
            IReadOnlyList<LocalModelManifest> knownManifests,
            CancellationToken cancellationToken)
        {
            if (knownManifests == null)
            {
                throw new ArgumentNullException(nameof(knownManifests));
            }

            ThrowIfDisposed();
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LocalModelPackageResult? readiness = EnsureStoreReady();
                if (readiness != null)
                {
                    return RecoveryFailure(readiness);
                }

                LocalModelManifestCatalog catalog = new LocalModelManifestCatalog(knownManifests);
                int removedImports = 0;
                int removedDownloads = 0;
                int retainedDownloads = 0;
                int corruptInstalled = 0;

                for (int index = 0; index < catalog.Manifests.Count; ++index)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LocalModelManifest manifest = catalog.Manifests[index];
                    string stagingDirectory = GetStagingDirectory(manifest);
                    if (Directory.Exists(stagingDirectory))
                    {
                        string importPath = GetImportPartPath(manifest);
                        if (File.Exists(importPath))
                        {
                            DeleteFileIfPresent(importPath);
                            ++removedImports;
                        }

                        string partPath = GetDownloadPartPath(manifest);
                        string metadataPath = GetDownloadMetadataPath(manifest);
                        bool anyDownloadState =
                            File.Exists(partPath) || File.Exists(metadataPath);
                        if (anyDownloadState)
                        {
                            bool resumable =
                                File.Exists(partPath) &&
                                File.Exists(metadataPath) &&
                                !IsReparsePoint(partPath) &&
                                !IsReparsePoint(metadataPath) &&
                                new FileInfo(partPath).Length <=
                                    manifest.Artifact.FileSizeBytes &&
                                TryReadDownloadMetadata(
                                    metadataPath,
                                    manifest,
                                    out _);
                            if (resumable)
                            {
                                ++retainedDownloads;
                            }
                            else
                            {
                                DeleteFileIfPresent(partPath);
                                DeleteFileIfPresent(metadataPath);
                                ++removedDownloads;
                            }
                        }
                    }

                    LocalModelPackageResult installed =
                        await ResolveInstalledCoreAsync(manifest, cancellationToken)
                            .ConfigureAwait(false);
                    if (!installed.Succeeded &&
                        installed.Failure == LocalModelPackageFailure.InstalledArtifactCorrupt)
                    {
                        ++corruptInstalled;
                    }
                    else if (!installed.Succeeded &&
                        installed.Failure != LocalModelPackageFailure.NotInstalled)
                    {
                        return new LocalModelRecoveryReport(
                            false,
                            installed.Failure,
                            installed.Detail,
                            removedImports,
                            removedDownloads,
                            retainedDownloads,
                            corruptInstalled);
                    }
                }

                return new LocalModelRecoveryReport(
                    true,
                    LocalModelPackageFailure.None,
                    "Recovery retained only manifest-bound resumable downloads and removed non-resumable import state.",
                    removedImports,
                    removedDownloads,
                    retainedDownloads,
                    corruptInstalled);
            }
            catch (IOException exception)
            {
                return new LocalModelRecoveryReport(
                    false,
                    LocalModelPackageFailure.IoFailure,
                    SafeIoDetail("Local-model recovery failed", exception),
                    0,
                    0,
                    0,
                    0);
            }
            catch (UnauthorizedAccessException exception)
            {
                return new LocalModelRecoveryReport(
                    false,
                    LocalModelPackageFailure.IoFailure,
                    SafeIoDetail("Local-model recovery was denied", exception),
                    0,
                    0,
                    0,
                    0);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<LocalModelCleanupReport> CleanupOrphansAsync(
            IReadOnlyList<LocalModelManifest> knownManifests,
            CancellationToken cancellationToken)
        {
            if (knownManifests == null)
            {
                throw new ArgumentNullException(nameof(knownManifests));
            }

            ThrowIfDisposed();
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LocalModelPackageResult? readiness = EnsureStoreReady();
                if (readiness != null)
                {
                    return CleanupFailure(readiness);
                }

                LocalModelManifestCatalog catalog = new LocalModelManifestCatalog(knownManifests);
                var expectedInstalled =
                    new HashSet<string>(GetPathComparer());
                var expectedStagingRoots =
                    new HashSet<string>(GetPathComparer());
                int corruptKnown = 0;

                for (int index = 0; index < catalog.Manifests.Count; ++index)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LocalModelManifest manifest = catalog.Manifests[index];
                    expectedInstalled.Add(GetInstalledArtifactPath(manifest));
                    expectedStagingRoots.Add(GetStagingDirectory(manifest));

                    LocalModelPackageResult installed =
                        await ResolveInstalledCoreAsync(manifest, cancellationToken)
                            .ConfigureAwait(false);
                    if (!installed.Succeeded &&
                        installed.Failure == LocalModelPackageFailure.InstalledArtifactCorrupt)
                    {
                        ++corruptKnown;
                    }
                    else if (!installed.Succeeded &&
                        installed.Failure != LocalModelPackageFailure.NotInstalled)
                    {
                        return new LocalModelCleanupReport(
                            false,
                            installed.Failure,
                            installed.Detail,
                            0,
                            0,
                            0,
                            corruptKnown);
                    }
                }

                int stagingRemoved = RemoveStagingOrphans(
                    stagingRoot,
                    expectedStagingRoots,
                    cancellationToken);
                int quarantineRemoved = RemoveAllChildrenNoFollow(
                    quarantineRoot,
                    cancellationToken);
                int installedRemoved = RemoveInstalledOrphans(
                    expectedInstalled,
                    cancellationToken);

                return new LocalModelCleanupReport(
                    true,
                    LocalModelPackageFailure.None,
                    "Orphan cleanup completed. Known corrupt artifacts were reported and retained.",
                    stagingRemoved,
                    quarantineRemoved,
                    installedRemoved,
                    corruptKnown);
            }
            catch (IOException exception)
            {
                return new LocalModelCleanupReport(
                    false,
                    LocalModelPackageFailure.IoFailure,
                    SafeIoDetail("Local-model orphan cleanup failed", exception),
                    0,
                    0,
                    0,
                    0);
            }
            catch (UnauthorizedAccessException exception)
            {
                return new LocalModelCleanupReport(
                    false,
                    LocalModelPackageFailure.IoFailure,
                    SafeIoDetail("Local-model orphan cleanup was denied", exception),
                    0,
                    0,
                    0,
                    0);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private int RemoveStagingOrphans(
            string managedRoot,
            HashSet<string> expectedDirectories,
            CancellationToken cancellationToken)
        {
            int removed = 0;
            string[] rootEntries = Directory.GetFileSystemEntries(managedRoot);
            for (int index = 0; index < rootEntries.Length; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string manifestEntry = RequireContainedPath(rootEntries[index]);
                if (IsReparsePoint(manifestEntry))
                {
                    DeleteEntryNoFollow(manifestEntry);
                    ++removed;
                    continue;
                }
                if (File.Exists(manifestEntry))
                {
                    File.Delete(manifestEntry);
                    ++removed;
                    continue;
                }
                if (!Directory.Exists(manifestEntry))
                {
                    continue;
                }

                string[] manifestEntries = Directory.GetFileSystemEntries(manifestEntry);
                for (int entryIndex = 0; entryIndex < manifestEntries.Length; ++entryIndex)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string hashEntry = RequireContainedPath(manifestEntries[entryIndex]);
                    if (IsReparsePoint(hashEntry))
                    {
                        DeleteEntryNoFollow(hashEntry);
                        ++removed;
                    }
                    else if (File.Exists(hashEntry))
                    {
                        File.Delete(hashEntry);
                        ++removed;
                    }
                    else if (Directory.Exists(hashEntry))
                    {
                        if (!expectedDirectories.Contains(hashEntry))
                        {
                            DeleteTreeNoFollow(hashEntry);
                            ++removed;
                        }
                        else
                        {
                            removed += RemoveUnexpectedStagingChildren(
                                hashEntry,
                                cancellationToken);
                        }
                    }
                }
                DeleteEmptyParents(manifestEntry, managedRoot);
            }
            return removed;
        }

        private int RemoveUnexpectedStagingChildren(
            string stagingDirectory,
            CancellationToken cancellationToken)
        {
            int removed = 0;
            string[] entries = Directory.GetFileSystemEntries(stagingDirectory);
            for (int index = 0; index < entries.Length; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entry = RequireContainedPath(entries[index]);
                if (IsReparsePoint(entry) || Directory.Exists(entry))
                {
                    if (Directory.Exists(entry) && !IsReparsePoint(entry))
                    {
                        DeleteTreeNoFollow(entry);
                    }
                    else
                    {
                        DeleteEntryNoFollow(entry);
                    }
                    ++removed;
                    continue;
                }

                string fileName = Path.GetFileName(entry);
                bool expectedFile =
                    string.Equals(fileName, DownloadPartFileName, StringComparison.Ordinal) ||
                    string.Equals(fileName, DownloadMetadataFileName, StringComparison.Ordinal) ||
                    string.Equals(fileName, ImportPartFileName, StringComparison.Ordinal);
                if (!expectedFile)
                {
                    File.Delete(entry);
                    ++removed;
                }
            }
            return removed;
        }

        private int RemoveInstalledOrphans(
            HashSet<string> expectedFiles,
            CancellationToken cancellationToken)
        {
            int removed = 0;
            var pending = new Stack<string>();
            pending.Push(installedRoot);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                string[] entries = Directory.GetFileSystemEntries(directory);
                for (int index = 0; index < entries.Length; ++index)
                {
                    string entry = RequireContainedPath(entries[index]);
                    if (IsReparsePoint(entry))
                    {
                        DeleteEntryNoFollow(entry);
                        ++removed;
                    }
                    else if (Directory.Exists(entry))
                    {
                        pending.Push(entry);
                    }
                    else if (File.Exists(entry) && !expectedFiles.Contains(entry))
                    {
                        File.Delete(entry);
                        ++removed;
                    }
                }
            }

            RemoveEmptyDirectories(installedRoot, cancellationToken);
            return removed;
        }

        private int RemoveAllChildrenNoFollow(
            string directory,
            CancellationToken cancellationToken)
        {
            int removed = 0;
            string[] entries = Directory.GetFileSystemEntries(directory);
            for (int index = 0; index < entries.Length; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entry = RequireContainedPath(entries[index]);
                if (Directory.Exists(entry) && !IsReparsePoint(entry))
                {
                    DeleteTreeNoFollow(entry);
                }
                else
                {
                    DeleteEntryNoFollow(entry);
                }
                ++removed;
            }
            return removed;
        }

        private void RemoveEmptyDirectories(
            string root,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            var ordered = new List<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                string[] directories = Directory.GetDirectories(current);
                for (int index = 0; index < directories.Length; ++index)
                {
                    string child = RequireContainedPath(directories[index]);
                    if (IsReparsePoint(child))
                    {
                        DeleteEntryNoFollow(child);
                    }
                    else
                    {
                        pending.Push(child);
                        ordered.Add(child);
                    }
                }
            }

            for (int index = ordered.Count - 1; index >= 0; --index)
            {
                string directory = ordered[index];
                if (Directory.Exists(directory) &&
                    Directory.GetFileSystemEntries(directory).Length == 0)
                {
                    Directory.Delete(directory);
                }
            }
        }

        private static LocalModelRecoveryReport RecoveryFailure(
            LocalModelPackageResult result)
        {
            return new LocalModelRecoveryReport(
                false,
                result.Failure,
                result.Detail,
                0,
                0,
                0,
                0);
        }

        private static LocalModelCleanupReport CleanupFailure(
            LocalModelPackageResult result)
        {
            return new LocalModelCleanupReport(
                false,
                result.Failure,
                result.Detail,
                0,
                0,
                0,
                0);
        }
    }
}

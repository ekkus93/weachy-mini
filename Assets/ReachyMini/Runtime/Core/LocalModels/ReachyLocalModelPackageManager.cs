#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public sealed class LocalModelPackageManager : IDisposable
    {
        private const string MarkerFileName = ".reachy-local-model-store-v1";
        private const string MarkerContents = "reachy-local-model-store-v1\n";
        private const string MarkerTemporaryFileName = ".reachy-local-model-store-v1.tmp";
        private const string InstalledDirectoryName = "installed";
        private const string StagingDirectoryName = "staging";
        private const string QuarantineDirectoryName = "quarantine";
        private const string DownloadPartFileName = "artifact.download.part";
        private const string DownloadMetadataFileName = "artifact.download.meta";
        private const string ImportPartFileName = "artifact.import.part";

        private readonly string rootPath;
        private readonly string rootPathWithSeparator;
        private readonly string installedRoot;
        private readonly string stagingRoot;
        private readonly string quarantineRoot;
        private readonly ILocalModelStorageProbe storageProbe;
        private readonly LocalModelPackageOptions options;
        private readonly SemaphoreSlim operationGate = new SemaphoreSlim(1, 1);
        private int disposed;

        public LocalModelPackageManager(
            string managedStoreRoot,
            ILocalModelStorageProbe storageProbe,
            LocalModelPackageOptions? options = null)
        {
            if (managedStoreRoot == null)
            {
                throw new ArgumentNullException(nameof(managedStoreRoot));
            }
            if (string.IsNullOrWhiteSpace(managedStoreRoot))
            {
                throw new ArgumentException(
                    "The local-model store root cannot be empty.",
                    nameof(managedStoreRoot));
            }
            if (!Path.IsPathRooted(managedStoreRoot))
            {
                throw new ArgumentException(
                    "The local-model store root must be an absolute path.",
                    nameof(managedStoreRoot));
            }

            string fullRoot = Path.GetFullPath(managedStoreRoot);
            string filesystemRoot = Path.GetPathRoot(fullRoot) ??
                throw new ArgumentException(
                    "The local-model store must have a filesystem root.",
                    nameof(managedStoreRoot));
            if (string.Equals(fullRoot, filesystemRoot, GetPathComparison()))
            {
                throw new ArgumentException(
                    "The local-model store cannot own an entire filesystem root.",
                    nameof(managedStoreRoot));
            }

            rootPath = fullRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            rootPathWithSeparator = rootPath + Path.DirectorySeparatorChar;
            installedRoot = RequireContainedPath(
                Path.Combine(rootPath, InstalledDirectoryName));
            stagingRoot = RequireContainedPath(
                Path.Combine(rootPath, StagingDirectoryName));
            quarantineRoot = RequireContainedPath(
                Path.Combine(rootPath, QuarantineDirectoryName));
            this.storageProbe = storageProbe ??
                throw new ArgumentNullException(nameof(storageProbe));
            this.options = options ?? new LocalModelPackageOptions();
        }

        public string ManagedStoreRoot => rootPath;

        public async Task<LocalModelPackageResult> DownloadAsync(
            LocalModelManifest manifest,
            Uri artifactUri,
            ILocalModelDownloadTransport transport,
            CancellationToken cancellationToken)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (artifactUri == null)
            {
                throw new ArgumentNullException(nameof(artifactUri));
            }
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            ThrowIfDisposed();
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LocalModelPackageResult? readiness = EnsureReadyAndValidateArtifact(manifest);
                if (readiness != null)
                {
                    return readiness;
                }

                LocalModelPackageResult? uriFailure =
                    ValidateDownloadUri(manifest, artifactUri);
                if (uriFailure != null)
                {
                    return uriFailure;
                }

                LocalModelPackageResult existing =
                    await ResolveInstalledCoreAsync(manifest, cancellationToken)
                        .ConfigureAwait(false);
                if (existing.Succeeded)
                {
                    return LocalModelPackageResult.Success(
                        LocalModelPackageOutcome.AlreadyInstalled,
                        "The exact verified local-model artifact is already installed.",
                        existing.Artifact);
                }
                if (existing.Failure != LocalModelPackageFailure.NotInstalled &&
                    existing.Failure != LocalModelPackageFailure.InstalledArtifactCorrupt)
                {
                    return existing;
                }

                return await DownloadCoreAsync(
                        manifest,
                        artifactUri,
                        transport,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<LocalModelPackageResult> ImportAsync(
            LocalModelManifest manifest,
            Stream source,
            CancellationToken cancellationToken)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (!source.CanRead)
            {
                throw new ArgumentException(
                    "Local-model import streams must be readable.",
                    nameof(source));
            }

            ThrowIfDisposed();
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LocalModelPackageResult? readiness = EnsureReadyAndValidateArtifact(manifest);
                if (readiness != null)
                {
                    return readiness;
                }

                LocalModelPackageResult existing =
                    await ResolveInstalledCoreAsync(manifest, cancellationToken)
                        .ConfigureAwait(false);
                if (existing.Succeeded)
                {
                    return LocalModelPackageResult.Success(
                        LocalModelPackageOutcome.AlreadyInstalled,
                        "The exact verified local-model artifact is already installed.",
                        existing.Artifact);
                }
                if (existing.Failure != LocalModelPackageFailure.NotInstalled &&
                    existing.Failure != LocalModelPackageFailure.InstalledArtifactCorrupt)
                {
                    return existing;
                }

                LocalModelPackageResult? storageFailure =
                    CheckStorage(manifest.Artifact.FileSizeBytes);
                if (storageFailure != null)
                {
                    return storageFailure;
                }

                string stagingDirectory = GetStagingDirectory(manifest);
                LocalModelPackageResult? stagingFailure =
                    EnsureManagedDirectoryTree(stagingDirectory);
                if (stagingFailure != null)
                {
                    return stagingFailure;
                }

                string partPath = GetImportPartPath(manifest);
                LocalModelPackageResult? pathFailure = RejectReparsePoint(partPath);
                if (pathFailure != null)
                {
                    return pathFailure;
                }

                DeleteFileIfPresent(partPath);
                CopyExactResult copy;
                try
                {
                    copy = await CopyExactAsync(
                            source,
                            partPath,
                            manifest.Artifact.FileSizeBytes,
                            append: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    DeleteFileIfPresent(partPath);
                    throw;
                }
                catch (IOException exception)
                {
                    DeleteFileIfPresent(partPath);
                    return IoFailure("Import write failed", exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    DeleteFileIfPresent(partPath);
                    return IoFailure("Import write was denied", exception);
                }

                if (copy.HasExtraBytes ||
                    copy.BytesWritten != manifest.Artifact.FileSizeBytes)
                {
                    DeleteFileIfPresent(partPath);
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SizeMismatch,
                        copy.HasExtraBytes
                            ? "The imported model contains more bytes than the manifest permits."
                            : "The imported model ended before the manifest's exact file size.");
                }

                LocalModelPackageResult? hashFailure =
                    await VerifyHashAsync(manifest, partPath, cancellationToken)
                        .ConfigureAwait(false);
                if (hashFailure != null)
                {
                    DeleteFileIfPresent(partPath);
                    return hashFailure;
                }

                LocalModelPackageResult finalized =
                    await FinalizeVerifiedStagingAsync(
                            manifest,
                            partPath,
                            LocalModelPackageOutcome.Imported,
                            copy.BytesWritten,
                            resumed: false,
                            restarted: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (!finalized.Succeeded)
                {
                    DeleteFileIfPresent(partPath);
                }
                return finalized;
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<LocalModelPackageResult> ResolveInstalledAsync(
            LocalModelManifest manifest,
            CancellationToken cancellationToken)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ThrowIfDisposed();
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LocalModelPackageResult? readiness = EnsureReadyAndValidateArtifact(manifest);
                if (readiness != null)
                {
                    return readiness;
                }
                return await ResolveInstalledCoreAsync(manifest, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                operationGate.Release();
            }
        }

        public async Task<LocalModelPackageResult> DeleteAsync(
            LocalModelManifest manifest,
            CancellationToken cancellationToken)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ThrowIfDisposed();
            await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LocalModelPackageResult? readiness = EnsureStoreReady();
                if (readiness != null)
                {
                    return readiness;
                }

                string finalPath = GetInstalledArtifactPath(manifest);
                LocalModelPackageResult? pathFailure = RejectReparsePoint(finalPath);
                if (pathFailure != null)
                {
                    return pathFailure;
                }

                if (!File.Exists(finalPath))
                {
                    return LocalModelPackageResult.Success(
                        LocalModelPackageOutcome.NotInstalled,
                        "The requested exact local-model artifact is not installed.");
                }

                try
                {
                    File.Delete(finalPath);
                    DeleteEmptyParents(
                        Path.GetDirectoryName(finalPath),
                        installedRoot);
                    return LocalModelPackageResult.Success(
                        LocalModelPackageOutcome.Deleted,
                        "The exact installed local-model artifact was deleted.");
                }
                catch (IOException exception)
                {
                    return IoFailure("Deleting the installed model failed", exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    return IoFailure("Deleting the installed model was denied", exception);
                }
            }
            finally
            {
                operationGate.Release();
            }
        }

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

                int stagingRemoved = RemoveUnknownHashDirectories(
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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                operationGate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(LocalModelPackageManager));
            }
        }

        private async Task<LocalModelPackageResult> DownloadCoreAsync(
            LocalModelManifest manifest,
            Uri artifactUri,
            ILocalModelDownloadTransport transport,
            CancellationToken cancellationToken)
        {
            string stagingDirectory = GetStagingDirectory(manifest);
            LocalModelPackageResult? stagingFailure =
                EnsureManagedDirectoryTree(stagingDirectory);
            if (stagingFailure != null)
            {
                return stagingFailure;
            }

            string partPath = GetDownloadPartPath(manifest);
            string metadataPath = GetDownloadMetadataPath(manifest);
            LocalModelPackageResult? pathFailure =
                RejectReparsePoint(partPath) ?? RejectReparsePoint(metadataPath);
            if (pathFailure != null)
            {
                return pathFailure;
            }

            string sourceFingerprint = Sha256Text(artifactUri.AbsoluteUri);
            long offset;
            try
            {
                offset = PrepareDownloadPartial(
                    manifest,
                    partPath,
                    metadataPath,
                    sourceFingerprint);
            }
            catch (IOException exception)
            {
                return IoFailure("Preparing the resumable download failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Preparing the resumable download was denied", exception);
            }

            LocalModelPackageResult? storageFailure =
                CheckStorage(manifest.Artifact.FileSizeBytes - offset);
            if (storageFailure != null)
            {
                return storageFailure;
            }

            bool resumed = offset > 0L;
            bool restarted = false;
            LocalModelDownloadResponse? response =
                await OpenTransportAsync(
                        transport,
                        artifactUri,
                        offset,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (response == null)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.SourceUnavailable,
                    "The explicit download transport returned no response.");
            }

            using (response)
            {
                if (response.Kind == LocalModelDownloadResponseKind.Rejected)
                {
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SourceUnavailable,
                        BoundedDetail(
                            response.Detail,
                            "The explicit model source rejected the download."));
                }

                if (response.Kind == LocalModelDownloadResponseKind.RestartRequired)
                {
                    return await RestartDownloadAsync(
                            manifest,
                            artifactUri,
                            transport,
                            partPath,
                            metadataPath,
                            sourceFingerprint,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (response.Kind != LocalModelDownloadResponseKind.Content)
                {
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SourceUnavailable,
                        "The explicit model source returned no content.");
                }

                if (response.TotalSizeBytes.HasValue &&
                    response.TotalSizeBytes.Value != manifest.Artifact.FileSizeBytes)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SizeMismatch,
                        "The download source declared a total size that differs from the manifest.");
                }

                if (offset > 0L && response.ResponseOffset == 0L)
                {
                    restarted = true;
                    resumed = false;
                    offset = 0L;
                    DeleteFileIfPresent(partPath);
                    storageFailure = CheckStorage(manifest.Artifact.FileSizeBytes);
                    if (storageFailure != null)
                    {
                        return storageFailure;
                    }
                }
                else if (response.ResponseOffset != offset)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.ResumeProtocolViolation,
                        "The model source returned bytes from an unexpected resume offset.");
                }

                CopyExactResult copy;
                try
                {
                    copy = await CopyExactAsync(
                            response.Content,
                            partPath,
                            manifest.Artifact.FileSizeBytes - offset,
                            append: offset > 0L,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException exception)
                {
                    return IoFailure(
                        "Writing the resumable model download failed",
                        exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    return IoFailure(
                        "Writing the resumable model download was denied",
                        exception);
                }

                if (copy.HasExtraBytes)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SizeMismatch,
                        "The model source returned more bytes than the manifest permits.");
                }

                if (offset + copy.BytesWritten != manifest.Artifact.FileSizeBytes)
                {
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SizeMismatch,
                        "The model download ended early; the manifest-bound partial remains resumable.");
                }

                LocalModelPackageResult? hashFailure =
                    await VerifyHashAsync(manifest, partPath, cancellationToken)
                        .ConfigureAwait(false);
                if (hashFailure != null)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return hashFailure;
                }

                LocalModelPackageResult finalized =
                    await FinalizeVerifiedStagingAsync(
                            manifest,
                            partPath,
                            LocalModelPackageOutcome.Installed,
                            copy.BytesWritten,
                            resumed,
                            restarted,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (finalized.Succeeded)
                {
                    DeleteFileIfPresent(metadataPath);
                }
                return finalized;
            }
        }

        private async Task<LocalModelPackageResult> RestartDownloadAsync(
            LocalModelManifest manifest,
            Uri artifactUri,
            ILocalModelDownloadTransport transport,
            string partPath,
            string metadataPath,
            string sourceFingerprint,
            CancellationToken cancellationToken)
        {
            DeleteFileIfPresent(partPath);
            DeleteFileIfPresent(metadataPath);
            try
            {
                WriteDownloadMetadataAtomic(
                    metadataPath,
                    manifest,
                    sourceFingerprint);
            }
            catch (IOException exception)
            {
                return IoFailure("Resetting resumable download metadata failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Resetting resumable download metadata was denied", exception);
            }

            LocalModelPackageResult? storageFailure =
                CheckStorage(manifest.Artifact.FileSizeBytes);
            if (storageFailure != null)
            {
                return storageFailure;
            }

            LocalModelDownloadResponse? response =
                await OpenTransportAsync(
                        transport,
                        artifactUri,
                        0L,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (response == null)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.SourceUnavailable,
                    "The explicit model source returned no response after a clean restart.");
            }

            using (response)
            {
                if (response.Kind != LocalModelDownloadResponseKind.Content ||
                    response.ResponseOffset != 0L)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.ResumeProtocolViolation,
                        "The model source did not provide full content after a clean restart.");
                }
                if (response.TotalSizeBytes.HasValue &&
                    response.TotalSizeBytes.Value != manifest.Artifact.FileSizeBytes)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SizeMismatch,
                        "The restarted model source declared a size that differs from the manifest.");
                }

                CopyExactResult copy;
                try
                {
                    copy = await CopyExactAsync(
                            response.Content,
                            partPath,
                            manifest.Artifact.FileSizeBytes,
                            append: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException exception)
                {
                    return IoFailure(
                        "Writing the restarted model download failed",
                        exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    return IoFailure(
                        "Writing the restarted model download was denied",
                        exception);
                }

                if (copy.HasExtraBytes ||
                    copy.BytesWritten != manifest.Artifact.FileSizeBytes)
                {
                    if (copy.HasExtraBytes)
                    {
                        DeleteFileIfPresent(partPath);
                        DeleteFileIfPresent(metadataPath);
                    }
                    return LocalModelPackageResult.Failed(
                        LocalModelPackageFailure.SizeMismatch,
                        copy.HasExtraBytes
                            ? "The restarted model source returned too many bytes."
                            : "The restarted model download ended early and remains resumable.");
                }

                LocalModelPackageResult? hashFailure =
                    await VerifyHashAsync(manifest, partPath, cancellationToken)
                        .ConfigureAwait(false);
                if (hashFailure != null)
                {
                    DeleteFileIfPresent(partPath);
                    DeleteFileIfPresent(metadataPath);
                    return hashFailure;
                }

                LocalModelPackageResult finalized =
                    await FinalizeVerifiedStagingAsync(
                            manifest,
                            partPath,
                            LocalModelPackageOutcome.Installed,
                            copy.BytesWritten,
                            resumed: false,
                            restarted: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (finalized.Succeeded)
                {
                    DeleteFileIfPresent(metadataPath);
                }
                return finalized;
            }
        }

        private static async Task<LocalModelDownloadResponse?> OpenTransportAsync(
            ILocalModelDownloadTransport transport,
            Uri artifactUri,
            long offset,
            CancellationToken cancellationToken)
        {
            try
            {
                return await transport.OpenAsync(
                        artifactUri,
                        offset,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException)
            {
                return LocalModelDownloadResponse.CreateRejected(
                    "The explicit model source encountered an I/O failure.");
            }
            catch (UnauthorizedAccessException)
            {
                return LocalModelDownloadResponse.CreateRejected(
                    "The explicit model source denied access.");
            }
            catch (InvalidOperationException)
            {
                return LocalModelDownloadResponse.CreateRejected(
                    "The explicit model source rejected the request.");
            }
            catch (NotSupportedException)
            {
                return LocalModelDownloadResponse.CreateRejected(
                    "The explicit model source does not support this request.");
            }
        }

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

        private static LocalModelPackageResult? ValidateDownloadUri(
            LocalModelManifest manifest,
            Uri artifactUri)
        {
            if (!artifactUri.IsAbsoluteUri ||
                artifactUri.AbsoluteUri.Length > LocalModelPackagePolicy.MaximumDownloadUriLength ||
                !string.Equals(
                    artifactUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(artifactUri.Host) ||
                !string.IsNullOrEmpty(artifactUri.UserInfo) ||
                !string.IsNullOrEmpty(artifactUri.Fragment))
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.DownloadUriRejected,
                    "Model downloads require an explicit absolute HTTPS URI without credentials or a fragment.");
            }

            Uri provenance = manifest.Identity.SourceUri;
            if (!string.Equals(
                    provenance.Host,
                    artifactUri.Host,
                    StringComparison.OrdinalIgnoreCase) ||
                provenance.Port != artifactUri.Port)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.DownloadUriRejected,
                    "The initial model download URI must use the manifest provenance host and port.");
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

        private static long PrepareDownloadPartial(
            LocalModelManifest manifest,
            string partPath,
            string metadataPath,
            string sourceFingerprint)
        {
            if (!File.Exists(partPath) && !File.Exists(metadataPath))
            {
                WriteDownloadMetadataAtomic(
                    metadataPath,
                    manifest,
                    sourceFingerprint);
                return 0L;
            }

            bool matches =
                File.Exists(partPath) &&
                File.Exists(metadataPath) &&
                TryReadDownloadMetadata(
                    metadataPath,
                    manifest,
                    out string existingSourceFingerprint) &&
                string.Equals(
                    existingSourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal);

            if (!matches)
            {
                DeleteFileIfPresent(partPath);
                DeleteFileIfPresent(metadataPath);
                WriteDownloadMetadataAtomic(
                    metadataPath,
                    manifest,
                    sourceFingerprint);
                return 0L;
            }

            long size = new FileInfo(partPath).Length;
            if (size > manifest.Artifact.FileSizeBytes)
            {
                DeleteFileIfPresent(partPath);
                DeleteFileIfPresent(metadataPath);
                WriteDownloadMetadataAtomic(
                    metadataPath,
                    manifest,
                    sourceFingerprint);
                return 0L;
            }
            return size;
        }

        private static void WriteDownloadMetadataAtomic(
            string metadataPath,
            LocalModelManifest manifest,
            string sourceFingerprint)
        {
            string temporaryPath = metadataPath + ".tmp";
            DeleteFileIfPresent(temporaryPath);
            string contents =
                "rma132-download-v1\n" +
                "source_sha256=" + sourceFingerprint + "\n" +
                "expected_size=" +
                manifest.Artifact.FileSizeBytes.ToString(CultureInfo.InvariantCulture) +
                "\n" +
                "artifact_sha256=" + manifest.Artifact.Sha256 + "\n";
            File.WriteAllText(
                temporaryPath,
                contents,
                new UTF8Encoding(false));
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
            File.Move(temporaryPath, metadataPath);
        }

        private static bool TryReadDownloadMetadata(
            string metadataPath,
            LocalModelManifest manifest,
            out string sourceFingerprint)
        {
            sourceFingerprint = string.Empty;
            string[] lines = File.ReadAllLines(metadataPath, Encoding.UTF8);
            if (lines.Length != 4 ||
                !string.Equals(lines[0], "rma132-download-v1", StringComparison.Ordinal) ||
                !lines[1].StartsWith("source_sha256=", StringComparison.Ordinal) ||
                !lines[2].StartsWith("expected_size=", StringComparison.Ordinal) ||
                !lines[3].StartsWith("artifact_sha256=", StringComparison.Ordinal))
            {
                return false;
            }

            string fingerprint = lines[1].Substring("source_sha256=".Length);
            string sizeText = lines[2].Substring("expected_size=".Length);
            string artifactSha = lines[3].Substring("artifact_sha256=".Length);
            if (!IsLowercaseSha256(fingerprint) ||
                !long.TryParse(
                    sizeText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long expectedSize) ||
                expectedSize != manifest.Artifact.FileSizeBytes ||
                !string.Equals(
                    artifactSha,
                    manifest.Artifact.Sha256,
                    StringComparison.Ordinal))
            {
                return false;
            }

            sourceFingerprint = fingerprint;
            return true;
        }

        private static async Task<LocalModelPackageResult?> VerifyHashAsync(
            LocalModelManifest manifest,
            string path,
            CancellationToken cancellationToken)
        {
            string actual;
            try
            {
                actual = await ComputeSha256Async(path, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                return IoFailure("Hashing a staged local model failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Hashing a staged local model was denied", exception);
            }

            if (!string.Equals(
                    actual,
                    manifest.Artifact.Sha256,
                    StringComparison.Ordinal))
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.Sha256Mismatch,
                    "The model artifact SHA-256 does not match the manifest.");
            }
            return null;
        }

        private async Task<LocalModelPackageResult> FinalizeVerifiedStagingAsync(
            LocalModelManifest manifest,
            string stagingPath,
            LocalModelPackageOutcome successOutcome,
            long bytesTransferred,
            bool resumed,
            bool restarted,
            CancellationToken cancellationToken)
        {
            string finalPath = GetInstalledArtifactPath(manifest);
            string? finalDirectory = Path.GetDirectoryName(finalPath);
            if (finalDirectory == null)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.StorePathUnsafe,
                    "The managed final model path has no containing directory.");
            }

            LocalModelPackageResult? directoryFailure =
                EnsureManagedDirectoryTree(finalDirectory);
            if (directoryFailure != null)
            {
                return directoryFailure;
            }

            LocalModelPackageResult? pathFailure = RejectReparsePoint(finalPath);
            if (pathFailure != null)
            {
                return pathFailure;
            }

            if (File.Exists(finalPath))
            {
                LocalModelPackageResult existing =
                    await ResolveInstalledCoreAsync(manifest, cancellationToken)
                        .ConfigureAwait(false);
                if (existing.Succeeded)
                {
                    DeleteFileIfPresent(stagingPath);
                    return LocalModelPackageResult.Success(
                        LocalModelPackageOutcome.AlreadyInstalled,
                        "The exact verified local-model artifact is already installed.",
                        existing.Artifact,
                        resumed,
                        restarted,
                        bytesTransferred);
                }

                LocalModelPackageResult quarantine =
                    QuarantineCorruptInstalledFile(manifest, finalPath);
                if (!quarantine.Succeeded)
                {
                    return quarantine;
                }
            }

            try
            {
                File.Move(stagingPath, finalPath);
            }
            catch (IOException exception)
            {
                return IoFailure("Atomic local-model installation failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Atomic local-model installation was denied", exception);
            }

            LocalModelPackageResult verified =
                await ResolveInstalledCoreAsync(manifest, cancellationToken)
                    .ConfigureAwait(false);
            if (!verified.Succeeded)
            {
                LocalModelPackageResult quarantine =
                    QuarantineCorruptInstalledFile(manifest, finalPath);
                return quarantine.Succeeded ? verified : quarantine;
            }

            return LocalModelPackageResult.Success(
                successOutcome,
                "The exact local-model artifact was verified and atomically installed.",
                verified.Artifact,
                resumed,
                restarted,
                bytesTransferred);
        }

        private async Task<LocalModelPackageResult> ResolveInstalledCoreAsync(
            LocalModelManifest manifest,
            CancellationToken cancellationToken)
        {
            string finalPath = GetInstalledArtifactPath(manifest);
            LocalModelPackageResult? pathFailure = RejectReparsePoint(finalPath);
            if (pathFailure != null)
            {
                return pathFailure;
            }
            if (!File.Exists(finalPath))
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.NotInstalled,
                    "The exact local-model artifact is not installed.");
            }

            long fileSize;
            try
            {
                fileSize = new FileInfo(finalPath).Length;
            }
            catch (IOException exception)
            {
                return IoFailure("Reading the installed model size failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Reading the installed model size was denied", exception);
            }

            if (fileSize != manifest.Artifact.FileSizeBytes)
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.InstalledArtifactCorrupt,
                    "The installed model size differs from the manifest; no approved path was issued.");
            }

            string actualHash;
            try
            {
                actualHash = await ComputeSha256Async(finalPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                return IoFailure("Hashing the installed model failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Hashing the installed model was denied", exception);
            }

            if (!string.Equals(
                    actualHash,
                    manifest.Artifact.Sha256,
                    StringComparison.Ordinal))
            {
                return LocalModelPackageResult.Failed(
                    LocalModelPackageFailure.InstalledArtifactCorrupt,
                    "The installed model hash differs from the manifest; no approved path was issued.");
            }

            return LocalModelPackageResult.Success(
                LocalModelPackageOutcome.Resolved,
                "The installed artifact matches the manifest size and SHA-256.",
                new LocalModelApprovedArtifact(
                    manifest.Identity.ManifestId,
                    manifest.Identity.ModelId,
                    finalPath,
                    fileSize,
                    actualHash));
        }

        private LocalModelPackageResult QuarantineCorruptInstalledFile(
            LocalModelManifest manifest,
            string finalPath)
        {
            string quarantineDirectory = RequireContainedPath(
                Path.Combine(
                    quarantineRoot,
                    manifest.Identity.ManifestId,
                    manifest.Artifact.Sha256));
            LocalModelPackageResult? directoryFailure =
                EnsureManagedDirectoryTree(quarantineDirectory);
            if (directoryFailure != null)
            {
                return directoryFailure;
            }

            string quarantinePath = RequireContainedPath(
                Path.Combine(
                    quarantineDirectory,
                    "corrupt-" + Guid.NewGuid().ToString("N") + ".gguf"));
            try
            {
                File.Move(finalPath, quarantinePath);
                return LocalModelPackageResult.Success(
                    LocalModelPackageOutcome.Deleted,
                    "The corrupt installed artifact was quarantined before replacement.");
            }
            catch (IOException exception)
            {
                return IoFailure("Quarantining a corrupt installed model failed", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return IoFailure("Quarantining a corrupt installed model was denied", exception);
            }
        }

        private static async Task<CopyExactResult> CopyExactAsync(
            Stream source,
            string destinationPath,
            long maximumBytes,
            bool append,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[LocalModelPackagePolicy.CopyBufferBytes];
            long written = 0L;
            FileMode mode = append ? FileMode.OpenOrCreate : FileMode.Create;
            using (var destination = new FileStream(
                destinationPath,
                mode,
                FileAccess.Write,
                FileShare.None,
                LocalModelPackagePolicy.CopyBufferBytes,
                useAsync: true))
            {
                if (append)
                {
                    destination.Seek(0L, SeekOrigin.End);
                }

                while (written < maximumBytes)
                {
                    int requestSize = (int)Math.Min(
                        buffer.Length,
                        maximumBytes - written);
                    int read = await source.ReadAsync(
                            buffer.AsMemory(0, requestSize),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    await destination.WriteAsync(
                            buffer.AsMemory(0, read),
                            cancellationToken)
                        .ConfigureAwait(false);
                    written += read;
                }

                bool hasExtra = false;
                if (written == maximumBytes)
                {
                    hasExtra = await source.ReadAsync(
                            buffer.AsMemory(0, 1),
                            cancellationToken)
                        .ConfigureAwait(false) != 0;
                }

                await destination.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new CopyExactResult(written, hasExtra);
            }
        }

        private static async Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[LocalModelPackagePolicy.CopyBufferBytes];
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                LocalModelPackagePolicy.CopyBufferBytes,
                useAsync: true))
            {
                while (true)
                {
                    int read = await stream.ReadAsync(
                            buffer.AsMemory(0, buffer.Length),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    hash.AppendData(buffer, 0, read);
                }
                return ToLowerHex(hash.GetHashAndReset());
            }
        }

        private int RemoveUnknownHashDirectories(
            string managedRoot,
            HashSet<string> expectedDirectories,
            CancellationToken cancellationToken)
        {
            int removed = 0;
            string[] manifestDirectories = Directory.GetDirectories(managedRoot);
            for (int index = 0; index < manifestDirectories.Length; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string manifestDirectory = RequireContainedPath(manifestDirectories[index]);
                if (IsReparsePoint(manifestDirectory))
                {
                    DeleteEntryNoFollow(manifestDirectory);
                    ++removed;
                    continue;
                }

                string[] hashDirectories = Directory.GetDirectories(manifestDirectory);
                for (int hashIndex = 0; hashIndex < hashDirectories.Length; ++hashIndex)
                {
                    string hashDirectory = RequireContainedPath(hashDirectories[hashIndex]);
                    if (!expectedDirectories.Contains(hashDirectory))
                    {
                        DeleteTreeNoFollow(hashDirectory);
                        ++removed;
                    }
                }
                DeleteEmptyParents(manifestDirectory, managedRoot);
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

        private static void DeleteTreeNoFollow(string directory)
        {
            if (IsReparsePoint(directory))
            {
                DeleteEntryNoFollow(directory);
                return;
            }

            string[] entries = Directory.GetFileSystemEntries(directory);
            for (int index = 0; index < entries.Length; ++index)
            {
                string entry = entries[index];
                if (Directory.Exists(entry) && !IsReparsePoint(entry))
                {
                    DeleteTreeNoFollow(entry);
                }
                else
                {
                    DeleteEntryNoFollow(entry);
                }
            }
            Directory.Delete(directory);
        }

        private static void DeleteEntryNoFollow(string path)
        {
            if (Directory.Exists(path) && !File.Exists(path))
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }
        }

        private static void DeleteEmptyParents(
            string? startingDirectory,
            string stopDirectory)
        {
            string stop = Path.GetFullPath(stopDirectory);
            string? current = startingDirectory;
            while (current != null &&
                !string.Equals(
                    Path.GetFullPath(current),
                    stop,
                    GetPathComparison()))
            {
                if (!Directory.Exists(current) ||
                    Directory.GetFileSystemEntries(current).Length != 0)
                {
                    return;
                }
                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
            }
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static void DeleteFileIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string GetInstalledArtifactPath(LocalModelManifest manifest)
        {
            string hashRoot = RequireContainedPath(
                Path.Combine(
                    installedRoot,
                    manifest.Identity.ManifestId,
                    manifest.Artifact.Sha256));
            string relative = manifest.Artifact.RelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
            return RequireContainedPath(Path.Combine(hashRoot, relative));
        }

        private string GetStagingDirectory(LocalModelManifest manifest)
        {
            return RequireContainedPath(
                Path.Combine(
                    stagingRoot,
                    manifest.Identity.ManifestId,
                    manifest.Artifact.Sha256));
        }

        private string GetDownloadPartPath(LocalModelManifest manifest)
        {
            return RequireContainedPath(
                Path.Combine(
                    GetStagingDirectory(manifest),
                    DownloadPartFileName));
        }

        private string GetDownloadMetadataPath(LocalModelManifest manifest)
        {
            return RequireContainedPath(
                Path.Combine(
                    GetStagingDirectory(manifest),
                    DownloadMetadataFileName));
        }

        private string GetImportPartPath(LocalModelManifest manifest)
        {
            return RequireContainedPath(
                Path.Combine(
                    GetStagingDirectory(manifest),
                    ImportPartFileName));
        }

        private string RequireContainedPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(rootPathWithSeparator, GetPathComparison()))
            {
                throw new InvalidOperationException(
                    "A generated local-model path escaped the managed store root.");
            }
            return fullPath;
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private static StringComparer GetPathComparer()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        private static string Sha256Text(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            using (SHA256 hash = SHA256.Create())
            {
                return ToLowerHex(hash.ComputeHash(bytes));
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; ++index)
            {
                builder.Append(
                    bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }
            for (int index = 0; index < value.Length; ++index)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static string BoundedDetail(string detail, string fallback)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return fallback;
            }
            return detail.Length <= 512 ? detail : detail.Substring(0, 512);
        }

        private static LocalModelPackageResult IoFailure(
            string operation,
            Exception exception)
        {
            return LocalModelPackageResult.Failed(
                LocalModelPackageFailure.IoFailure,
                SafeIoDetail(operation, exception));
        }

        private static string SafeIoDetail(
            string operation,
            Exception exception)
        {
            return operation + " (" + exception.GetType().Name + ").";
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

        private readonly struct CopyExactResult
        {
            public CopyExactResult(long bytesWritten, bool hasExtraBytes)
            {
                BytesWritten = bytesWritten;
                HasExtraBytes = hasExtraBytes;
            }

            public long BytesWritten { get; }

            public bool HasExtraBytes { get; }
        }
    }
}

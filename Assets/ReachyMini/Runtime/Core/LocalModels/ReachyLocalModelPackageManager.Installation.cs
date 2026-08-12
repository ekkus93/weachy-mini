#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalModelPackageManager
    {
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
    }
}

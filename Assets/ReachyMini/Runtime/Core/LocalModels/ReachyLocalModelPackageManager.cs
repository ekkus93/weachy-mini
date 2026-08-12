#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalModelPackageManager : IDisposable
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

    }
}

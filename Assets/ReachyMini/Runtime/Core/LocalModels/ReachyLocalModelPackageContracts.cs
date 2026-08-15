#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public static class LocalModelPackagePolicy
    {
        public const long DefaultMaximumArtifactBytes = 8L * 1024L * 1024L * 1024L;

        public const long DefaultSafetyReserveBytes = 64L * 1024L * 1024L;

        public const int CopyBufferBytes = 128 * 1024;

        public const long StorageRecheckIntervalBytes = 4L * 1024L * 1024L;

        public const int MaximumDownloadUriLength = 2048;
    }

    public enum LocalModelPackageFailure
    {
        None = 0,
        StoreOwnershipMismatch = 1,
        StorePathUnsafe = 2,
        StorageProbeFailed = 3,
        InsufficientStorage = 4,
        ArtifactTooLarge = 5,
        DownloadUriRejected = 6,
        SourceUnavailable = 7,
        ResumeProtocolViolation = 8,
        SizeMismatch = 9,
        Sha256Mismatch = 10,
        IoFailure = 11,
        NotInstalled = 12,
        InstalledArtifactCorrupt = 13,
    }

    public enum LocalModelPackageOutcome
    {
        Installed = 0,
        Imported = 1,
        AlreadyInstalled = 2,
        Resolved = 3,
        Deleted = 4,
        NotInstalled = 5,
        Failed = 6,
    }

    public enum LocalModelDownloadResponseKind
    {
        Content = 0,
        RestartRequired = 1,
        Rejected = 2,
    }

    public sealed class LocalModelPackageOptions
    {
        public LocalModelPackageOptions(
            long maximumArtifactBytes = LocalModelPackagePolicy.DefaultMaximumArtifactBytes,
            long safetyReserveBytes = LocalModelPackagePolicy.DefaultSafetyReserveBytes)
        {
            if (maximumArtifactBytes <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
            }
            if (safetyReserveBytes < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(safetyReserveBytes));
            }

            MaximumArtifactBytes = maximumArtifactBytes;
            SafetyReserveBytes = safetyReserveBytes;
        }

        public long MaximumArtifactBytes { get; }

        public long SafetyReserveBytes { get; }
    }

    public interface ILocalModelStorageProbe
    {
        long GetAvailableBytes(string managedStoreRoot);
    }

    public sealed class DriveInfoLocalModelStorageProbe : ILocalModelStorageProbe
    {
        public long GetAvailableBytes(string managedStoreRoot)
        {
            if (managedStoreRoot == null)
            {
                throw new ArgumentNullException(nameof(managedStoreRoot));
            }

            string fullPath = Path.GetFullPath(managedStoreRoot);
            string filesystemRoot = Path.GetPathRoot(fullPath) ??
                throw new InvalidOperationException(
                    "The managed local-model store does not have a filesystem root.");
            var drive = new DriveInfo(filesystemRoot);
            return drive.AvailableFreeSpace;
        }
    }

    public interface ILocalModelDownloadTransport
    {
        Task<LocalModelDownloadResponse> OpenAsync(
            Uri sourceUri,
            long requestedOffset,
            CancellationToken cancellationToken);
    }

    public sealed class LocalModelDownloadResponse : IDisposable
    {
        private Stream? content;

        private LocalModelDownloadResponse(
            LocalModelDownloadResponseKind kind,
            Stream? content,
            long responseOffset,
            long? totalSizeBytes,
            string detail)
        {
            if (responseOffset < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(responseOffset));
            }
            if (totalSizeBytes.HasValue && totalSizeBytes.Value <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(totalSizeBytes));
            }
            if (kind == LocalModelDownloadResponseKind.Content)
            {
                this.content = content ?? throw new ArgumentNullException(nameof(content));
                if (!content.CanRead)
                {
                    throw new ArgumentException(
                        "Download content streams must be readable.",
                        nameof(content));
                }
            }
            else if (content != null)
            {
                throw new ArgumentException(
                    "Non-content download responses cannot carry a stream.",
                    nameof(content));
            }

            Kind = kind;
            ResponseOffset = responseOffset;
            TotalSizeBytes = totalSizeBytes;
            Detail = detail ?? string.Empty;
        }

        public LocalModelDownloadResponseKind Kind { get; }

        public long ResponseOffset { get; }

        public long? TotalSizeBytes { get; }

        public string Detail { get; }

        public Stream Content =>
            content ?? throw new ObjectDisposedException(nameof(LocalModelDownloadResponse));

        public static LocalModelDownloadResponse CreateContent(
            Stream content,
            long responseOffset,
            long? totalSizeBytes = null)
        {
            return new LocalModelDownloadResponse(
                LocalModelDownloadResponseKind.Content,
                content,
                responseOffset,
                totalSizeBytes,
                string.Empty);
        }

        public static LocalModelDownloadResponse CreateRestartRequired(string detail)
        {
            return new LocalModelDownloadResponse(
                LocalModelDownloadResponseKind.RestartRequired,
                null,
                0L,
                null,
                detail ?? string.Empty);
        }

        public static LocalModelDownloadResponse CreateRejected(string detail)
        {
            return new LocalModelDownloadResponse(
                LocalModelDownloadResponseKind.Rejected,
                null,
                0L,
                null,
                detail ?? string.Empty);
        }

        public void Dispose()
        {
            Stream? owned = content;
            content = null;
            if (owned != null)
            {
                owned.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }

    public sealed class LocalModelApprovedArtifact
    {
        internal LocalModelApprovedArtifact(
            string manifestId,
            string modelId,
            string fullPath,
            long fileSizeBytes,
            string sha256)
        {
            ManifestId = manifestId;
            ModelId = modelId;
            FullPath = fullPath;
            FileSizeBytes = fileSizeBytes;
            Sha256 = sha256;
        }

        public string ManifestId { get; }

        public string ModelId { get; }

        public string FullPath { get; }

        public long FileSizeBytes { get; }

        public string Sha256 { get; }
    }

    public sealed class LocalModelPackageResult
    {
        private LocalModelPackageResult(
            bool succeeded,
            LocalModelPackageOutcome outcome,
            LocalModelPackageFailure failure,
            string detail,
            LocalModelApprovedArtifact? artifact,
            bool resumed,
            bool restarted,
            long bytesTransferred)
        {
            Succeeded = succeeded;
            Outcome = outcome;
            Failure = failure;
            Detail = detail;
            Artifact = artifact;
            Resumed = resumed;
            Restarted = restarted;
            BytesTransferred = bytesTransferred;
        }

        public bool Succeeded { get; }

        public LocalModelPackageOutcome Outcome { get; }

        public LocalModelPackageFailure Failure { get; }

        public string Detail { get; }

        public LocalModelApprovedArtifact? Artifact { get; }

        public bool Resumed { get; }

        public bool Restarted { get; }

        public long BytesTransferred { get; }

        internal static LocalModelPackageResult Success(
            LocalModelPackageOutcome outcome,
            string detail,
            LocalModelApprovedArtifact? artifact = null,
            bool resumed = false,
            bool restarted = false,
            long bytesTransferred = 0L)
        {
            return new LocalModelPackageResult(
                true,
                outcome,
                LocalModelPackageFailure.None,
                detail,
                artifact,
                resumed,
                restarted,
                bytesTransferred);
        }

        internal static LocalModelPackageResult Failed(
            LocalModelPackageFailure failure,
            string detail)
        {
            if (failure == LocalModelPackageFailure.None)
            {
                throw new ArgumentOutOfRangeException(nameof(failure));
            }

            return new LocalModelPackageResult(
                false,
                LocalModelPackageOutcome.Failed,
                failure,
                detail,
                null,
                false,
                false,
                0L);
        }
    }

    public sealed class LocalModelRecoveryReport
    {
        internal LocalModelRecoveryReport(
            bool succeeded,
            LocalModelPackageFailure failure,
            string detail,
            int removedImportPartials,
            int removedInvalidDownloadPartials,
            int retainedResumableDownloads,
            int corruptInstalledArtifacts)
        {
            Succeeded = succeeded;
            Failure = failure;
            Detail = detail;
            RemovedImportPartials = removedImportPartials;
            RemovedInvalidDownloadPartials = removedInvalidDownloadPartials;
            RetainedResumableDownloads = retainedResumableDownloads;
            CorruptInstalledArtifacts = corruptInstalledArtifacts;
        }

        public bool Succeeded { get; }

        public LocalModelPackageFailure Failure { get; }

        public string Detail { get; }

        public int RemovedImportPartials { get; }

        public int RemovedInvalidDownloadPartials { get; }

        public int RetainedResumableDownloads { get; }

        public int CorruptInstalledArtifacts { get; }
    }

    public sealed class LocalModelCleanupReport
    {
        internal LocalModelCleanupReport(
            bool succeeded,
            LocalModelPackageFailure failure,
            string detail,
            int removedStagingEntries,
            int removedQuarantineEntries,
            int removedInstalledOrphans,
            int corruptKnownArtifacts)
        {
            Succeeded = succeeded;
            Failure = failure;
            Detail = detail;
            RemovedStagingEntries = removedStagingEntries;
            RemovedQuarantineEntries = removedQuarantineEntries;
            RemovedInstalledOrphans = removedInstalledOrphans;
            CorruptKnownArtifacts = corruptKnownArtifacts;
        }

        public bool Succeeded { get; }

        public LocalModelPackageFailure Failure { get; }

        public string Detail { get; }

        public int RemovedStagingEntries { get; }

        public int RemovedQuarantineEntries { get; }

        public int RemovedInstalledOrphans { get; }

        public int CorruptKnownArtifacts { get; }
    }
}

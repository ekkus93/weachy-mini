#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReachyMini.LocalModels
{
    public sealed partial class LocalModelPackageManager
    {
        private async Task<CopyExactResult> CopyExactAsync(
            Stream source,
            string destinationPath,
            long maximumBytes,
            bool append,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[LocalModelPackagePolicy.CopyBufferBytes];
            long written = 0L;
            long nextStorageCheck = LocalModelPackagePolicy.StorageRecheckIntervalBytes;
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
                    if (written >= nextStorageCheck)
                    {
                        LocalModelPackageResult? storageFailure =
                            CheckStorage(maximumBytes - written);
                        if (storageFailure != null)
                        {
                            throw new StoragePressureIOException(storageFailure);
                        }
                        nextStorageCheck = checked(
                            written + LocalModelPackagePolicy.StorageRecheckIntervalBytes);
                    }

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
                    try
                    {
                        await destination.WriteAsync(
                                buffer.AsMemory(0, read),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (IOException exception)
                    {
                        LocalModelPackageResult? storageFailure =
                            CheckStorage(maximumBytes - written);
                        if (storageFailure != null)
                        {
                            throw new StoragePressureIOException(
                                storageFailure,
                                exception);
                        }
                        throw;
                    }
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

                try
                {
                    await destination.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    LocalModelPackageResult? storageFailure = CheckStorage(0L);
                    if (storageFailure != null)
                    {
                        throw new StoragePressureIOException(
                            storageFailure,
                            exception);
                    }
                    throw;
                }
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
            if (exception is StoragePressureIOException pressure)
            {
                return pressure.Failure;
            }
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

        private sealed class StoragePressureIOException : IOException
        {
            public StoragePressureIOException(
                LocalModelPackageResult failure,
                Exception? innerException = null)
                : base(
                    "Free storage fell below the local-model acquisition reserve.",
                    innerException)
            {
                Failure = failure ?? throw new ArgumentNullException(nameof(failure));
            }

            public LocalModelPackageResult Failure { get; }
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

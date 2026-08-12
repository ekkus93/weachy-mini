#nullable enable

using System;
using System.IO;
using System.Threading;

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
    }
}

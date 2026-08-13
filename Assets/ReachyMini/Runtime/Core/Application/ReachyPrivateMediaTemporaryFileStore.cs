#nullable enable

using System;
using System.IO;

namespace ReachyMini.AppState
{
    public sealed class ReachyPrivateMediaTemporaryFileStore
    {
        private const string StoreDirectoryName = "reachy-private-media";

        public ReachyPrivateMediaTemporaryFileStore(string applicationPrivateCacheRoot)
        {
            if (string.IsNullOrWhiteSpace(applicationPrivateCacheRoot))
            {
                throw new ArgumentException(
                    "Temporary media storage requires an application-private cache root.",
                    nameof(applicationPrivateCacheRoot));
            }

            string fullCacheRoot = Path.GetFullPath(applicationPrivateCacheRoot);
            RootPath = Path.Combine(fullCacheRoot, StoreDirectoryName);
        }

        public string RootPath { get; }

        public ReachyPrivateMediaTemporaryFileLease Create(
            ReachyPrivateMediaKind kind,
            byte[] content)
        {
            _ = ReachyPrivateMediaRetentionPolicy.IsPersistentMediaRetentionAllowed(kind);
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (content.Length == 0)
            {
                throw new ArgumentException(
                    "Temporary media content cannot be empty.",
                    nameof(content));
            }

            Directory.CreateDirectory(RootPath);
            string path = Path.Combine(
                RootPath,
                Guid.NewGuid().ToString("N") + ".media");
            try
            {
                File.WriteAllBytes(path, content);
                return new ReachyPrivateMediaTemporaryFileLease(path);
            }
            catch
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                throw;
            }
        }

        public int PurgeAbandonedFiles()
        {
            if (!Directory.Exists(RootPath))
            {
                return 0;
            }

            int deleted = 0;
            foreach (string path in Directory.EnumerateFiles(
                         RootPath,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                File.Delete(path);
                deleted = checked(deleted + 1);
            }
            return deleted;
        }
    }

    public sealed class ReachyPrivateMediaTemporaryFileLease : IDisposable
    {
        private string? path;

        internal ReachyPrivateMediaTemporaryFileLease(string path)
        {
            this.path = path;
        }

        public string Path => path ?? throw new ObjectDisposedException(
            nameof(ReachyPrivateMediaTemporaryFileLease));

        public void Dispose()
        {
            string? currentPath = path;
            if (currentPath == null)
            {
                return;
            }
            if (File.Exists(currentPath))
            {
                File.Delete(currentPath);
            }
            path = null;
        }
    }
}

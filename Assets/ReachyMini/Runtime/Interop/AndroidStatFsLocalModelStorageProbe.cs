#nullable enable

using System;
using System.IO;
using ReachyMini.LocalModels;

namespace ReachyMini.Interop
{
    public sealed class AndroidStatFsLocalModelStorageProbe : ILocalModelStorageProbe
    {
        public long GetAvailableBytes(string managedStoreRoot)
        {
            if (managedStoreRoot == null)
            {
                throw new ArgumentNullException(nameof(managedStoreRoot));
            }
            if (string.IsNullOrWhiteSpace(managedStoreRoot))
            {
                throw new ArgumentException(
                    "The managed local-model store root cannot be empty.",
                    nameof(managedStoreRoot));
            }

            string fullPath = Path.GetFullPath(managedStoreRoot);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(
                    "The managed local-model store root does not exist: " + fullPath);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            using var statFs = new UnityEngine.AndroidJavaObject(
                "android.os.StatFs",
                fullPath);
            long availableBytes = statFs.Call<long>("getAvailableBytes");
            if (availableBytes < 0L)
            {
                throw new InvalidOperationException(
                    "Android StatFs returned a negative available-byte count.");
            }
            return availableBytes;
#else
            throw new PlatformNotSupportedException(
                "Android StatFs local-model storage probing requires an Android player build.");
#endif
        }
    }
}

#nullable enable

using System;
using System.IO;
using ReachyMini.LocalModels;
using UnityEngine;

namespace ReachyMini.AppState
{
    /// <summary>
    /// Reads available space using the platform API that supports the app's
    /// private storage path on Android players.
    /// </summary>
    public sealed class ReachyPlatformLocalModelStorageProbe :
        ILocalModelStorageProbe
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly int ownerManagedThreadId =
            Environment.CurrentManagedThreadId;
#endif

        public long GetAvailableBytes(string managedStoreRoot)
        {
            if (string.IsNullOrWhiteSpace(managedStoreRoot))
            {
                throw new ArgumentException(
                    "The managed model-store path cannot be empty.",
                    nameof(managedStoreRoot));
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            bool detachCurrentThread = false;
            if (Environment.CurrentManagedThreadId != ownerManagedThreadId)
            {
                int attachStatus = AndroidJNI.AttachCurrentThread();
                if (attachStatus < 0)
                {
                    throw new InvalidOperationException(
                        "Android storage monitoring could not attach its worker thread to the JVM.");
                }
                detachCurrentThread = true;
            }

            try
            {
                using var stats = new AndroidJavaObject(
                    "android.os.StatFs",
                    Path.GetFullPath(managedStoreRoot));
                long availableBytes = stats.Call<long>("getAvailableBytes");
                if (availableBytes < 0L)
                {
                    throw new InvalidOperationException(
                        "Android returned a negative available-storage value.");
                }
                return availableBytes;
            }
            catch (AndroidJavaException exception)
            {
                throw new InvalidOperationException(
                    "Android could not read available model-store space.",
                    exception);
            }
            finally
            {
                if (detachCurrentThread)
                {
                    int detachStatus = AndroidJNI.DetachCurrentThread();
                    if (detachStatus < 0)
                    {
                        throw new InvalidOperationException(
                            "Android storage monitoring could not detach its worker thread from the JVM.");
                    }
                }
            }
#else
            string fullPath = Path.GetFullPath(managedStoreRoot);
            string filesystemRoot = Path.GetPathRoot(fullPath) ??
                throw new InvalidOperationException(
                    "The managed model store does not have a filesystem root.");
            return new DriveInfo(filesystemRoot).AvailableFreeSpace;
#endif
        }
    }
}

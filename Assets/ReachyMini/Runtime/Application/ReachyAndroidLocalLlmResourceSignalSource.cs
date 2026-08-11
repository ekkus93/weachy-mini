#nullable enable

using System;
using ReachyMini.LocalModels;
using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed class ReachyAndroidLocalLlmResourceSignalSource :
        ILocalLlmResourceSignalSource,
        IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly AndroidJavaObject activityManager;
        private readonly AndroidJavaObject powerManager;
        private readonly int apiLevel;
        private bool disposed;
#endif

        public ReachyAndroidLocalLlmResourceSignalSource()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using AndroidJavaObject activity = GetCurrentActivity();
            activityManager = activity.Call<AndroidJavaObject>(
                "getSystemService",
                "activity") ?? throw new InvalidOperationException(
                    "Android did not return ActivityManager for local LLM resource governance.");
            powerManager = activity.Call<AndroidJavaObject>(
                "getSystemService",
                "power") ?? throw new InvalidOperationException(
                    "Android did not return PowerManager for local LLM resource governance.");
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            apiLevel = version.GetStatic<int>("SDK_INT");
#else
            throw new PlatformNotSupportedException(
                "Android local LLM resource signals are available only in Android player builds.");
#endif
        }

        public LocalLlmResourceSnapshot Capture(
            LocalLlmPhysicsBudgetState physicsBudgetState)
        {
            if (!Enum.IsDefined(typeof(LocalLlmPhysicsBudgetState), physicsBudgetState))
            {
                throw new ArgumentOutOfRangeException(nameof(physicsBudgetState));
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            try
            {
                using var memoryInfo = new AndroidJavaObject(
                    "android.app.ActivityManager$MemoryInfo");
                activityManager.Call("getMemoryInfo", memoryInfo);
                long totalMemory = memoryInfo.Get<long>("totalMem");
                long availableMemory = memoryInfo.Get<long>("availMem");
                long threshold = memoryInfo.Get<long>("threshold");
                bool lowMemory = memoryInfo.Get<bool>("lowMemory");
                if (totalMemory <= 0L || availableMemory < 0L || threshold < 0L)
                {
                    throw new InvalidOperationException(
                        "Android returned invalid ActivityManager memory information.");
                }

                LocalLlmThermalStatus thermalStatus = ReadThermalStatus();
                return new LocalLlmResourceSnapshot(
                    totalMemory,
                    availableMemory,
                    threshold,
                    lowMemory,
                    Environment.ProcessorCount,
                    thermalStatus,
                    physicsBudgetState);
            }
            catch (AndroidJavaException exception)
            {
                throw new InvalidOperationException(
                    "Android local LLM resource signal capture failed.",
                    exception);
            }
#else
            throw new PlatformNotSupportedException(
                "Android local LLM resource signals require an Android player.");
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (disposed)
            {
                return;
            }
            disposed = true;
            activityManager.Dispose();
            powerManager.Dispose();
#endif
            GC.SuppressFinalize(this);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private LocalLlmThermalStatus ReadThermalStatus()
        {
            if (apiLevel < 29)
            {
                return LocalLlmThermalStatus.Unavailable;
            }

            int status = powerManager.Call<int>("getCurrentThermalStatus");
            return status switch
            {
                0 => LocalLlmThermalStatus.None,
                1 => LocalLlmThermalStatus.Light,
                2 => LocalLlmThermalStatus.Moderate,
                3 => LocalLlmThermalStatus.Severe,
                4 => LocalLlmThermalStatus.Critical,
                5 => LocalLlmThermalStatus.Emergency,
                6 => LocalLlmThermalStatus.Shutdown,
                _ => throw new InvalidOperationException(
                    "Android returned an unknown thermal status value: " + status + "."),
            };
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidLocalLlmResourceSignalSource));
            }
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity") ??
                throw new InvalidOperationException(
                    "Unity did not expose an Android currentActivity.");
        }
#endif
    }
}

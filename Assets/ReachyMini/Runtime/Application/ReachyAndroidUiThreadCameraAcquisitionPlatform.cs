#nullable enable

using System;
using System.Threading;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal sealed class ReachyAndroidUiThreadCameraAcquisitionPlatform :
        IReachyDeviceCameraAcquisitionPlatform
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BridgeClassName =
            "com.ekkus93.weachy.camera.ReachyCameraFrameBridge";
        private static readonly TimeSpan UiThreadTimeout =
            TimeSpan.FromSeconds(15);
        private bool disposed;
#endif

        public bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public string Start(
            long sessionId,
            string cameraId,
            int width,
            int height)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            return InvokeOnAndroidUiThread(
                "start",
                activity =>
                {
                    using var bridge = new AndroidJavaClass(BridgeClassName);
                    return bridge.CallStatic<string>(
                        "start",
                        activity,
                        sessionId,
                        cameraId,
                        width,
                        height);
                });
#else
            throw Unsupported();
#endif
        }

        public string Pause()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            return InvokeOnAndroidUiThread(
                "pause",
                activity =>
                {
                    using var bridge = new AndroidJavaClass(BridgeClassName);
                    return bridge.CallStatic<string>("pause", activity);
                });
#else
            throw Unsupported();
#endif
        }

        public string Resume()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            return InvokeOnAndroidUiThread(
                "resume",
                activity =>
                {
                    using var bridge = new AndroidJavaClass(BridgeClassName);
                    return bridge.CallStatic<string>("resume", activity);
                });
#else
            throw Unsupported();
#endif
        }

        public string Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            return InvokeOnAndroidUiThread(
                "stop",
                activity =>
                {
                    using var bridge = new AndroidJavaClass(BridgeClassName);
                    return bridge.CallStatic<string>("stop", activity);
                });
#else
            throw Unsupported();
#endif
        }

        public string Snapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>("snapshot");
#else
            throw Unsupported();
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
            try
            {
                InvokeOnAndroidUiThread(
                    "shutdown",
                    activity =>
                    {
                        using var bridge =
                            new AndroidJavaClass(BridgeClassName);
                        bridge.CallStatic("shutdown", activity);
                        return string.Empty;
                    },
                    allowDisposed: true);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "CameraX shutdown failed on the Android UI thread: " +
                    exception.Message);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private string InvokeOnAndroidUiThread(
            string operation,
            Func<AndroidJavaObject, string> action,
            bool allowDisposed = false)
        {
            if (!allowDisposed)
            {
                ThrowIfDisposed();
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            using AndroidJavaObject activity = GetCurrentActivity();
            using var completed = new ManualResetEventSlim(false);
            string? result = null;
            Exception? failure = null;
            activity.Call(
                "runOnUiThread",
                new AndroidJavaRunnable(
                    () =>
                    {
                        try
                        {
                            result = action(activity);
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                        finally
                        {
                            completed.Set();
                        }
                    }));

            if (!completed.Wait(UiThreadTimeout))
            {
                throw new TimeoutException(
                    $"CameraX {operation} did not complete on the Android UI thread within " +
                    $"{UiThreadTimeout.TotalSeconds:0} seconds.");
            }
            if (failure != null)
            {
                throw new InvalidOperationException(
                    $"CameraX {operation} failed on the Android UI thread: " +
                    failure.Message,
                    failure);
            }
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"CameraX {operation} returned no diagnostics JSON.");
            }
            return result;
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer = new AndroidJavaClass(
                "com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity");
            return activity ?? throw new InvalidOperationException(
                "UnityPlayer.currentActivity is unavailable.");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidUiThreadCameraAcquisitionPlatform));
            }
        }
#endif

        private static PlatformNotSupportedException Unsupported()
        {
            return new PlatformNotSupportedException(
                "Android UI-thread CameraX acquisition is available only in an Android player.");
        }
    }
}

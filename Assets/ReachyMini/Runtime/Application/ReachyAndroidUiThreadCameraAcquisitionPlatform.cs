#nullable enable

using System;
using System.Threading;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal sealed class ReachyAndroidUiThreadCameraAcquisitionPlatform :
        IReachyDeviceCameraAcquisitionPlatform
    {
        private const int MaximumAnalysisWidth = 1280;
        private const int MaximumAnalysisHeight = 720;
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BridgeClassName =
            "com.ekkus93.weachy.camera.ReachyCameraFrameBridge";
        private const string QueuedPauseSnapshot =
            "{\"status\":\"ok\",\"state\":\"Paused\",\"errorCode\":\"\"," +
            "\"message\":\"CameraX lifecycle pause is queued on the Android UI thread.\"}";
        private const string QueuedResumeSnapshot =
            "{\"status\":\"ok\",\"state\":\"Paused\",\"errorCode\":\"\"," +
            "\"message\":\"CameraX lifecycle resume is queued on the Android UI thread.\"}";
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
            SelectAnalysisTarget(
                width,
                height,
                out int targetWidth,
                out int targetHeight);
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
                        targetWidth,
                        targetHeight);
                });
#else
            throw Unsupported();
#endif
        }

        public string Pause()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            PostLifecycleOperationOnAndroidUiThread(
                "pause",
                activity =>
                {
                    using var bridge = new AndroidJavaClass(BridgeClassName);
                    _ = bridge.CallStatic<string>("pause", activity);
                });
            return QueuedPauseSnapshot;
#else
            throw Unsupported();
#endif
        }

        public string Resume()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            PostLifecycleOperationOnAndroidUiThread(
                "resume",
                activity =>
                {
                    using var bridge = new AndroidJavaClass(BridgeClassName);
                    _ = bridge.CallStatic<string>("resume", activity);
                });
            return QueuedResumeSnapshot;
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

        internal static void SelectAnalysisTarget(
            int requestedWidth,
            int requestedHeight,
            out int targetWidth,
            out int targetHeight)
        {
            if (requestedWidth <= 0 || requestedHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedWidth),
                    "Camera analysis dimensions must be positive.");
            }

            if (requestedWidth <= MaximumAnalysisWidth &&
                requestedHeight <= MaximumAnalysisHeight)
            {
                targetWidth = requestedWidth;
                targetHeight = requestedHeight;
                return;
            }

            targetWidth = MaximumAnalysisWidth;
            targetHeight = MaximumAnalysisHeight;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Unity can invoke OnApplicationPause while Android is waiting for the
        // Unity player to finish pausing. Posting without waiting avoids a
        // Unity-thread/Android-UI-thread lifecycle deadlock.
        private static void PostLifecycleOperationOnAndroidUiThread(
            string operation,
            Action<AndroidJavaObject> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            using AndroidJavaObject activity = GetCurrentActivity();
            activity.Call(
                "runOnUiThread",
                new AndroidJavaRunnable(
                    () =>
                    {
                        try
                        {
                            using AndroidJavaObject currentActivity =
                                GetCurrentActivity();
                            action(currentActivity);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogError(
                                $"CameraX {operation} failed on the Android UI thread: " +
                                exception.Message);
                        }
                    }));
        }

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

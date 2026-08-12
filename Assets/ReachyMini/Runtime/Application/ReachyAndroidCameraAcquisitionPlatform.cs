#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    public interface IReachyDeviceCameraAcquisitionPlatform : IDisposable
    {
        bool IsSupported { get; }

        string Start(
            long sessionId,
            string cameraId,
            int width,
            int height);

        string Pause();

        string Resume();

        string Stop();

        IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            long sessionId,
            long afterSequence);

        string Snapshot();
    }

    internal sealed class ReachyUnityAndroidCameraAcquisitionPlatform :
        IReachyDeviceCameraAcquisitionPlatform
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BridgeClassName =
            "com.ekkus93.weachy.camera.ReachyCameraFrameBridge";
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
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>(
                "start",
                activity,
                sessionId,
                cameraId,
                width,
                height) ?? throw new InvalidOperationException(
                    "The Android CameraX bridge returned null from start.");
#else
            throw new PlatformNotSupportedException(
                "CameraX frame acquisition requires an Android player.");
#endif
        }

        public string Pause()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>("pause", activity) ??
                throw new InvalidOperationException(
                    "The Android CameraX bridge returned null from pause.");
#else
            throw new PlatformNotSupportedException(
                "CameraX frame acquisition requires an Android player.");
#endif
        }

        public string Resume()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>("resume", activity) ??
                throw new InvalidOperationException(
                    "The Android CameraX bridge returned null from resume.");
#else
            throw new PlatformNotSupportedException(
                "CameraX frame acquisition requires an Android player.");
#endif
        }

        public string Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>("stop", activity) ??
                throw new InvalidOperationException(
                    "The Android CameraX bridge returned null from stop.");
#else
            throw new PlatformNotSupportedException(
                "CameraX frame acquisition requires an Android player.");
#endif
        }

        public IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            long requestedSessionId,
            long afterSequence)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            AndroidJavaObject? javaLease =
                bridge.CallStatic<AndroidJavaObject>(
                    "acquireLatestTextureFrame",
                    requestedSessionId,
                    afterSequence);
            return javaLease == null
                ? null
                : new ReachyAndroidJavaCameraTextureFrameLease(javaLease);
#else
            throw new PlatformNotSupportedException(
                "CameraX frame acquisition requires an Android player.");
#endif
        }

        public string Snapshot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>("snapshot") ??
                throw new InvalidOperationException(
                    "The Android CameraX bridge returned null from snapshot.");
#else
            throw new PlatformNotSupportedException(
                "CameraX frame acquisition requires an Android player.");
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (disposed)
            {
                return;
            }
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            bridge.CallStatic("shutdown", activity);
            disposed = true;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject GetCurrentActivity()
        {
            using var unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyUnityAndroidCameraAcquisitionPlatform));
            }
        }
#endif
    }
}

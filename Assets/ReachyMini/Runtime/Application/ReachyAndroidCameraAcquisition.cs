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

        string Snapshot();
    }

    [DisallowMultipleComponent]
    public sealed class ReachyAndroidCameraAcquisition : MonoBehaviour
    {
        private const float PollIntervalSeconds = 0.05f;

        private ReachyAndroidCameraDiscovery? discovery;
        private IReachyDeviceCameraAcquisitionPlatform? platform;
        private readonly ReachyCameraAcquisitionStateStore state =
            new ReachyCameraAcquisitionStateStore();
        private bool initialized;
        private bool desiredActive;
        private bool disposed;
        private float nextPollTime;
        private ulong nextSessionId = 1UL;
        private ReachyCameraFacing preferredFacing =
            ReachyCameraFacing.Unconfigured;

        public ReachyCameraAcquisitionStateStore State => state;

        public bool DesiredActive => desiredActive;

        public ReachyCameraFacing PreferredFacing => preferredFacing;

        public void Configure(ReachyAndroidCameraDiscovery cameraDiscovery)
        {
            if (cameraDiscovery == null)
            {
                throw new ArgumentNullException(nameof(cameraDiscovery));
            }
            if (discovery != null && discovery != cameraDiscovery)
            {
                throw new InvalidOperationException(
                    "Camera acquisition discovery cannot change after configuration.");
            }

            EnsureInitialized();
            discovery = cameraDiscovery;
            discovery.State.Changed -= OnCapabilitiesChanged;
            discovery.State.Changed += OnCapabilitiesChanged;
        }

        public void ConfigurePlatformForTests(
            ReachyAndroidCameraDiscovery cameraDiscovery,
            IReachyDeviceCameraAcquisitionPlatform testPlatform)
        {
            if (testPlatform == null)
            {
                throw new ArgumentNullException(nameof(testPlatform));
            }
            if (state.Current.IsActive)
            {
                throw new InvalidOperationException(
                    "The camera acquisition platform cannot change while active.");
            }

            platform?.Dispose();
            platform = testPlatform;
            initialized = true;
            Configure(cameraDiscovery);
        }

        public void Toggle(ReachyCameraFacing facing)
        {
            if (state.Current.IsActive)
            {
                StopAcquisition();
            }
            else
            {
                StartPreferred(facing);
            }
        }

        public void StartPreferred(ReachyCameraFacing facing)
        {
            EnsureReady();
            preferredFacing = facing;
            ReachyCameraCapabilitySnapshot capabilities =
                RequireDiscovery().State.Current;
            if (capabilities.Permission != ReachyCameraPermissionState.Granted)
            {
                desiredActive = false;
                if (capabilities.Permission == ReachyCameraPermissionState.Revoked)
                {
                    state.MarkPermissionRevoked(capabilities.Message);
                }
                else
                {
                    state.MarkUnavailable(capabilities.Message);
                }
                return;
            }

            ReachyCameraCapability? selected =
                SelectCamera(capabilities, facing);
            if (selected == null)
            {
                desiredActive = false;
                state.MarkUnavailable(
                    $"No available {GetFacingLabel(facing)} camera exposes a YUV analysis resolution.");
                return;
            }

            ReachyCameraResolution resolution =
                selected.AnalysisResolutions[0];
            if (state.Current.IsActive)
            {
                StopPlatformForSwitch();
            }

            ulong session = nextSessionId;
            nextSessionId = checked(nextSessionId + 1UL);
            desiredActive = true;
            state.BeginStart(
                session,
                selected.Facing,
                selected.CameraId,
                $"Binding CameraX camera {selected.CameraId} at {resolution}.");
            string json = RequirePlatform().Start(
                checked((long)session),
                selected.CameraId,
                resolution.Width,
                resolution.Height);
            ApplyPlatformSnapshot(json);
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }

        public void StopAcquisition()
        {
            EnsureReady();
            desiredActive = false;
            if (state.Current.IsActive &&
                state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    "Stopping CameraX Preview and ImageAnalysis.");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            if (state.Current.State != ReachyCameraAcquisitionState.Stopped)
            {
                state.MarkStopped(
                    "CameraX Preview and ImageAnalysis are stopped.");
            }
        }

        public void RefreshNow()
        {
            EnsureReady();
            ApplyPlatformSnapshot(RequirePlatform().Snapshot());
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (!initialized || disposed ||
                !state.Current.IsActive ||
                Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
            ApplyPlatformSnapshot(RequirePlatform().Snapshot());
        }

        private void OnApplicationPause(bool paused)
        {
            if (!initialized || disposed || !state.Current.IsActive)
            {
                return;
            }

            if (paused)
            {
                ApplyPlatformSnapshot(RequirePlatform().Pause());
                return;
            }

            ReachyCameraCapabilitySnapshot capabilities =
                RequireDiscovery().State.Current;
            if (capabilities.Permission != ReachyCameraPermissionState.Granted)
            {
                desiredActive = false;
                RequirePlatform().Stop();
                state.MarkPermissionRevoked(
                    "Camera permission is no longer granted after application resume.");
                return;
            }
            if (desiredActive)
            {
                ApplyPlatformSnapshot(RequirePlatform().Resume());
            }
        }

        private void OnDestroy()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (discovery != null)
            {
                discovery.State.Changed -= OnCapabilitiesChanged;
            }
            if (platform != null)
            {
                try
                {
                    if (state.Current.IsActive)
                    {
                        platform.Stop();
                    }
                }
                finally
                {
                    platform.Dispose();
                }
            }
            platform = null;
            discovery = null;
            initialized = false;
            desiredActive = false;
        }

        private void OnCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            ReachyCameraCapabilitySnapshot capabilities = eventArgs.Snapshot;
            if (!state.Current.IsActive)
            {
                return;
            }
            if (capabilities.Permission != ReachyCameraPermissionState.Granted)
            {
                desiredActive = false;
                RequirePlatform().Stop();
                if (capabilities.Permission == ReachyCameraPermissionState.Revoked)
                {
                    state.MarkPermissionRevoked(capabilities.Message);
                }
                else
                {
                    state.MarkUnavailable(capabilities.Message);
                }
                return;
            }

            ReachyCameraCapability? selected = FindCamera(
                capabilities,
                state.Current.CameraId);
            if (selected == null)
            {
                // Some API-26 camera services temporarily omit an application-owned
                // camera from getCameraIdList(). CameraX remains authoritative for
                // the health of an already-bound session.
                return;
            }
            if (!CanRemainBoundToSelectedCamera(selected.Availability))
            {
                string selectedCameraId = state.Current.CameraId;
                desiredActive = false;
                RequirePlatform().Stop();
                state.MarkUnavailable(
                    $"Selected camera '{selectedCameraId}' can no longer remain bound " +
                    $"because discovery reports {selected.Availability}.");
            }
        }

        private void StopPlatformForSwitch()
        {
            if (state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    "Unbinding the current camera before an explicit switch.");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            if (state.Current.State != ReachyCameraAcquisitionState.Stopped)
            {
                state.MarkStopped(
                    "The previous camera is fully unbound before switching.");
            }
        }

        private void ApplyPlatformSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                state.MarkFaulted(
                    "The Android CameraX bridge returned no state JSON.");
                desiredActive = false;
                return;
            }

            ReachyCameraAcquisitionEnvelope? envelope;
            try
            {
                envelope = JsonUtility.FromJson<ReachyCameraAcquisitionEnvelope>(json);
            }
            catch (Exception exception)
            {
                state.MarkFaulted(
                    $"camera_acquisition_json_failed: {exception.Message}");
                desiredActive = false;
                return;
            }
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.state))
            {
                state.MarkFaulted(
                    "The Android CameraX bridge returned an invalid state object.");
                desiredActive = false;
                return;
            }

            string detail = string.IsNullOrWhiteSpace(envelope.message)
                ? "The Android CameraX bridge changed state without diagnostics."
                : envelope.message;
            switch (envelope.state)
            {
                case "Starting":
                    break;
                case "Running":
                    if (state.Current.State == ReachyCameraAcquisitionState.Starting ||
                        state.Current.State == ReachyCameraAcquisitionState.Paused)
                    {
                        state.MarkRunning(detail);
                    }
                    PublishFrame(envelope.latestFrame);
                    break;
                case "Paused":
                    if (state.Current.State == ReachyCameraAcquisitionState.Starting ||
                        state.Current.State == ReachyCameraAcquisitionState.Running)
                    {
                        state.MarkPaused(detail);
                    }
                    break;
                case "Stopping":
                    if (state.Current.IsActive &&
                        state.Current.State != ReachyCameraAcquisitionState.Stopping)
                    {
                        state.BeginStop(detail);
                    }
                    break;
                case "Stopped":
                    state.MarkStopped(detail);
                    break;
                case "PermissionRevoked":
                    desiredActive = false;
                    state.MarkPermissionRevoked(detail);
                    break;
                case "Unavailable":
                    desiredActive = false;
                    state.MarkUnavailable(
                        PrefixError(envelope.errorCode, detail));
                    break;
                case "Faulted":
                    desiredActive = false;
                    state.MarkFaulted(
                        PrefixError(envelope.errorCode, detail));
                    break;
                default:
                    desiredActive = false;
                    state.MarkFaulted(
                        $"camera_acquisition_state_unknown: {envelope.state}");
                    break;
            }
        }

        private void PublishFrame(ReachyCameraFrameDto? dto)
        {
            if (dto == null || dto.sequence <= 0L)
            {
                return;
            }
            if (dto.crop == null || dto.intrinsics == null)
            {
                state.MarkFaulted(
                    "CameraX frame metadata omitted crop or intrinsics.");
                desiredActive = false;
                RequirePlatform().Stop();
                return;
            }

            ReachyCameraFrameMetadata frame;
            try
            {
                frame = new ReachyCameraFrameMetadata(
                    checked((ulong)dto.sessionId),
                    checked((ulong)dto.sequence),
                    dto.timestampNanoseconds,
                    dto.cameraId,
                    ParseFacing(dto.facing),
                    dto.sensorOrientationDegrees,
                    dto.rotationDegrees,
                    dto.width,
                    dto.height,
                    new ReachyCameraFrameCrop(
                        dto.crop.left,
                        dto.crop.top,
                        dto.crop.right,
                        dto.crop.bottom),
                    ParsePixelFormat(dto.pixelFormat),
                    new ReachyCameraFrameIntrinsics(
                        ParseIntrinsicsSource(dto.intrinsics.source),
                        dto.intrinsics.fx,
                        dto.intrinsics.fy,
                        dto.intrinsics.cx,
                        dto.intrinsics.cy,
                        dto.intrinsics.skew,
                        dto.intrinsics.provenance));
                state.PublishFrame(frame);
            }
            catch (Exception exception)
            {
                desiredActive = false;
                RequirePlatform().Stop();
                state.MarkFaulted(
                    $"camera_frame_contract_failed: {exception.Message}");
            }
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            platform = new ReachyUnityAndroidCameraAcquisitionPlatform();
            initialized = true;
        }

        private void EnsureReady()
        {
            EnsureInitialized();
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ReachyAndroidCameraAcquisition));
            }
            if (!RequirePlatform().IsSupported)
            {
                desiredActive = false;
                state.MarkUnavailable(
                    "CameraX frame acquisition is unavailable outside an Android player.");
            }
            _ = RequireDiscovery();
        }

        private IReachyDeviceCameraAcquisitionPlatform RequirePlatform()
        {
            return platform ?? throw new InvalidOperationException(
                "The Android CameraX acquisition platform is not initialized.");
        }

        private ReachyAndroidCameraDiscovery RequireDiscovery()
        {
            return discovery ?? throw new InvalidOperationException(
                "CameraX acquisition is not bound to camera discovery.");
        }

        private static ReachyCameraCapability? SelectCamera(
            ReachyCameraCapabilitySnapshot capabilities,
            ReachyCameraFacing facing)
        {
            ReachyDeviceCameraFacing desired = facing switch
            {
                ReachyCameraFacing.Front => ReachyDeviceCameraFacing.Front,
                ReachyCameraFacing.Rear => ReachyDeviceCameraFacing.Rear,
                _ => ReachyDeviceCameraFacing.Rear,
            };
            for (int index = 0; index < capabilities.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = capabilities.Cameras[index];
                if (camera.Facing == desired &&
                    camera.Availability == ReachyCameraAvailabilityState.Available &&
                    camera.AnalysisResolutions.Count > 0)
                {
                    return camera;
                }
            }
            return null;
        }

        private static ReachyCameraCapability? FindCamera(
            ReachyCameraCapabilitySnapshot capabilities,
            string cameraId)
        {
            for (int index = 0; index < capabilities.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = capabilities.Cameras[index];
                if (string.Equals(
                        camera.CameraId,
                        cameraId,
                        StringComparison.Ordinal))
                {
                    return camera;
                }
            }
            return null;
        }

        private static bool CanRemainBoundToSelectedCamera(
            ReachyCameraAvailabilityState availability)
        {
            return availability == ReachyCameraAvailabilityState.Available ||
                availability ==
                    ReachyCameraAvailabilityState.InUseOrUnavailable;
        }

        private static ReachyDeviceCameraFacing ParseFacing(string value)
        {
            return value switch
            {
                "front" => ReachyDeviceCameraFacing.Front,
                "rear" => ReachyDeviceCameraFacing.Rear,
                "external" => ReachyDeviceCameraFacing.External,
                _ => ReachyDeviceCameraFacing.Unknown,
            };
        }

        private static ReachyCameraPixelFormat ParsePixelFormat(string value)
        {
            return value == "YUV_420_888"
                ? ReachyCameraPixelFormat.Yuv420888
                : ReachyCameraPixelFormat.Unknown;
        }

        private static ReachyCameraFrameIntrinsicsSource ParseIntrinsicsSource(
            string value)
        {
            return value == "android_calibration"
                ? ReachyCameraFrameIntrinsicsSource.AndroidCalibration
                : ReachyCameraFrameIntrinsicsSource.UncalibratedPinholeEstimate;
        }

        private static string GetFacingLabel(ReachyCameraFacing facing)
        {
            return facing switch
            {
                ReachyCameraFacing.Front => "front",
                ReachyCameraFacing.Rear => "rear",
                _ => "rear",
            };
        }

        private static string PrefixError(string code, string detail)
        {
            return string.IsNullOrWhiteSpace(code)
                ? detail
                : $"{code}: {detail}";
        }
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

    [Serializable]
    internal sealed class ReachyCameraAcquisitionEnvelope
    {
        public string status = string.Empty;
        public string state = string.Empty;
        public string errorCode = string.Empty;
        public string message = string.Empty;
        public long sessionId;
        public string cameraId = string.Empty;
        public string facing = string.Empty;
        public string analysisBackpressure = string.Empty;
        public string previewSink = string.Empty;
        public bool cpuPixelCopyPerformed;
        public ReachyCameraFrameDto? latestFrame;
    }

    [Serializable]
    internal sealed class ReachyCameraFrameDto
    {
        public long sessionId;
        public long sequence;
        public long timestampNanoseconds;
        public string cameraId = string.Empty;
        public string facing = string.Empty;
        public int sensorOrientationDegrees;
        public int rotationDegrees;
        public int width;
        public int height;
        public ReachyCameraFrameCropDto? crop;
        public string pixelFormat = string.Empty;
        public ReachyCameraFrameIntrinsicsDto? intrinsics;
        public bool imagePlanesAccessed;
        public bool cpuPixelCopyPerformed;
    }

    [Serializable]
    internal sealed class ReachyCameraFrameCropDto
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [Serializable]
    internal sealed class ReachyCameraFrameIntrinsicsDto
    {
        public string source = string.Empty;
        public float fx;
        public float fy;
        public float cx;
        public float cy;
        public float skew;
        public string coordinateSpace = string.Empty;
        public int activeArrayLeft;
        public int activeArrayTop;
        public int activeArrayRight;
        public int activeArrayBottom;
        public string provenance = string.Empty;
    }
}

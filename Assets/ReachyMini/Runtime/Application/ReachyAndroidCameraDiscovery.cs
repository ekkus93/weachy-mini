#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace ReachyMini.AppState
{
    public interface IReachyDeviceCameraPlatform : IDisposable
    {
        bool IsSupported { get; }

        bool HasCameraPermission();

        bool ShouldShowCameraPermissionRationale();

        void RequestCameraPermission(Action granted, Action denied);

        string DiscoverCameraCapabilitiesJson();

        void OpenApplicationSettings();
    }

    [DisallowMultipleComponent]
    public sealed class ReachyAndroidCameraDiscovery : MonoBehaviour
    {
        private const string EverRequestedKey =
            "reachy.camera.permission.ever_requested";
        private const string EverGrantedKey =
            "reachy.camera.permission.ever_granted";
        private const string PermanentDenialKey =
            "reachy.camera.permission.permanent_denial";
        private const string RequestCountKey =
            "reachy.camera.permission.request_count";
        private const float RefreshIntervalSeconds = 1f;

        private IReachyDeviceCameraPlatform? platform;
        private ReachyCameraCapabilityStateStore state =
            new ReachyCameraCapabilityStateStore();
        private bool initialized;
        private bool usePersistentHistory = true;
        private float nextRefreshTime;

        public ReachyCameraCapabilityStateStore State => state;

        public void ConfigurePlatformForTests(IReachyDeviceCameraPlatform testPlatform)
        {
            if (testPlatform == null)
            {
                throw new ArgumentNullException(nameof(testPlatform));
            }
            if (State.Current.Permission == ReachyCameraPermissionState.Requesting)
            {
                throw new InvalidOperationException(
                    "The camera platform cannot change while permission is being requested.");
            }

            platform?.Dispose();
            platform = testPlatform;
            usePersistentHistory = false;
            ReplaceStateStore();
            InitializePlatform();
        }

        public void RequestAccessOrRefresh()
        {
            EnsureInitialized();
            IReachyDeviceCameraPlatform currentPlatform = RequirePlatform();
            if (!currentPlatform.IsSupported)
            {
                state.MarkUnsupported(
                    "Android camera discovery is unavailable on this platform.");
                return;
            }
            if (currentPlatform.HasCameraPermission())
            {
                RecordPermissionGranted();
                DiscoverCapabilities();
                return;
            }
            if (state.Current.Permission ==
                ReachyCameraPermissionState.PermanentlyDenied)
            {
                currentPlatform.OpenApplicationSettings();
                state.MarkDenied(
                    permanent: true,
                    "Camera permission is permanently denied. Android application settings were opened so access can be restored explicitly.");
                return;
            }

            RecordPermissionRequested();
            state.MarkRequesting(
                "Waiting for the Android camera permission decision.");
            currentPlatform.RequestCameraPermission(
                OnPermissionGranted,
                OnPermissionDenied);
        }

        public void RefreshPermissionAndCapabilities()
        {
            EnsureInitialized();
            RefreshPermissionAndCapabilitiesInternal();
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (!initialized ||
                state.Current.Permission != ReachyCameraPermissionState.Granted ||
                Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            RefreshPermissionAndCapabilitiesInternal();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && initialized)
            {
                RefreshPermissionAndCapabilitiesInternal();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused && initialized)
            {
                RefreshPermissionAndCapabilitiesInternal();
            }
        }

        private void OnDestroy()
        {
            state.Changed -= OnStateChanged;
            platform?.Dispose();
            platform = null;
            initialized = false;
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            platform = new ReachyUnityAndroidCameraPlatform();
            InitializePlatform();
        }

        private void InitializePlatform()
        {
            initialized = true;
            state.Changed += OnStateChanged;
            IReachyDeviceCameraPlatform currentPlatform = RequirePlatform();
            if (!currentPlatform.IsSupported)
            {
                state.MarkUnsupported(
                    "Android camera discovery is unavailable outside an Android player.");
                return;
            }

            if (currentPlatform.HasCameraPermission())
            {
                RecordPermissionGranted();
                DiscoverCapabilities();
                return;
            }

            if (ReadHistory(EverGrantedKey) != 0)
            {
                state.MarkRevoked(
                    "Camera permission was previously granted and is now revoked. The fixed Reachy presentation remains active.");
            }
            else if (ReadHistory(PermanentDenialKey) != 0)
            {
                state.MarkDenied(
                    permanent: true,
                    "Camera permission is permanently denied. Open Android application settings to restore access.");
            }
            else if (ReadHistory(EverRequestedKey) != 0)
            {
                state.MarkDenied(
                    permanent: false,
                    "Camera permission was denied. Access will be requested again only after a user camera action.");
            }
            else
            {
                state.MarkNotRequested(
                    "Camera permission has not been requested. The app will request it only after a user camera action.");
            }
        }

        private void ReplaceStateStore()
        {
            state.Changed -= OnStateChanged;
            state = new ReachyCameraCapabilityStateStore();
            state.Changed += OnStateChanged;
        }

        private void RefreshPermissionAndCapabilitiesInternal()
        {
            IReachyDeviceCameraPlatform currentPlatform = RequirePlatform();
            if (!currentPlatform.IsSupported)
            {
                return;
            }
            if (currentPlatform.HasCameraPermission())
            {
                RecordPermissionGranted();
                DiscoverCapabilities();
                return;
            }

            if (ReadHistory(EverGrantedKey) != 0 ||
                state.Current.Permission == ReachyCameraPermissionState.Granted)
            {
                state.MarkRevoked(
                    "Camera permission was revoked. Device-camera discovery and acquisition are disabled until access is restored.");
            }
        }

        private void OnPermissionGranted()
        {
            RecordPermissionGranted();
            DiscoverCapabilities();
        }

        private void OnPermissionDenied()
        {
            IReachyDeviceCameraPlatform currentPlatform = RequirePlatform();
            int requestCount = ReadHistory(RequestCountKey);
            bool permanent =
                requestCount > 1 &&
                !currentPlatform.ShouldShowCameraPermissionRationale();
            WriteHistory(PermanentDenialKey, permanent ? 1 : 0);
            state.MarkDenied(
                permanent,
                permanent
                    ? "Camera permission is permanently denied. Use Android application settings to restore access."
                    : "Camera permission was denied. The app will not request it again until another user camera action.");
        }

        private void DiscoverCapabilities()
        {
            try
            {
                string json = RequirePlatform().DiscoverCameraCapabilitiesJson();
                ReachyCameraDiscoveryEnvelope? envelope =
                    JsonUtility.FromJson<ReachyCameraDiscoveryEnvelope>(json);
                if (envelope == null)
                {
                    throw new InvalidOperationException(
                        "Android camera discovery returned no JSON object.");
                }
                if (!string.Equals(envelope.status, "ok", StringComparison.Ordinal))
                {
                    string code = string.IsNullOrWhiteSpace(envelope.errorCode)
                        ? "camera_discovery_error"
                        : envelope.errorCode;
                    string message = string.IsNullOrWhiteSpace(envelope.message)
                        ? "Android camera discovery failed without diagnostics."
                        : envelope.message;
                    state.MarkFaulted($"{code}: {message}");
                    return;
                }

                ReachyCameraDiscoveryCamera[] cameraDtos =
                    envelope.cameras ?? Array.Empty<ReachyCameraDiscoveryCamera>();
                var cameras = new List<ReachyCameraCapability>(cameraDtos.Length);
                for (int index = 0; index < cameraDtos.Length; ++index)
                {
                    cameras.Add(ConvertCamera(cameraDtos[index]));
                }
                string detail = string.IsNullOrWhiteSpace(envelope.message)
                    ? "Android camera capability discovery completed."
                    : envelope.message;
                state.ApplyDiscovery(cameras, detail);
                nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            }
            catch (Exception exception)
            {
                state.MarkFaulted(
                    $"camera_discovery_exception: {exception.Message}");
            }
        }

        private static ReachyCameraCapability ConvertCamera(
            ReachyCameraDiscoveryCamera dto)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }
            ReachyCameraDiscoveryResolution[] resolutionDtos =
                dto.analysisResolutions ??
                Array.Empty<ReachyCameraDiscoveryResolution>();
            var resolutions = new List<ReachyCameraResolution>(
                resolutionDtos.Length);
            for (int index = 0; index < resolutionDtos.Length; ++index)
            {
                ReachyCameraDiscoveryResolution resolution = resolutionDtos[index];
                resolutions.Add(
                    new ReachyCameraResolution(
                        resolution.width,
                        resolution.height));
            }

            ReachyCameraDiscoveryIntrinsics? intrinsicDto = dto.intrinsics;
            string fallback = string.IsNullOrWhiteSpace(dto.calibrationFallback)
                ? "Persist a versioned checkerboard calibration. Until then, use an explicitly uncalibrated pinhole estimate derived from the active array and selected analysis resolution."
                : dto.calibrationFallback;
            ReachyCameraIntrinsics intrinsics =
                intrinsicDto != null && intrinsicDto.available
                    ? new ReachyCameraIntrinsics(
                        ReachyCameraIntrinsicsSource.AndroidCalibration,
                        intrinsicDto.fx,
                        intrinsicDto.fy,
                        intrinsicDto.cx,
                        intrinsicDto.cy,
                        intrinsicDto.skew,
                        fallback)
                    : ReachyCameraIntrinsics.CreateUnavailable(fallback);

            return new ReachyCameraCapability(
                string.IsNullOrWhiteSpace(dto.id) ? "unknown" : dto.id,
                ParseFacing(dto.facing),
                dto.sensorOrientationDegrees,
                string.IsNullOrWhiteSpace(dto.hardwareLevel)
                    ? "unknown"
                    : dto.hardwareLevel,
                ParseAvailability(dto.availability),
                resolutions,
                intrinsics,
                dto.activeArrayWidth,
                dto.activeArrayHeight);
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

        private static ReachyCameraAvailabilityState ParseAvailability(
            string value)
        {
            return value switch
            {
                "available" => ReachyCameraAvailabilityState.Available,
                "in_use_or_unavailable" =>
                    ReachyCameraAvailabilityState.InUseOrUnavailable,
                "disabled" => ReachyCameraAvailabilityState.Disabled,
                "disconnected" => ReachyCameraAvailabilityState.Disconnected,
                _ => ReachyCameraAvailabilityState.Unknown,
            };
        }

        private void RecordPermissionRequested()
        {
            WriteHistory(EverRequestedKey, 1);
            WriteHistory(
                RequestCountKey,
                checked(ReadHistory(RequestCountKey) + 1));
        }

        private void RecordPermissionGranted()
        {
            WriteHistory(EverRequestedKey, 1);
            WriteHistory(EverGrantedKey, 1);
            WriteHistory(PermanentDenialKey, 0);
        }

        private int ReadHistory(string key)
        {
            return usePersistentHistory ? PlayerPrefs.GetInt(key, 0) : 0;
        }

        private void WriteHistory(string key, int value)
        {
            if (!usePersistentHistory)
            {
                return;
            }
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        private IReachyDeviceCameraPlatform RequirePlatform()
        {
            return platform ?? throw new InvalidOperationException(
                "The Android camera platform is not initialized.");
        }

        private void OnStateChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            Debug.Log(
                "RMA090_CAMERA_CAPABILITIES " +
                eventArgs.Snapshot.Summary);
        }
    }

    internal sealed class ReachyUnityAndroidCameraPlatform :
        IReachyDeviceCameraPlatform
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BridgeClassName =
            "com.ekkus93.weachy.camera.ReachyCameraDiscoveryBridge";
        private PermissionCallbacks? callbacks;
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

        public bool HasCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
            return false;
#endif
        }

        public bool ShouldShowCameraPermissionRationale()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<bool>(
                "shouldShowCameraPermissionRationale",
                activity);
#else
            return false;
#endif
        }

        public void RequestCameraPermission(Action granted, Action denied)
        {
            if (granted == null)
            {
                throw new ArgumentNullException(nameof(granted));
            }
            if (denied == null)
            {
                throw new ArgumentNullException(nameof(denied));
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                callbacks = null;
                granted();
            };
            callbacks.PermissionDenied += _ =>
            {
                callbacks = null;
                denied();
            };
            Permission.RequestUserPermission(Permission.Camera, callbacks);
#else
            denied();
#endif
        }

        public string DiscoverCameraCapabilitiesJson()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            return bridge.CallStatic<string>(
                "discover",
                activity) ?? throw new InvalidOperationException(
                    "The Android camera bridge returned a null result.");
#else
            throw new PlatformNotSupportedException(
                "Android camera discovery requires an Android player.");
#endif
        }

        public void OpenApplicationSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            bridge.CallStatic("openApplicationSettings", activity);
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
            callbacks = null;
            using AndroidJavaObject activity = GetCurrentActivity();
            using var bridge = new AndroidJavaClass(BridgeClassName);
            bridge.CallStatic("shutdown", activity);
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
                throw new ObjectDisposedException(nameof(ReachyUnityAndroidCameraPlatform));
            }
        }
#endif
    }

    [Serializable]
    internal sealed class ReachyCameraDiscoveryEnvelope
    {
        public string status = string.Empty;
        public string errorCode = string.Empty;
        public string message = string.Empty;
        public ReachyCameraDiscoveryCamera[]? cameras;
    }

    [Serializable]
    internal sealed class ReachyCameraDiscoveryCamera
    {
        public string id = string.Empty;
        public string facing = string.Empty;
        public int sensorOrientationDegrees;
        public string hardwareLevel = string.Empty;
        public string availability = string.Empty;
        public ReachyCameraDiscoveryResolution[]? analysisResolutions;
        public ReachyCameraDiscoveryIntrinsics? intrinsics;
        public int activeArrayWidth;
        public int activeArrayHeight;
        public string calibrationFallback = string.Empty;
    }

    [Serializable]
    internal sealed class ReachyCameraDiscoveryResolution
    {
        public int width;
        public int height;
    }

    [Serializable]
    internal sealed class ReachyCameraDiscoveryIntrinsics
    {
        public bool available;
        public float fx;
        public float fy;
        public float cx;
        public float cy;
        public float skew;
    }
}

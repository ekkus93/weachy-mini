#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.AppState
{
    public enum ReachyCameraPermissionState
    {
        NotRequested = 0,
        Requesting = 1,
        Granted = 2,
        Denied = 3,
        PermanentlyDenied = 4,
        Revoked = 5,
        Unsupported = 6,
        Faulted = 7,
    }

    public enum ReachyDeviceCameraFacing
    {
        Unknown = 0,
        Front = 1,
        Rear = 2,
        External = 3,
    }

    public enum ReachyCameraAvailabilityState
    {
        Unknown = 0,
        Available = 1,
        InUseOrUnavailable = 2,
        Disabled = 3,
        Disconnected = 4,
    }

    public enum ReachyCameraIntrinsicsSource
    {
        AndroidCalibration = 0,
        CalibrationFallbackRequired = 1,
    }

    public readonly struct ReachyCameraResolution :
        IEquatable<ReachyCameraResolution>
    {
        public ReachyCameraResolution(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    width,
                    "A camera resolution width must be positive.");
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(height),
                    height,
                    "A camera resolution height must be positive.");
            }

            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public long PixelCount => checked((long)Width * Height);

        public bool Equals(ReachyCameraResolution other)
        {
            return Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object? obj)
        {
            return obj is ReachyCameraResolution other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height);
        }

        public override string ToString()
        {
            return $"{Width}x{Height}";
        }
    }

    public sealed class ReachyCameraIntrinsics
    {
        public ReachyCameraIntrinsics(
            ReachyCameraIntrinsicsSource source,
            float focalLengthX,
            float focalLengthY,
            float principalPointX,
            float principalPointY,
            float skew,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(fallback))
            {
                throw new ArgumentException(
                    "Camera intrinsics require a calibration fallback description.",
                    nameof(fallback));
            }
            if (source == ReachyCameraIntrinsicsSource.AndroidCalibration)
            {
                RequireFinitePositive(focalLengthX, nameof(focalLengthX));
                RequireFinitePositive(focalLengthY, nameof(focalLengthY));
                RequireFinite(principalPointX, nameof(principalPointX));
                RequireFinite(principalPointY, nameof(principalPointY));
                RequireFinite(skew, nameof(skew));
            }

            Source = source;
            FocalLengthX = focalLengthX;
            FocalLengthY = focalLengthY;
            PrincipalPointX = principalPointX;
            PrincipalPointY = principalPointY;
            Skew = skew;
            Fallback = fallback;
        }

        public ReachyCameraIntrinsicsSource Source { get; }

        public bool Available =>
            Source == ReachyCameraIntrinsicsSource.AndroidCalibration;

        public float FocalLengthX { get; }

        public float FocalLengthY { get; }

        public float PrincipalPointX { get; }

        public float PrincipalPointY { get; }

        public float Skew { get; }

        public string Fallback { get; }

        public static ReachyCameraIntrinsics CreateUnavailable(string fallback)
        {
            return new ReachyCameraIntrinsics(
                ReachyCameraIntrinsicsSource.CalibrationFallbackRequired,
                0f,
                0f,
                0f,
                0f,
                0f,
                fallback);
        }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera intrinsic values must be finite.");
            }
        }

        private static void RequireFinitePositive(float value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "Camera focal lengths must be positive.");
            }
        }
    }

    public sealed class ReachyCameraCapability
    {
        private readonly ReachyCameraResolution[] analysisResolutions;

        public ReachyCameraCapability(
            string cameraId,
            ReachyDeviceCameraFacing facing,
            int sensorOrientationDegrees,
            string hardwareLevel,
            ReachyCameraAvailabilityState availability,
            IReadOnlyList<ReachyCameraResolution> supportedAnalysisResolutions,
            ReachyCameraIntrinsics intrinsics,
            int activeArrayWidth,
            int activeArrayHeight)
        {
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new ArgumentException(
                    "A camera capability requires a stable camera identifier.",
                    nameof(cameraId));
            }
            if (sensorOrientationDegrees != 0 &&
                sensorOrientationDegrees != 90 &&
                sensorOrientationDegrees != 180 &&
                sensorOrientationDegrees != 270)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sensorOrientationDegrees),
                    sensorOrientationDegrees,
                    "Sensor orientation must be 0, 90, 180, or 270 degrees.");
            }
            if (string.IsNullOrWhiteSpace(hardwareLevel))
            {
                throw new ArgumentException(
                    "A camera capability requires a hardware-level label.",
                    nameof(hardwareLevel));
            }
            if (supportedAnalysisResolutions == null)
            {
                throw new ArgumentNullException(nameof(supportedAnalysisResolutions));
            }
            if ((activeArrayWidth == 0) != (activeArrayHeight == 0) ||
                activeArrayWidth < 0 ||
                activeArrayHeight < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeArrayWidth),
                    "The active-array dimensions must both be positive or both be zero.");
            }

            analysisResolutions =
                new ReachyCameraResolution[supportedAnalysisResolutions.Count];
            var uniqueResolutions = new HashSet<ReachyCameraResolution>();
            for (int index = 0; index < supportedAnalysisResolutions.Count; ++index)
            {
                ReachyCameraResolution resolution =
                    supportedAnalysisResolutions[index];
                if (!uniqueResolutions.Add(resolution))
                {
                    throw new ArgumentException(
                        $"Camera '{cameraId}' repeats analysis resolution {resolution}.",
                        nameof(supportedAnalysisResolutions));
                }
                analysisResolutions[index] = resolution;
            }

            CameraId = cameraId;
            Facing = facing;
            SensorOrientationDegrees = sensorOrientationDegrees;
            HardwareLevel = hardwareLevel;
            Availability = availability;
            Intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
            ActiveArrayWidth = activeArrayWidth;
            ActiveArrayHeight = activeArrayHeight;
        }

        public string CameraId { get; }

        public ReachyDeviceCameraFacing Facing { get; }

        public int SensorOrientationDegrees { get; }

        public string HardwareLevel { get; }

        public ReachyCameraAvailabilityState Availability { get; }

        public IReadOnlyList<ReachyCameraResolution> AnalysisResolutions =>
            Array.AsReadOnly(analysisResolutions);

        public ReachyCameraIntrinsics Intrinsics { get; }

        public int ActiveArrayWidth { get; }

        public int ActiveArrayHeight { get; }

        public bool IsSelectable =>
            Facing == ReachyDeviceCameraFacing.Front ||
            Facing == ReachyDeviceCameraFacing.Rear;
    }

    public sealed class ReachyCameraCapabilitySnapshot
    {
        private readonly ReachyCameraCapability[] cameras;

        public ReachyCameraCapabilitySnapshot(
            ReachyCameraPermissionState permission,
            string message,
            int permissionRequestCount,
            IReadOnlyList<ReachyCameraCapability> discoveredCameras,
            ulong revision)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException(
                    "Camera capability state requires diagnostics.",
                    nameof(message));
            }
            if (permissionRequestCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(permissionRequestCount),
                    permissionRequestCount,
                    "Permission request count cannot be negative.");
            }
            if (discoveredCameras == null)
            {
                throw new ArgumentNullException(nameof(discoveredCameras));
            }

            cameras = new ReachyCameraCapability[discoveredCameras.Count];
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            int frontCount = 0;
            int rearCount = 0;
            int availableCount = 0;
            int calibratedCount = 0;
            for (int index = 0; index < discoveredCameras.Count; ++index)
            {
                ReachyCameraCapability camera = discoveredCameras[index] ??
                    throw new ArgumentException(
                        "Camera capability state cannot contain a null camera.",
                        nameof(discoveredCameras));
                if (!identifiers.Add(camera.CameraId))
                {
                    throw new ArgumentException(
                        $"Camera identifier '{camera.CameraId}' is duplicated.",
                        nameof(discoveredCameras));
                }
                cameras[index] = camera;
                if (camera.Facing == ReachyDeviceCameraFacing.Front)
                {
                    ++frontCount;
                }
                else if (camera.Facing == ReachyDeviceCameraFacing.Rear)
                {
                    ++rearCount;
                }
                if (camera.Availability == ReachyCameraAvailabilityState.Available)
                {
                    ++availableCount;
                }
                if (camera.Intrinsics.Available)
                {
                    ++calibratedCount;
                }
            }

            Permission = permission;
            Message = message;
            PermissionRequestCount = permissionRequestCount;
            Revision = revision;
            FrontCameraCount = frontCount;
            RearCameraCount = rearCount;
            AvailableCameraCount = availableCount;
            CalibratedCameraCount = calibratedCount;
        }

        public ReachyCameraPermissionState Permission { get; }

        public string Message { get; }

        public int PermissionRequestCount { get; }

        public IReadOnlyList<ReachyCameraCapability> Cameras =>
            Array.AsReadOnly(cameras);

        public ulong Revision { get; }

        public int FrontCameraCount { get; }

        public int RearCameraCount { get; }

        public int AvailableCameraCount { get; }

        public int CalibratedCameraCount { get; }

        public bool SelectionAvailable =>
            Permission == ReachyCameraPermissionState.Granted &&
            FrontCameraCount + RearCameraCount > 0;

        public bool AnyCameraAvailable => AvailableCameraCount > 0;

        public bool RequiresCalibrationFallback =>
            Cameras.Count > CalibratedCameraCount;

        public string Summary =>
            $"permission={Permission}; cameras={Cameras.Count}; " +
            $"front={FrontCameraCount}; rear={RearCameraCount}; " +
            $"available={AvailableCameraCount}; intrinsics={CalibratedCameraCount}/{Cameras.Count}; " +
            $"detail={Message}";
    }

    public sealed class ReachyCameraCapabilityChangedEventArgs : EventArgs
    {
        public ReachyCameraCapabilityChangedEventArgs(
            ReachyCameraCapabilitySnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ReachyCameraCapabilitySnapshot Snapshot { get; }
    }

    public sealed class ReachyCameraCapabilityStateStore
    {
        private ReachyCameraCapabilitySnapshot current =
            new ReachyCameraCapabilitySnapshot(
                ReachyCameraPermissionState.NotRequested,
                "Camera permission has not been requested.",
                0,
                Array.Empty<ReachyCameraCapability>(),
                0UL);

        public ReachyCameraCapabilitySnapshot Current => current;

        public event EventHandler<ReachyCameraCapabilityChangedEventArgs>? Changed;

        public void MarkNotRequested(string message)
        {
            Publish(
                ReachyCameraPermissionState.NotRequested,
                message,
                current.PermissionRequestCount,
                Array.Empty<ReachyCameraCapability>());
        }

        public void MarkRequesting(string message)
        {
            Publish(
                ReachyCameraPermissionState.Requesting,
                message,
                checked(current.PermissionRequestCount + 1),
                Array.Empty<ReachyCameraCapability>());
        }

        public void MarkDenied(bool permanent, string message)
        {
            Publish(
                permanent
                    ? ReachyCameraPermissionState.PermanentlyDenied
                    : ReachyCameraPermissionState.Denied,
                message,
                current.PermissionRequestCount,
                Array.Empty<ReachyCameraCapability>());
        }

        public void ApplyDiscovery(
            IReadOnlyList<ReachyCameraCapability> cameras,
            string message)
        {
            Publish(
                ReachyCameraPermissionState.Granted,
                message,
                current.PermissionRequestCount,
                cameras ?? throw new ArgumentNullException(nameof(cameras)));
        }

        public void MarkRevoked(string message)
        {
            Publish(
                ReachyCameraPermissionState.Revoked,
                message,
                current.PermissionRequestCount,
                Array.Empty<ReachyCameraCapability>());
        }

        public void MarkUnsupported(string message)
        {
            Publish(
                ReachyCameraPermissionState.Unsupported,
                message,
                current.PermissionRequestCount,
                Array.Empty<ReachyCameraCapability>());
        }

        public void MarkFaulted(string message)
        {
            Publish(
                ReachyCameraPermissionState.Faulted,
                message,
                current.PermissionRequestCount,
                Array.Empty<ReachyCameraCapability>());
        }

        private void Publish(
            ReachyCameraPermissionState permission,
            string message,
            int requestCount,
            IReadOnlyList<ReachyCameraCapability> cameras)
        {
            ReachyCameraCapabilitySnapshot next =
                new ReachyCameraCapabilitySnapshot(
                    permission,
                    message,
                    requestCount,
                    cameras,
                    checked(current.Revision + 1UL));
            current = next;
            Changed?.Invoke(
                this,
                new ReachyCameraCapabilityChangedEventArgs(next));
        }
    }
}

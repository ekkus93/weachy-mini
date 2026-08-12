#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraAcquisition
    {
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
}

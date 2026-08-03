#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed class ReachyCameraDiscoveryEvidence : MonoBehaviour
    {
        public const string ResultFileName =
            "rma090-camera-discovery-state.json";

        private ReachyAndroidCameraDiscovery? discovery;

        public string ResultPath => Path.Combine(
            Application.persistentDataPath,
            ResultFileName);

        public void Configure(ReachyAndroidCameraDiscovery cameraDiscovery)
        {
            if (cameraDiscovery == null)
            {
                throw new ArgumentNullException(nameof(cameraDiscovery));
            }
            if (discovery != null)
            {
                throw new InvalidOperationException(
                    "Camera discovery evidence is already configured.");
            }

            discovery = cameraDiscovery;
            discovery.State.Changed += OnCameraCapabilitiesChanged;
            Publish(discovery.State.Current);
        }

        public static string BuildJson(
            ReachyCameraCapabilitySnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var cameras = new CameraEvidence[snapshot.Cameras.Count];
            for (int index = 0; index < snapshot.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = snapshot.Cameras[index];
                var resolutions =
                    new ResolutionEvidence[camera.AnalysisResolutions.Count];
                for (int resolutionIndex = 0;
                    resolutionIndex < camera.AnalysisResolutions.Count;
                    ++resolutionIndex)
                {
                    ReachyCameraResolution resolution =
                        camera.AnalysisResolutions[resolutionIndex];
                    resolutions[resolutionIndex] = new ResolutionEvidence
                    {
                        width = resolution.Width,
                        height = resolution.Height,
                    };
                }

                cameras[index] = new CameraEvidence
                {
                    id = camera.CameraId,
                    facing = camera.Facing.ToString(),
                    sensor_orientation_degrees =
                        camera.SensorOrientationDegrees,
                    hardware_level = camera.HardwareLevel,
                    availability = camera.Availability.ToString(),
                    analysis_resolution_count =
                        camera.AnalysisResolutions.Count,
                    largest_analysis_resolution =
                        camera.AnalysisResolutions.Count == 0
                            ? "none"
                            : camera.AnalysisResolutions[0].ToString(),
                    analysis_resolutions = resolutions,
                    active_array_width = camera.ActiveArrayWidth,
                    active_array_height = camera.ActiveArrayHeight,
                    intrinsics_source = camera.Intrinsics.Source.ToString(),
                    intrinsics_available = camera.Intrinsics.Available,
                    focal_length_x = camera.Intrinsics.FocalLengthX,
                    focal_length_y = camera.Intrinsics.FocalLengthY,
                    principal_point_x = camera.Intrinsics.PrincipalPointX,
                    principal_point_y = camera.Intrinsics.PrincipalPointY,
                    skew = camera.Intrinsics.Skew,
                    calibration_fallback = camera.Intrinsics.Fallback,
                };
            }

            var report = new CameraDiscoveryEvidenceReport
            {
                status = "ok",
                permission = snapshot.Permission.ToString(),
                message = snapshot.Message,
                permission_request_count = snapshot.PermissionRequestCount,
                revision = snapshot.Revision.ToString(),
                camera_count = snapshot.Cameras.Count,
                front_count = snapshot.FrontCameraCount,
                rear_count = snapshot.RearCameraCount,
                available_count = snapshot.AvailableCameraCount,
                calibrated_count = snapshot.CalibratedCameraCount,
                selection_available = snapshot.SelectionAvailable,
                any_camera_available = snapshot.AnyCameraAvailable,
                requires_calibration_fallback =
                    snapshot.RequiresCalibrationFallback,
                cameras = cameras,
            };
            return JsonUtility.ToJson(report, prettyPrint: true);
        }

        private void OnDestroy()
        {
            if (discovery != null)
            {
                discovery.State.Changed -= OnCameraCapabilitiesChanged;
                discovery = null;
            }
        }

        private void OnCameraCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            Publish(eventArgs.Snapshot);
        }

        private void Publish(ReachyCameraCapabilitySnapshot snapshot)
        {
            try
            {
                string directory = Application.persistentDataPath;
                Directory.CreateDirectory(directory);
                string resultPath = Path.Combine(directory, ResultFileName);
                string temporaryPath = resultPath + ".tmp";
                File.WriteAllText(temporaryPath, BuildJson(snapshot));
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }
                File.Move(temporaryPath, resultPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not publish RMA-090 camera discovery evidence: " +
                    exception.Message,
                    this);
            }
        }

        [Serializable]
        private sealed class CameraDiscoveryEvidenceReport
        {
            public string status = string.Empty;
            public string permission = string.Empty;
            public string message = string.Empty;
            public int permission_request_count;
            public string revision = string.Empty;
            public int camera_count;
            public int front_count;
            public int rear_count;
            public int available_count;
            public int calibrated_count;
            public bool selection_available;
            public bool any_camera_available;
            public bool requires_calibration_fallback;
            public CameraEvidence[] cameras = Array.Empty<CameraEvidence>();
        }

        [Serializable]
        private sealed class CameraEvidence
        {
            public string id = string.Empty;
            public string facing = string.Empty;
            public int sensor_orientation_degrees;
            public string hardware_level = string.Empty;
            public string availability = string.Empty;
            public int analysis_resolution_count;
            public string largest_analysis_resolution = string.Empty;
            public ResolutionEvidence[] analysis_resolutions =
                Array.Empty<ResolutionEvidence>();
            public int active_array_width;
            public int active_array_height;
            public string intrinsics_source = string.Empty;
            public bool intrinsics_available;
            public float focal_length_x;
            public float focal_length_y;
            public float principal_point_x;
            public float principal_point_y;
            public float skew;
            public string calibration_fallback = string.Empty;
        }

        [Serializable]
        private sealed class ResolutionEvidence
        {
            public int width;
            public int height;
        }
    }
}

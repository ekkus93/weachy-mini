#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed class ReachyCameraAcquisitionAcceptanceRetry : MonoBehaviour
    {
        private const float RetryIntervalSeconds = 0.25f;

        private ReachyAndroidCameraAcquisition? acquisition;
        private ReachyAndroidCameraDiscovery? discovery;
        private float nextRetryTime;

        public void Configure(
            ReachyAndroidCameraAcquisition cameraAcquisition,
            ReachyAndroidCameraDiscovery cameraDiscovery)
        {
            acquisition = cameraAcquisition ??
                throw new ArgumentNullException(nameof(cameraAcquisition));
            discovery = cameraDiscovery ??
                throw new ArgumentNullException(nameof(cameraDiscovery));
        }

        private void Update()
        {
            if (acquisition == null || discovery == null ||
                Time.unscaledTime < nextRetryTime ||
                acquisition.State.Current.State !=
                    ReachyCameraAcquisitionState.Unavailable ||
                discovery.State.Current.Permission !=
                    ReachyCameraPermissionState.Granted)
            {
                return;
            }

            nextRetryTime = Time.unscaledTime + RetryIntervalSeconds;
            CameraAcceptanceCommand? command = ReadCommand();
            if (command == null ||
                string.IsNullOrWhiteSpace(command.id) ||
                !string.Equals(
                    command.action,
                    "start",
                    StringComparison.Ordinal))
            {
                return;
            }

            ReachyCameraFacing facing = string.Equals(
                    command.facing,
                    "front",
                    StringComparison.OrdinalIgnoreCase)
                ? ReachyCameraFacing.Front
                : ReachyCameraFacing.Rear;
            if (!HasAvailableCamera(discovery.State.Current, facing))
            {
                discovery.RefreshPermissionAndCapabilities();
                return;
            }

            acquisition.StartPreferred(facing);
        }

        private CameraAcceptanceCommand? ReadCommand()
        {
            string path = Path.Combine(
                Application.persistentDataPath,
                ReachyCameraAcquisitionEvidence.CommandFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<CameraAcceptanceCommand>(
                    File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not read the opt-in RMA-091 retry command: " +
                    exception.Message,
                    this);
                return null;
            }
        }

        private static bool HasAvailableCamera(
            ReachyCameraCapabilitySnapshot snapshot,
            ReachyCameraFacing facing)
        {
            ReachyDeviceCameraFacing desired = facing == ReachyCameraFacing.Front
                ? ReachyDeviceCameraFacing.Front
                : ReachyDeviceCameraFacing.Rear;
            for (int index = 0; index < snapshot.Cameras.Count; ++index)
            {
                ReachyCameraCapability camera = snapshot.Cameras[index];
                if (camera.Facing == desired &&
                    camera.Availability ==
                        ReachyCameraAvailabilityState.Available &&
                    camera.AnalysisResolutions.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        [Serializable]
        private sealed class CameraAcceptanceCommand
        {
            public string id = string.Empty;
            public string action = string.Empty;
            public string facing = string.Empty;
        }
    }
}

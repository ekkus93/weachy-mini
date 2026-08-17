#nullable enable

using UnityEngine;

namespace ReachyMini.AppState
{
    // RMA-195 phase A: wires the main screen's camera-preview action to the real
    // RMA-091 CameraX acquisition/texture-bridge pipeline instead of the stale
    // "begins in RMA-091" refusal. That pipeline is already auto-instantiated in
    // the live app by ReachyCameraAcquisitionBootstrap (a second bootstrap,
    // independent of the one that builds this screen), so it is located lazily
    // here rather than threaded through Bind() -- by the time a user can actually
    // tap this button, both AfterSceneLoad bootstraps have long since run, so the
    // lookup is not racing app startup. Unlike the microphone, no fresh Android
    // permission dialog appears here: CameraX binding itself pops nothing once
    // camera permission is already Granted, so this still respects RMA-090's
    // contract that the app never proactively surfaces a permission dialog --
    // the dialog for camera access was already requested and resolved by the
    // explicit "REQUEST CAMERA ACCESS" action above this one in the settings
    // panel, not by this button.
    public sealed partial class ReachyMainScreen
    {
        private ReachyAndroidCameraAcquisition? cameraAcquisition;
        private ReachyAndroidCameraTextureBridge? cameraPreviewBridge;

        public bool CameraPreviewActive =>
            cameraAcquisition != null && cameraAcquisition.State.Current.IsActive;

        public Texture? CameraPreviewTexture =>
            cameraPreviewBridge != null && cameraPreviewBridge.Current.HasTexture
                ? cameraPreviewBridge.PreviewTexture
                : null;

        public void RequestCameraPreview()
        {
            ReachyMainScreenStateStore store = RequireStore();
            ReachyCameraCapabilitySnapshot camera = RequireCameraCapabilities();
            if (camera.Permission != ReachyCameraPermissionState.Granted)
            {
                store.ReportUnavailableAction(
                    "Camera preview",
                    "camera permission has not been granted yet -- use REQUEST CAMERA ACCESS first");
                return;
            }

            ReachyAndroidCameraAcquisition? acquisition = LocateCameraAcquisition();
            if (acquisition == null)
            {
                store.ReportUnavailableAction(
                    "Camera preview",
                    "the camera acquisition pipeline is not installed on this device");
                return;
            }

            ReachyCameraFacing facing = RequireSettings().Current.PreferredCameraFacing;
            if (facing == ReachyCameraFacing.Unconfigured)
            {
                facing = ReachyCameraFacing.Rear;
            }
            acquisition.Toggle(facing);
        }

        private ReachyAndroidCameraAcquisition? LocateCameraAcquisition()
        {
            if (cameraAcquisition != null)
            {
                return cameraAcquisition;
            }

            ReachyAndroidCameraAcquisition? located =
                UnityEngine.Object.FindAnyObjectByType<
                    ReachyAndroidCameraAcquisition>();
            if (located == null)
            {
                return null;
            }

            cameraAcquisition = located;
            cameraPreviewBridge =
                located.GetComponent<ReachyAndroidCameraTextureBridge>();
            return cameraAcquisition;
        }

        private void ReleaseCameraPreview()
        {
            cameraAcquisition = null;
            cameraPreviewBridge = null;
        }
    }
}

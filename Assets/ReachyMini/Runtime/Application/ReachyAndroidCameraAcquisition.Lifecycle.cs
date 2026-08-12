#nullable enable

using UnityEngine;

namespace ReachyMini.AppState
{
    public sealed partial class ReachyAndroidCameraAcquisition
    {
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
            pendingStartAfterStop = false;
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
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }
    }
}

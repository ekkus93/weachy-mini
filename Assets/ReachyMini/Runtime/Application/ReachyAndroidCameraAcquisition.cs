#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.AppState
{
    [DisallowMultipleComponent]
    public sealed partial class ReachyAndroidCameraAcquisition : MonoBehaviour
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
        private bool pendingStartAfterStop;

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
            if (state.Current.IsActive)
            {
                desiredActive = true;
                pendingStartAfterStop = true;
                if (state.Current.State !=
                    ReachyCameraAcquisitionState.Stopping)
                {
                    StopPlatformForSwitch();
                }
                return;
            }

            ReachyCameraCapabilitySnapshot capabilities =
                RequireDiscovery().State.Current;
            if (capabilities.Permission != ReachyCameraPermissionState.Granted)
            {
                desiredActive = false;
                pendingStartAfterStop = false;
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
                pendingStartAfterStop = false;
                state.MarkUnavailable(
                    $"No available {GetFacingLabel(facing)} camera exposes a YUV analysis resolution.");
                return;
            }

            ReachyCameraResolution resolution =
                selected.AnalysisResolutions[0];
            ulong session = nextSessionId;
            nextSessionId = checked(nextSessionId + 1UL);
            desiredActive = true;
            pendingStartAfterStop = false;
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
            pendingStartAfterStop = false;
            if (state.Current.IsActive &&
                state.Current.State != ReachyCameraAcquisitionState.Stopping)
            {
                state.BeginStop(
                    "Stopping CameraX Preview and ImageAnalysis.");
            }
            ApplyPlatformSnapshot(RequirePlatform().Stop());
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        }

        public void RefreshNow()
        {
            EnsureReady();
            ApplyPlatformSnapshot(RequirePlatform().Snapshot());
        }

        public IReachyCameraTextureFrameLease? AcquireLatestTextureFrame(
            ulong afterSequence)
        {
            EnsureReady();
            ReachyCameraAcquisitionSnapshot snapshot = state.Current;
            if (snapshot.State != ReachyCameraAcquisitionState.Running ||
                snapshot.SessionId == 0UL)
            {
                return null;
            }
            return RequirePlatform().AcquireLatestTextureFrame(
                checked((long)snapshot.SessionId),
                checked((long)afterSequence));
        }
    }
}

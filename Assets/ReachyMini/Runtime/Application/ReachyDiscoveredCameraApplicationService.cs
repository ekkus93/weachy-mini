#nullable enable

using System;
using ReachyMini.Presentation;
using UnityEngine;

namespace ReachyMini.AppState
{
    internal sealed class ReachyDiscoveredCameraApplicationService :
        ReachyApplicationServiceBase,
        IReachyCameraService
    {
        private readonly Camera presentationCamera;
        private readonly ReachyAndroidCameraDiscovery discovery;

        public ReachyDiscoveredCameraApplicationService(
            Camera presentationCamera,
            ReachyAndroidCameraDiscovery discovery)
            : base(
                "camera-capability-discovery",
                ReachyServiceKind.Camera,
                ReachyServiceCriticality.Optional)
        {
            this.presentationCamera = presentationCamera ??
                throw new ArgumentNullException(nameof(presentationCamera));
            this.discovery = discovery ??
                throw new ArgumentNullException(nameof(discovery));
        }

        public ReachyCameraCapabilityStateStore Capabilities => discovery.State;

        protected override void OnInitialize()
        {
            ValidatePresentationCamera();
            discovery.State.Changed += OnCapabilitiesChanged;
            PublishCapabilityHealth(discovery.State.Current);
        }

        protected override void OnDispose()
        {
            discovery.State.Changed -= OnCapabilitiesChanged;
        }

        private void ValidatePresentationCamera()
        {
            ReachyPresentationCamera metadata =
                presentationCamera.GetComponent<ReachyPresentationCamera>();
            if (metadata == null ||
                !string.Equals(
                    metadata.Framing,
                    "fixed_front_three_quarter",
                    StringComparison.Ordinal) ||
                metadata.AcceptsUserNavigation)
            {
                throw new InvalidOperationException(
                    "The application camera boundary requires the fixed non-navigable Reachy presentation camera.");
            }
            if (!presentationCamera.CompareTag("MainCamera") ||
                !presentationCamera.enabled)
            {
                throw new InvalidOperationException(
                    "The fixed Reachy presentation camera is not the active main camera.");
            }
        }

        private void OnCapabilitiesChanged(
            object? sender,
            ReachyCameraCapabilityChangedEventArgs eventArgs)
        {
            PublishCapabilityHealth(eventArgs.Snapshot);
        }

        private void PublishCapabilityHealth(
            ReachyCameraCapabilitySnapshot snapshot)
        {
            string prefix =
                "Fixed front / three-quarter presentation camera is active. ";
            switch (snapshot.Permission)
            {
                case ReachyCameraPermissionState.NotRequested:
                case ReachyCameraPermissionState.Requesting:
                case ReachyCameraPermissionState.Denied:
                case ReachyCameraPermissionState.PermanentlyDenied:
                case ReachyCameraPermissionState.Revoked:
                case ReachyCameraPermissionState.Unsupported:
                    SetDegraded(prefix + snapshot.Message);
                    break;
                case ReachyCameraPermissionState.Granted:
                    SetDegraded(
                        prefix +
                        $"Android capability discovery found {snapshot.Cameras.Count} camera(s), " +
                        $"including {snapshot.FrontCameraCount} front and {snapshot.RearCameraCount} rear. " +
                        "Frame acquisition remains unavailable until RMA-091.");
                    break;
                case ReachyCameraPermissionState.Faulted:
                    SetDegraded(prefix + snapshot.Message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(snapshot.Permission),
                        snapshot.Permission,
                        null);
            }
        }
    }
}

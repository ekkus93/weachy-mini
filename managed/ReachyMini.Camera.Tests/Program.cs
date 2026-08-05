#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Program
    {
        private static async Task<int> Main()
        {
            PermissionTransitionsRemainExplicit();
            DiscoveryPublishesImmutableCapabilities();
            AvailabilityAndCalibrationRemainIndependent();
            InvalidCameraContractsFailClosed();
            AcquisitionLifecycleRemainsExplicit();
            FrameMetadataIsImmutableAndMonotonic();
            CameraSwitchCreatesANewSession();
            InvalidFrameContractsFailClosed();
            Rma100CameraCalibrationContracts.Run();
            Rma101AuthoritativeRotationContracts.Run();
            await Rma110VisionProviderContracts.RunAsync()
                .ConfigureAwait(false);
            Console.WriteLine("RMA-090/RMA-091/RMA-100 camera contracts passed.");
            return 0;
        }

        private static void PermissionTransitionsRemainExplicit()
        {
            var store = new ReachyCameraCapabilityStateStore();
            Equal(
                ReachyCameraPermissionState.NotRequested,
                store.Current.Permission,
                "initial permission");
            Equal(0, store.Current.PermissionRequestCount, "initial request count");
            Equal(0, store.Current.Cameras.Count, "initial camera count");

            store.MarkRequesting("first request");
            Equal(1, store.Current.PermissionRequestCount, "first request count");
            store.MarkDenied(permanent: false, "first denial");
            Equal(
                ReachyCameraPermissionState.Denied,
                store.Current.Permission,
                "recoverable denial");

            store.MarkRequesting("second request");
            Equal(2, store.Current.PermissionRequestCount, "second request count");
            store.MarkDenied(permanent: true, "do not ask again");
            Equal(
                ReachyCameraPermissionState.PermanentlyDenied,
                store.Current.Permission,
                "permanent denial");

            store.MarkRevoked("revoked after grant");
            Equal(
                ReachyCameraPermissionState.Revoked,
                store.Current.Permission,
                "revocation");
            Equal(0, store.Current.Cameras.Count, "revocation clears inventory");
        }

        private static void DiscoveryPublishesImmutableCapabilities()
        {
            var resolutions = new List<ReachyCameraResolution>
            {
                new ReachyCameraResolution(1920, 1080),
                new ReachyCameraResolution(1280, 720),
            };
            var cameras = new List<ReachyCameraCapability>
            {
                new ReachyCameraCapability(
                    "0",
                    ReachyDeviceCameraFacing.Rear,
                    90,
                    "full",
                    ReachyCameraAvailabilityState.Available,
                    resolutions,
                    new ReachyCameraIntrinsics(
                        ReachyCameraIntrinsicsSource.AndroidCalibration,
                        1200f,
                        1198f,
                        960f,
                        540f,
                        0f,
                        "checkerboard calibration remains available as an override"),
                    4032,
                    3024),
                new ReachyCameraCapability(
                    "1",
                    ReachyDeviceCameraFacing.Front,
                    270,
                    "limited",
                    ReachyCameraAvailabilityState.InUseOrUnavailable,
                    new[] { new ReachyCameraResolution(1280, 720) },
                    ReachyCameraIntrinsics.CreateUnavailable(
                        "persist a versioned checkerboard calibration"),
                    3264,
                    2448),
            };

            var store = new ReachyCameraCapabilityStateStore();
            store.MarkRequesting("request");
            store.ApplyDiscovery(cameras, "inventory complete");
            ReachyCameraCapabilitySnapshot snapshot = store.Current;

            Equal(
                ReachyCameraPermissionState.Granted,
                snapshot.Permission,
                "discovery permission");
            Equal(2, snapshot.Cameras.Count, "camera count");
            Equal(1, snapshot.FrontCameraCount, "front count");
            Equal(1, snapshot.RearCameraCount, "rear count");
            Equal(1, snapshot.AvailableCameraCount, "available count");
            Equal(1, snapshot.CalibratedCameraCount, "calibrated count");
            True(snapshot.SelectionAvailable, "selection availability");
            True(snapshot.AnyCameraAvailable, "runtime availability");
            True(snapshot.RequiresCalibrationFallback, "fallback requirement");

            cameras.Clear();
            resolutions.Clear();
            Equal(2, snapshot.Cameras.Count, "camera snapshot copy");
            Equal(
                2,
                snapshot.Cameras[0].AnalysisResolutions.Count,
                "resolution snapshot copy");
            Contains(snapshot.Summary, "front=1", "summary front count");
            Contains(snapshot.Summary, "intrinsics=1/2", "summary intrinsics count");
        }

        private static void AvailabilityAndCalibrationRemainIndependent()
        {
            var unavailableCalibrated = new ReachyCameraCapability(
                "rear-calibrated",
                ReachyDeviceCameraFacing.Rear,
                90,
                "level_3",
                ReachyCameraAvailabilityState.InUseOrUnavailable,
                new[] { new ReachyCameraResolution(640, 480) },
                new ReachyCameraIntrinsics(
                    ReachyCameraIntrinsicsSource.AndroidCalibration,
                    500f,
                    500f,
                    320f,
                    240f,
                    0f,
                    "manual calibration can supersede platform metadata"),
                1920,
                1080);
            var store = new ReachyCameraCapabilityStateStore();
            store.ApplyDiscovery(
                new[] { unavailableCalibrated },
                "camera currently unavailable");

            True(store.Current.SelectionAvailable, "preference remains selectable");
            False(store.Current.AnyCameraAvailable, "in-use camera not available");
            False(
                store.Current.RequiresCalibrationFallback,
                "platform intrinsics remain recorded while camera is in use");
        }

        private static void InvalidCameraContractsFailClosed()
        {
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraResolution(0, 720);
                },
                "zero-width resolution");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraIntrinsics(
                        ReachyCameraIntrinsicsSource.AndroidCalibration,
                        0f,
                        500f,
                        320f,
                        240f,
                        0f,
                        "fallback");
                },
                "invalid focal length");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraCapability(
                        "bad-orientation",
                        ReachyDeviceCameraFacing.Rear,
                        45,
                        "limited",
                        ReachyCameraAvailabilityState.Unknown,
                        Array.Empty<ReachyCameraResolution>(),
                        ReachyCameraIntrinsics.CreateUnavailable("fallback"),
                        0,
                        0);
                },
                "invalid orientation");

            ReachyCameraCapability camera = new ReachyCameraCapability(
                "duplicate",
                ReachyDeviceCameraFacing.Front,
                0,
                "limited",
                ReachyCameraAvailabilityState.Available,
                new[] { new ReachyCameraResolution(640, 480) },
                ReachyCameraIntrinsics.CreateUnavailable("fallback"),
                0,
                0);
            Throws<ArgumentException>(
                () =>
                {
                    _ = new ReachyCameraCapabilitySnapshot(
                        ReachyCameraPermissionState.Granted,
                        "duplicate inventory",
                        1,
                        new[] { camera, camera },
                        1UL);
                },
                "duplicate camera identifier");
        }

        private static void AcquisitionLifecycleRemainsExplicit()
        {
            var store = new ReachyCameraAcquisitionStateStore();
            Equal(
                ReachyCameraAcquisitionState.Stopped,
                store.Current.State,
                "initial acquisition state");
            False(store.Current.IsActive, "initial acquisition activity");

            store.BeginStart(
                11UL,
                ReachyDeviceCameraFacing.Rear,
                "rear-0",
                "binding CameraX");
            Equal(
                ReachyCameraAcquisitionState.Starting,
                store.Current.State,
                "starting state");
            True(store.Current.IsActive, "starting activity");
            store.MarkRunning("preview and analysis bound");
            Equal(
                ReachyCameraAcquisitionState.Running,
                store.Current.State,
                "running state");
            store.MarkPaused("application paused");
            Equal(
                ReachyCameraAcquisitionState.Paused,
                store.Current.State,
                "paused state");
            store.MarkRunning("application resumed");
            store.BeginStop("explicit stop");
            Equal(
                ReachyCameraAcquisitionState.Stopping,
                store.Current.State,
                "stopping state");
            store.MarkStopped("all CameraX use cases unbound");
            Equal(
                ReachyCameraAcquisitionState.Stopped,
                store.Current.State,
                "stopped state");
            Equal(0UL, store.Current.SessionId, "stopped session cleared");
            Equal(string.Empty, store.Current.CameraId, "stopped camera cleared");

            store.BeginStart(
                12UL,
                ReachyDeviceCameraFacing.Front,
                "front-1",
                "rapid restart");
            store.MarkRunning("rapid restart bound");
            store.MarkPermissionRevoked("permission revoked while active");
            Equal(
                ReachyCameraAcquisitionState.PermissionRevoked,
                store.Current.State,
                "permission-revoked state");
            False(store.Current.IsActive, "revocation stops acquisition");
        }

        private static void FrameMetadataIsImmutableAndMonotonic()
        {
            var store = new ReachyCameraAcquisitionStateStore();
            store.BeginStart(
                21UL,
                ReachyDeviceCameraFacing.Rear,
                "0",
                "start");
            store.MarkRunning("running");

            ReachyCameraFrameMetadata first = CreateFrame(
                sessionId: 21UL,
                sequence: 1UL,
                timestampNanoseconds: 1_000_000L,
                cameraId: "0",
                facing: ReachyDeviceCameraFacing.Rear,
                source: ReachyCameraFrameIntrinsicsSource.AndroidCalibration);
            True(store.PublishFrame(first), "first frame accepted");
            Equal(1UL, store.Current.AcceptedFrameCount, "first accepted count");
            Equal(first, store.Current.LatestFrame, "first latest frame");
            True(store.Current.LatestFrame!.Intrinsics.IsCalibrated, "calibrated frame");
            Contains(
                store.Current.LatestFrame.Summary,
                "format=Yuv420888",
                "frame summary format");

            False(store.PublishFrame(first), "duplicate poll ignored");
            Equal(0UL, store.Current.StaleFrameCount, "duplicate poll is not stale");

            ReachyCameraFrameMetadata second = CreateFrame(
                sessionId: 21UL,
                sequence: 2UL,
                timestampNanoseconds: 2_000_000L,
                cameraId: "0",
                facing: ReachyDeviceCameraFacing.Rear,
                source: ReachyCameraFrameIntrinsicsSource.UncalibratedPinholeEstimate);
            True(store.PublishFrame(second), "second frame accepted");
            Equal(2UL, store.Current.AcceptedFrameCount, "second accepted count");
            False(
                store.Current.LatestFrame!.Intrinsics.IsCalibrated,
                "fallback intrinsics remain explicit");

            ReachyCameraFrameMetadata stale = CreateFrame(
                sessionId: 21UL,
                sequence: 1UL,
                timestampNanoseconds: 3_000_000L,
                cameraId: "0",
                facing: ReachyDeviceCameraFacing.Rear,
                source: ReachyCameraFrameIntrinsicsSource.AndroidCalibration);
            False(store.PublishFrame(stale), "stale frame rejected");
            Equal(1UL, store.Current.StaleFrameCount, "stale count");
            Equal(2UL, store.Current.LatestFrame!.Sequence, "latest frame retained");
        }

        private static void CameraSwitchCreatesANewSession()
        {
            var store = new ReachyCameraAcquisitionStateStore();
            store.BeginStart(
                31UL,
                ReachyDeviceCameraFacing.Rear,
                "rear",
                "rear start");
            store.MarkRunning("rear running");
            store.PublishFrame(
                CreateFrame(
                    31UL,
                    1UL,
                    1_000L,
                    "rear",
                    ReachyDeviceCameraFacing.Rear,
                    ReachyCameraFrameIntrinsicsSource.AndroidCalibration));
            store.BeginStop("switch teardown");
            store.MarkStopped("rear unbound");
            store.BeginStart(
                32UL,
                ReachyDeviceCameraFacing.Front,
                "front",
                "front start");
            store.MarkRunning("front running");

            Equal(32UL, store.Current.SessionId, "front session");
            Equal("front", store.Current.CameraId, "front camera");
            Equal(0UL, store.Current.AcceptedFrameCount, "new session frame count");
            Equal(null, store.Current.LatestFrame, "new session latest frame");
            Throws<InvalidOperationException>(
                () =>
                {
                    store.PublishFrame(
                        CreateFrame(
                            31UL,
                            2UL,
                            2_000L,
                            "rear",
                            ReachyDeviceCameraFacing.Rear,
                            ReachyCameraFrameIntrinsicsSource.AndroidCalibration));
                },
                "old-session frame after switch");
        }

        private static void InvalidFrameContractsFailClosed()
        {
            ReachyCameraFrameIntrinsics intrinsics =
                new ReachyCameraFrameIntrinsics(
                    ReachyCameraFrameIntrinsicsSource.AndroidCalibration,
                    500f,
                    500f,
                    320f,
                    240f,
                    0f,
                    "Camera2 LENS_INTRINSIC_CALIBRATION");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraFrameCrop(0, 0, 0, 480);
                },
                "empty crop");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraFrameMetadata(
                        1UL,
                        1UL,
                        1L,
                        "0",
                        ReachyDeviceCameraFacing.Rear,
                        45,
                        0,
                        640,
                        480,
                        new ReachyCameraFrameCrop(0, 0, 640, 480),
                        ReachyCameraPixelFormat.Yuv420888,
                        intrinsics);
                },
                "invalid frame sensor orientation");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraFrameMetadata(
                        1UL,
                        1UL,
                        1L,
                        "0",
                        ReachyDeviceCameraFacing.Rear,
                        90,
                        0,
                        640,
                        480,
                        new ReachyCameraFrameCrop(0, 0, 641, 480),
                        ReachyCameraPixelFormat.Yuv420888,
                        intrinsics);
                },
                "crop outside buffer");
            Throws<ArgumentOutOfRangeException>(
                () =>
                {
                    _ = new ReachyCameraFrameIntrinsics(
                        ReachyCameraFrameIntrinsicsSource.AndroidCalibration,
                        float.NaN,
                        500f,
                        320f,
                        240f,
                        0f,
                        "bad intrinsics");
                },
                "non-finite frame intrinsics");
        }

        private static ReachyCameraFrameMetadata CreateFrame(
            ulong sessionId,
            ulong sequence,
            long timestampNanoseconds,
            string cameraId,
            ReachyDeviceCameraFacing facing,
            ReachyCameraFrameIntrinsicsSource source)
        {
            return new ReachyCameraFrameMetadata(
                sessionId,
                sequence,
                timestampNanoseconds,
                cameraId,
                facing,
                90,
                0,
                640,
                480,
                new ReachyCameraFrameCrop(0, 0, 640, 480),
                ReachyCameraPixelFormat.Yuv420888,
                new ReachyCameraFrameIntrinsics(
                    source,
                    500f,
                    500f,
                    320f,
                    240f,
                    0f,
                    source == ReachyCameraFrameIntrinsicsSource.AndroidCalibration
                        ? "Camera2 LENS_INTRINSIC_CALIBRATION"
                        : "uncalibrated active-array pinhole estimate"));
        }

        private static void Contains(string value, string expected, string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: '{value}' lacks '{expected}'.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected true.");
            }
        }

        private static void False(bool value, string label)
        {
            if (value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void Throws<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"Managed test failed for {label}: expected {typeof(TException).Name}.");
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using ReachyMini.AppState;

namespace ReachyMini.Camera.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            PermissionTransitionsRemainExplicit();
            DiscoveryPublishesImmutableCapabilities();
            AvailabilityAndCalibrationRemainIndependent();
            InvalidCameraContractsFailClosed();
            Console.WriteLine("RMA-090 camera capability tests passed.");
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

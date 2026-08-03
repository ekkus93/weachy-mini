#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyAndroidCameraDiscoveryTests
    {
        [Test]
        public void PermissionIsRequestedOnlyAfterUserAction()
        {
            GameObject gameObject = new GameObject("Rma090PermissionDiscovery");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var platform = new FakeCameraPlatform
                {
                    PermissionGranted = false,
                    ShowRationale = true,
                    GrantOnRequest = false,
                };
                discovery.ConfigurePlatformForTests(platform);

                Assert.That(platform.RequestCount, Is.EqualTo(0));
                Assert.That(
                    discovery.State.Current.Permission,
                    Is.EqualTo(ReachyCameraPermissionState.NotRequested));

                discovery.RequestAccessOrRefresh();
                Assert.That(platform.RequestCount, Is.EqualTo(1));
                Assert.That(
                    discovery.State.Current.Permission,
                    Is.EqualTo(ReachyCameraPermissionState.Denied));

                platform.ShowRationale = false;
                discovery.RequestAccessOrRefresh();
                Assert.That(platform.RequestCount, Is.EqualTo(2));
                Assert.That(
                    discovery.State.Current.Permission,
                    Is.EqualTo(ReachyCameraPermissionState.PermanentlyDenied));

                discovery.RequestAccessOrRefresh();
                Assert.That(platform.OpenSettingsCount, Is.EqualTo(1));
                Assert.That(platform.RequestCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GrantedPermissionPublishesInventoryAndRevocation()
        {
            GameObject gameObject = new GameObject("Rma090InventoryDiscovery");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var platform = new FakeCameraPlatform
                {
                    PermissionGranted = true,
                    DiscoveryJson =
                        "{\"status\":\"ok\",\"errorCode\":\"\",\"message\":\"two cameras\",\"cameras\":[" +
                        "{\"id\":\"0\",\"facing\":\"rear\",\"sensorOrientationDegrees\":90," +
                        "\"hardwareLevel\":\"full\",\"availability\":\"available\"," +
                        "\"analysisResolutions\":[{\"width\":1920,\"height\":1080},{\"width\":1280,\"height\":720}]," +
                        "\"intrinsics\":{\"available\":true,\"fx\":1200.0,\"fy\":1198.0,\"cx\":960.0,\"cy\":540.0,\"skew\":0.0}," +
                        "\"activeArrayWidth\":4032,\"activeArrayHeight\":3024,\"calibrationFallback\":\"checkerboard fallback\"}," +
                        "{\"id\":\"1\",\"facing\":\"front\",\"sensorOrientationDegrees\":270," +
                        "\"hardwareLevel\":\"limited\",\"availability\":\"in_use_or_unavailable\"," +
                        "\"analysisResolutions\":[{\"width\":1280,\"height\":720}]," +
                        "\"intrinsics\":{\"available\":false,\"fx\":0.0,\"fy\":0.0,\"cx\":0.0,\"cy\":0.0,\"skew\":0.0}," +
                        "\"activeArrayWidth\":3264,\"activeArrayHeight\":2448,\"calibrationFallback\":\"manual calibration required\"}]}"
                };
                discovery.ConfigurePlatformForTests(platform);

                ReachyCameraCapabilitySnapshot snapshot = discovery.State.Current;
                Assert.That(
                    snapshot.Permission,
                    Is.EqualTo(ReachyCameraPermissionState.Granted));
                Assert.That(snapshot.Cameras, Has.Count.EqualTo(2));
                Assert.That(snapshot.FrontCameraCount, Is.EqualTo(1));
                Assert.That(snapshot.RearCameraCount, Is.EqualTo(1));
                Assert.That(snapshot.AvailableCameraCount, Is.EqualTo(1));
                Assert.That(snapshot.CalibratedCameraCount, Is.EqualTo(1));
                Assert.That(snapshot.SelectionAvailable, Is.True);
                Assert.That(
                    snapshot.Cameras[0].AnalysisResolutions[0],
                    Is.EqualTo(new ReachyCameraResolution(1920, 1080)));
                Assert.That(
                    snapshot.Cameras[1].Intrinsics.Source,
                    Is.EqualTo(
                        ReachyCameraIntrinsicsSource.CalibrationFallbackRequired));

                platform.PermissionGranted = false;
                discovery.RefreshPermissionAndCapabilities();
                Assert.That(
                    discovery.State.Current.Permission,
                    Is.EqualTo(ReachyCameraPermissionState.Revoked));
                Assert.That(discovery.State.Current.Cameras, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CameraAccessErrorsRemainVisible()
        {
            GameObject gameObject = new GameObject("Rma090FaultDiscovery");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var platform = new FakeCameraPlatform
                {
                    PermissionGranted = true,
                    DiscoveryJson =
                        "{\"status\":\"error\",\"errorCode\":\"camera_in_use\",\"message\":\"another client owns the camera\",\"cameras\":[]}"
                };
                discovery.ConfigurePlatformForTests(platform);

                Assert.That(
                    discovery.State.Current.Permission,
                    Is.EqualTo(ReachyCameraPermissionState.Faulted));
                StringAssert.Contains(
                    "camera_in_use",
                    discovery.State.Current.Message);
                StringAssert.Contains(
                    "another client",
                    discovery.State.Current.Message);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private sealed class FakeCameraPlatform : IReachyDeviceCameraPlatform
        {
            public bool IsSupported => true;

            public bool PermissionGranted { get; set; }

            public bool GrantOnRequest { get; set; }

            public bool ShowRationale { get; set; }

            public int RequestCount { get; private set; }

            public int OpenSettingsCount { get; private set; }

            public int DiscoverCount { get; private set; }

            public string DiscoveryJson { get; set; } =
                "{\"status\":\"ok\",\"errorCode\":\"\",\"message\":\"no cameras\",\"cameras\":[]}";

            public bool HasCameraPermission()
            {
                return PermissionGranted;
            }

            public bool ShouldShowCameraPermissionRationale()
            {
                return ShowRationale;
            }

            public void RequestCameraPermission(Action granted, Action denied)
            {
                ++RequestCount;
                if (GrantOnRequest)
                {
                    PermissionGranted = true;
                    granted();
                }
                else
                {
                    denied();
                }
            }

            public string DiscoverCameraCapabilitiesJson()
            {
                ++DiscoverCount;
                return DiscoveryJson;
            }

            public void OpenApplicationSettings()
            {
                ++OpenSettingsCount;
            }

            public void Dispose()
            {
            }
        }
    }
}

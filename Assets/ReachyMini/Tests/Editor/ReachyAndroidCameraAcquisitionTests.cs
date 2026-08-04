#nullable enable

using System;
using NUnit.Framework;
using ReachyMini.AppState;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReachyMini.Tests
{
    public sealed class ReachyAndroidCameraAcquisitionTests
    {
        [Test]
        public void RapidStartStopAndFrontRearSwitchUseDistinctSessions()
        {
            GameObject gameObject = new GameObject("Rma091CameraSwitch");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new FakeDiscoveryPlatform();
                discovery.ConfigurePlatformForTests(discoveryPlatform);

                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new FakeAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);

                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                Assert.That(acquisitionPlatform.StartCount, Is.EqualTo(1));
                Assert.That(acquisitionPlatform.CameraId, Is.EqualTo("rear-0"));
                Assert.That(acquisitionPlatform.Width, Is.EqualTo(1280));
                Assert.That(acquisitionPlatform.Height, Is.EqualTo(720));
                ulong rearSession = acquisition.State.Current.SessionId;
                Assert.That(rearSession, Is.GreaterThan(0UL));

                acquisitionPlatform.NextSnapshot = RunningSnapshot(
                    checked((long)rearSession),
                    "rear-0",
                    "rear",
                    1L,
                    1_000_000L,
                    0);
                acquisition.RefreshNow();
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Running));
                Assert.That(
                    acquisition.State.Current.LatestFrame?.Sequence,
                    Is.EqualTo(1UL));

                acquisition.StartPreferred(ReachyCameraFacing.Front);
                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(1));
                Assert.That(acquisitionPlatform.StartCount, Is.EqualTo(2));
                Assert.That(acquisitionPlatform.CameraId, Is.EqualTo("front-1"));
                ulong frontSession = acquisition.State.Current.SessionId;
                Assert.That(frontSession, Is.GreaterThan(rearSession));
                Assert.That(acquisition.State.Current.LatestFrame, Is.Null);

                acquisitionPlatform.NextSnapshot = RunningSnapshot(
                    checked((long)frontSession),
                    "front-1",
                    "front",
                    1L,
                    2_000_000L,
                    90);
                acquisition.RefreshNow();
                Assert.That(
                    acquisition.State.Current.LatestFrame?.RotationDegrees,
                    Is.EqualTo(90));
                Assert.That(
                    acquisition.State.Current.LatestFrame?.LensFacing,
                    Is.EqualTo(ReachyDeviceCameraFacing.Front));

                for (int index = 0; index < 8; ++index)
                {
                    acquisition.StopAcquisition();
                    acquisition.StartPreferred(
                        index % 2 == 0
                            ? ReachyCameraFacing.Rear
                            : ReachyCameraFacing.Front);
                }
                acquisition.StopAcquisition();
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Stopped));
                Assert.That(acquisition.State.Current.SessionId, Is.EqualTo(0UL));
                Assert.That(acquisition.State.Current.CameraId, Is.Empty);
                Assert.That(acquisitionPlatform.StartCount, Is.EqualTo(10));
                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(10));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PauseResumeAndPermissionRevocationRemainVisible()
        {
            GameObject gameObject = new GameObject("Rma091CameraLifecycle");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new FakeDiscoveryPlatform();
                discovery.ConfigurePlatformForTests(discoveryPlatform);

                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new FakeAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong session = acquisition.State.Current.SessionId;
                acquisitionPlatform.NextSnapshot = RunningSnapshot(
                    checked((long)session),
                    "rear-0",
                    "rear",
                    1L,
                    1_000L,
                    0);
                acquisition.RefreshNow();

                gameObject.SendMessage("OnApplicationPause", true);
                Assert.That(acquisitionPlatform.PauseCount, Is.EqualTo(1));
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Paused));

                gameObject.SendMessage("OnApplicationPause", false);
                Assert.That(acquisitionPlatform.ResumeCount, Is.EqualTo(1));
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Running));

                discoveryPlatform.PermissionGranted = false;
                discovery.RefreshPermissionAndCapabilities();
                Assert.That(acquisitionPlatform.StopCount, Is.EqualTo(1));
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(
                        ReachyCameraAcquisitionState.PermissionRevoked));
                Assert.That(acquisition.State.Current.IsActive, Is.False);
                Assert.That(acquisition.DesiredActive, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DuplicatePollAndStaleMetadataDoNotReplaceLatestFrame()
        {
            GameObject gameObject = new GameObject("Rma091CameraBackpressure");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                discovery.ConfigurePlatformForTests(new FakeDiscoveryPlatform());
                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new FakeAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);
                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                ulong session = acquisition.State.Current.SessionId;

                acquisitionPlatform.NextSnapshot = RunningSnapshot(
                    checked((long)session),
                    "rear-0",
                    "rear",
                    4L,
                    4_000L,
                    270);
                acquisition.RefreshNow();
                acquisition.RefreshNow();
                Assert.That(
                    acquisition.State.Current.AcceptedFrameCount,
                    Is.EqualTo(1UL));
                Assert.That(
                    acquisition.State.Current.StaleFrameCount,
                    Is.EqualTo(0UL));

                acquisitionPlatform.NextSnapshot = RunningSnapshot(
                    checked((long)session),
                    "rear-0",
                    "rear",
                    3L,
                    5_000L,
                    0);
                acquisition.RefreshNow();
                Assert.That(
                    acquisition.State.Current.StaleFrameCount,
                    Is.EqualTo(1UL));
                Assert.That(
                    acquisition.State.Current.LatestFrame?.Sequence,
                    Is.EqualTo(4UL));
                Assert.That(
                    acquisition.State.Current.LatestFrame?.RotationDegrees,
                    Is.EqualTo(270));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void UnavailableCameraFailsClosedWithoutStartingPlatform()
        {
            GameObject gameObject = new GameObject("Rma091UnavailableCamera");
            try
            {
                ReachyAndroidCameraDiscovery discovery =
                    gameObject.AddComponent<ReachyAndroidCameraDiscovery>();
                var discoveryPlatform = new FakeDiscoveryPlatform
                {
                    DiscoveryJson =
                        "{\"status\":\"ok\",\"errorCode\":\"\",\"message\":\"rear busy\",\"cameras\":[" +
                        "{\"id\":\"rear-0\",\"facing\":\"rear\",\"sensorOrientationDegrees\":90," +
                        "\"hardwareLevel\":\"full\",\"availability\":\"in_use_or_unavailable\"," +
                        "\"analysisResolutions\":[{\"width\":1280,\"height\":720}]," +
                        "\"intrinsics\":{\"available\":true,\"fx\":900.0,\"fy\":900.0,\"cx\":640.0,\"cy\":360.0,\"skew\":0.0}," +
                        "\"activeArrayWidth\":4032,\"activeArrayHeight\":3024,\"calibrationFallback\":\"checkerboard fallback\"}]}"
                };
                discovery.ConfigurePlatformForTests(discoveryPlatform);
                ReachyAndroidCameraAcquisition acquisition =
                    gameObject.AddComponent<ReachyAndroidCameraAcquisition>();
                var acquisitionPlatform = new FakeAcquisitionPlatform();
                acquisition.ConfigurePlatformForTests(
                    discovery,
                    acquisitionPlatform);

                acquisition.StartPreferred(ReachyCameraFacing.Rear);
                Assert.That(acquisitionPlatform.StartCount, Is.EqualTo(0));
                Assert.That(
                    acquisition.State.Current.State,
                    Is.EqualTo(ReachyCameraAcquisitionState.Unavailable));
                StringAssert.Contains(
                    "No available rear camera",
                    acquisition.State.Current.Message);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static string RunningSnapshot(
            long sessionId,
            string cameraId,
            string facing,
            long sequence,
            long timestampNanoseconds,
            int rotationDegrees)
        {
            return
                "{\"status\":\"ok\",\"state\":\"Running\",\"errorCode\":\"\"," +
                "\"message\":\"frame metadata ready\",\"sessionId\":" + sessionId +
                ",\"cameraId\":\"" + cameraId + "\",\"facing\":\"" + facing +
                "\",\"analysisBackpressure\":\"keep_only_latest\"," +
                "\"previewSink\":\"private_discard_surface_until_rma092\"," +
                "\"cpuPixelCopyPerformed\":false,\"latestFrame\":{" +
                "\"sessionId\":" + sessionId +
                ",\"sequence\":" + sequence +
                ",\"timestampNanoseconds\":" + timestampNanoseconds +
                ",\"cameraId\":\"" + cameraId + "\",\"facing\":\"" + facing +
                "\",\"sensorOrientationDegrees\":90,\"rotationDegrees\":" +
                rotationDegrees +
                ",\"width\":1280,\"height\":720," +
                "\"crop\":{\"left\":0,\"top\":0,\"right\":1280,\"bottom\":720}," +
                "\"pixelFormat\":\"YUV_420_888\"," +
                "\"intrinsics\":{\"source\":\"android_calibration\"," +
                "\"fx\":900.0,\"fy\":900.0,\"cx\":640.0,\"cy\":360.0," +
                "\"skew\":0.0,\"coordinateSpace\":\"active_sensor_array\"," +
                "\"activeArrayLeft\":0,\"activeArrayTop\":0," +
                "\"activeArrayRight\":4032,\"activeArrayBottom\":3024," +
                "\"provenance\":\"Camera2 calibration\"}," +
                "\"imagePlanesAccessed\":false,\"cpuPixelCopyPerformed\":false}}";
        }

        private sealed class FakeDiscoveryPlatform :
            IReachyDeviceCameraPlatform
        {
            public bool IsSupported => true;

            public bool PermissionGranted { get; set; } = true;

            public string DiscoveryJson { get; set; } =
                "{\"status\":\"ok\",\"errorCode\":\"\",\"message\":\"front and rear available\",\"cameras\":[" +
                "{\"id\":\"rear-0\",\"facing\":\"rear\",\"sensorOrientationDegrees\":90," +
                "\"hardwareLevel\":\"full\",\"availability\":\"available\"," +
                "\"analysisResolutions\":[{\"width\":1280,\"height\":720}]," +
                "\"intrinsics\":{\"available\":true,\"fx\":900.0,\"fy\":900.0,\"cx\":640.0,\"cy\":360.0,\"skew\":0.0}," +
                "\"activeArrayWidth\":4032,\"activeArrayHeight\":3024,\"calibrationFallback\":\"checkerboard fallback\"}," +
                "{\"id\":\"front-1\",\"facing\":\"front\",\"sensorOrientationDegrees\":270," +
                "\"hardwareLevel\":\"limited\",\"availability\":\"available\"," +
                "\"analysisResolutions\":[{\"width\":960,\"height\":540}]," +
                "\"intrinsics\":{\"available\":false,\"fx\":0.0,\"fy\":0.0,\"cx\":0.0,\"cy\":0.0,\"skew\":0.0}," +
                "\"activeArrayWidth\":3264,\"activeArrayHeight\":2448,\"calibrationFallback\":\"manual calibration required\"}]}";

            public bool HasCameraPermission()
            {
                return PermissionGranted;
            }

            public bool ShouldShowCameraPermissionRationale()
            {
                return false;
            }

            public void RequestCameraPermission(Action granted, Action denied)
            {
                if (PermissionGranted)
                {
                    granted();
                }
                else
                {
                    denied();
                }
            }

            public string DiscoverCameraCapabilitiesJson()
            {
                return DiscoveryJson;
            }

            public void OpenApplicationSettings()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeAcquisitionPlatform :
            IReachyDeviceCameraAcquisitionPlatform
        {
            public bool IsSupported => true;

            public int StartCount { get; private set; }

            public int PauseCount { get; private set; }

            public int ResumeCount { get; private set; }

            public int StopCount { get; private set; }

            public long SessionId { get; private set; }

            public string CameraId { get; private set; } = string.Empty;

            public int Width { get; private set; }

            public int Height { get; private set; }

            public string NextSnapshot { get; set; } =
                "{\"status\":\"ok\",\"state\":\"Starting\",\"errorCode\":\"\",\"message\":\"binding\",\"sessionId\":1,\"cameraId\":\"rear-0\",\"facing\":\"rear\",\"analysisBackpressure\":\"keep_only_latest\",\"previewSink\":\"private_discard_surface_until_rma092\",\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";

            public string Start(
                long sessionId,
                string cameraId,
                int width,
                int height)
            {
                ++StartCount;
                SessionId = sessionId;
                CameraId = cameraId;
                Width = width;
                Height = height;
                return
                    "{\"status\":\"ok\",\"state\":\"Starting\",\"errorCode\":\"\"," +
                    "\"message\":\"binding\",\"sessionId\":" + sessionId +
                    ",\"cameraId\":\"" + cameraId +
                    "\",\"facing\":\"unknown\",\"analysisBackpressure\":\"keep_only_latest\"," +
                    "\"previewSink\":\"private_discard_surface_until_rma092\"," +
                    "\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
            }

            public string Pause()
            {
                ++PauseCount;
                return StateSnapshot("Paused", "paused");
            }

            public string Resume()
            {
                ++ResumeCount;
                return StateSnapshot("Running", "resumed");
            }

            public string Stop()
            {
                ++StopCount;
                return
                    "{\"status\":\"ok\",\"state\":\"Stopped\",\"errorCode\":\"\"," +
                    "\"message\":\"stopped\",\"sessionId\":0,\"cameraId\":\"\"," +
                    "\"facing\":\"unknown\",\"analysisBackpressure\":\"keep_only_latest\"," +
                    "\"previewSink\":\"private_discard_surface_until_rma092\"," +
                    "\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
            }

            public string Snapshot()
            {
                return NextSnapshot;
            }

            public void Dispose()
            {
            }

            private string StateSnapshot(string state, string message)
            {
                return
                    "{\"status\":\"ok\",\"state\":\"" + state +
                    "\",\"errorCode\":\"\",\"message\":\"" + message +
                    "\",\"sessionId\":" + SessionId +
                    ",\"cameraId\":\"" + CameraId +
                    "\",\"facing\":\"unknown\",\"analysisBackpressure\":\"keep_only_latest\"," +
                    "\"previewSink\":\"private_discard_surface_until_rma092\"," +
                    "\"cpuPixelCopyPerformed\":false,\"latestFrame\":null}";
            }
        }
    }
}
